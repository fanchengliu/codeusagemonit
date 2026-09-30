#!/usr/bin/env node
// Keeps pricing.json (USD per 1M tokens) in step with the public LiteLLM and models.dev
// catalogues, which track the API prices the vendors publish. Run daily by
// .github/workflows/pricing.yml; the app downloads the result (Pricing.SyncAsync).
//
//   node tools/update-pricing.mjs <pricing.json> <upstream-snapshot.json> [--litellm file] [--modelsdev file]
//
// Hand-curated values survive: a price is only changed when the upstream price it was
// taken from changes (compared with the snapshot of the previous run) and our value still
// equals that previous upstream value. Long-context tiers and Fast multipliers are left as
// they are. New models from the tracked vendors are added with their base prices.
// Nothing is written when a sanity check fails; the process exits with code 1.
import fs from 'node:fs/promises';

const LITELLM = 'https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json';
const MODELSDEV = 'https://models.dev/api.json';
// models.dev providers that are the vendors themselves (not resellers).
const OFFICIAL = ['anthropic', 'openai', 'google', 'xai', 'deepseek', 'moonshotai', 'zai', 'alibaba'];
// LiteLLM providers whose chat models are added when new; key style follows the table.
const ADD_FROM = { anthropic: 'bare', openai: 'bare', gemini: 'bare', xai: 'keep', deepseek: 'keep', moonshot: 'keep', dashscope: 'keep', zai: 'keep' };
const FAMILY = /^(claude-|gpt-|o\d|codex|gemini-|grok-|deepseek-|kimi-|moonshot-|glm-|qwen)/;
const SKIP = /(image|audio|tts|realtime|embedding|search|transcri|moderation|vision-preview|-ocr|livetranslate|omni)/;
const FIELDS = [['in', 'input_cost_per_token', 'input'], ['out', 'output_cost_per_token', 'output'], ['cr', 'cache_read_input_token_cost', 'cache_read'], ['cw', 'cache_creation_input_token_cost', 'cache_write']];

const args = process.argv.slice(2);
const [tablePath, snapPath] = args;
const opt = name => { const i = args.indexOf(name); return i > 0 ? args[i + 1] : null; };
if (!tablePath || !snapPath) { console.error('usage: update-pricing.mjs <pricing.json> <snapshot.json> [--litellm file] [--modelsdev file]'); process.exit(2); }

const load = async (file, url) => JSON.parse(file ? await fs.readFile(file, 'utf8') : await (await fetch(url, { headers: { 'User-Agent': 'codeusagemonit-pricing' } })).text());
const round = v => +(+v).toFixed(6);
const bare = k => k.split('/').pop();

const table = JSON.parse(await fs.readFile(tablePath, 'utf8'));
let snapshot = {}; try { snapshot = JSON.parse(await fs.readFile(snapPath, 'utf8')); } catch { }
const litellm = await load(opt('--litellm'), LITELLM);
const modelsdev = await load(opt('--modelsdev'), MODELSDEV);

// Upstream prices for one of our keys from a given source ("litellm:<key>" or "modelsdev:<provider>").
function fromSource(src, key) {
  const [kind, ref] = [src.slice(0, src.indexOf(':')), src.slice(src.indexOf(':') + 1)];
  const out = {};
  if (kind === 'litellm') { const e = litellm[ref]; if (!e || e.input_cost_per_token == null || e.output_cost_per_token == null) return null; for (const [f, l] of FIELDS) if (e[l] != null) out[f] = round(e[l] * 1e6); return out; }
  const m = modelsdev[ref]?.models?.[bare(key)]; if (!m?.cost || m.cost.input == null || m.cost.output == null) return null;
  for (const [f, , d] of FIELDS) if (m.cost[d] != null) out[f] = round(m.cost[d]); return out;
}
function sourcesFor(key) {
  const b = bare(key), list = [];
  for (const c of [key, b, 'gemini/' + b, 'xai/' + b, 'deepseek/' + b, 'moonshot/' + b, 'dashscope/' + b, 'zai/' + b, 'anthropic/' + b, 'openai/' + b]) if (litellm[c]?.input_cost_per_token != null) list.push('litellm:' + c);
  for (const p of OFFICIAL) if (modelsdev[p]?.models?.[b]?.cost) list.push('modelsdev:' + p);
  return [...new Set(list)];
}
// The source a key follows: kept from the last run; otherwise the first whose base
// prices equal ours (that is where the value came from); otherwise the first available.
function pick(key, ours) {
  const kept = snapshot[key]?.src; if (kept && fromSource(kept, key)) return kept;
  const all = sourcesFor(key);
  return all.find(s => { const u = fromSource(s, key); return u && u.in === ours.in && u.out === ours.out; }) ?? all[0] ?? null;
}

const models = table.models, before = Object.keys(models).length, changes = [], added = [], nextSnap = {};
for (const [key, ours] of Object.entries(models)) {
  const src = pick(key, ours); if (!src) continue;
  const up = fromSource(src, key); nextSnap[key] = { src, ...up };
  const prev = snapshot[key]; if (!prev || prev.src !== src) continue;
  for (const [f] of FIELDS) {
    if (up[f] == null || prev[f] == null || up[f] === prev[f]) continue;
    // Follow upstream only where our value still is the previous upstream value.
    if ((ours[f] ?? null) !== prev[f]) continue;
    changes.push({ key, field: f, from: ours[f], to: up[f] }); ours[f] = up[f];
  }
}
const known = new Set(Object.keys(models).map(k => bare(k)));
for (const [key, e] of Object.entries(litellm)) {
  const style = ADD_FROM[e.litellm_provider]; if (!style || !['chat', 'responses'].includes(e.mode)) continue;
  const name = bare(key); if (!FAMILY.test(name) || SKIP.test(name) || key.startsWith('ft:') || known.has(name)) continue;
  if (e.input_cost_per_token == null || e.output_cost_per_token == null) continue;
  const ourKey = style === 'bare' ? name : key.includes('/') ? key : e.litellm_provider + '/' + key;
  if (models[ourKey]) continue;
  const m = {}; for (const [f, l] of FIELDS) if (e[l] != null) m[f] = round(e[l] * 1e6);
  const tier = Object.keys(e).map(k => /^input_cost_per_token_above_(\d+)k_tokens$/.exec(k)).find(Boolean);
  if (tier) {
    const at = +tier[1], l = { at: at * 1000 }; if (e.litellm_provider !== 'openai') l.marginal = true;
    for (const [f, lf] of FIELDS) { const v = e[`${lf}_above_${at}k_tokens`]; if (v != null) l[f] = round(v * 1e6); }
    m.long = l;
  }
  models[ourKey] = m; known.add(name); added.push(ourKey); nextSnap[ourKey] = { src: 'litellm:' + key, ...Object.fromEntries(FIELDS.filter(([f]) => m[f] != null).map(([f]) => [f, m[f]])) };
}

// Sanity checks before anything is written.
const after = Object.keys(models).length, problems = [];
if (after < 100 || after < before * .9) problems.push(`model count ${before} → ${after}`);
if (changes.length > 60) problems.push(`${changes.length} price changes in one run`);
for (const c of changes) if (c.from > 0 && (c.to / c.from > 10 || c.to / c.from < .1)) problems.push(`${c.key}.${c.field} ${c.from} → ${c.to}`);
if (added.length > 80) problems.push(`${added.length} new models in one run`);
if (problems.length) { console.error('Refusing to update pricing.json:\n  ' + problems.join('\n  ')); process.exit(1); }

const sorted = o => Object.fromEntries(Object.keys(o).sort().map(k => [k, o[k]]));
if (changes.length || added.length) {
  // Minute precision, so two runs on one day still order correctly (the app compares it).
  table.updated = new Date().toISOString().slice(0, 16) + 'Z';
  table.models = sorted(models);
  // Same layout as the hand-made file: one model per line.
  const lines = Object.entries(table.models).map(([k, v]) => `    ${JSON.stringify(k)}: ${JSON.stringify(v)}`);
  const head = Object.entries(table).filter(([k]) => k !== 'models').map(([k, v]) => `  ${JSON.stringify(k)}: ${JSON.stringify(v)}`);
  await fs.writeFile(tablePath, '{\n' + head.join(',\n') + ',\n  "models": {\n' + lines.join(',\n') + '\n  }\n}\n');
}
await fs.writeFile(snapPath, JSON.stringify(sorted(nextSnap), null, 0).replace(/},"/g, '},\n"') + '\n');
console.log(`pricing.json: ${after} models, ${changes.length} price change(s), ${added.length} new model(s)${changes.length || added.length ? ', updated ' + table.updated : ', unchanged'}`);
for (const c of changes) console.log(`  ${c.key}.${c.field}: ${c.from} → ${c.to}`);
for (const k of added) console.log(`  + ${k}`);
