# Verification — Windows 1.1

## 1.1 output speed (t/s) per agent

- `--self-test` **53/53** (new: Codex timing across two incremental reads — 20 s + 25 s, a 20-token reply not timed; Claude timing across reads, a fake `"uuid"`/`"timestamp"` inside tool-result text ignored, the same responses in a second file not timed again; Grok `apiDurationMs` with `modelCalls`, timing carried into daily rows and per-model rows; formatting and acceptance limits).
- Feasibility on this machine's logs before implementing (last 30 days, token-weighted / median): [redacted], [redacted], [redacted] with a [redacted].
- Engine vs Claude Code's own `cost-state` API time, per session: [redacted], [redacted], [redacted], [redacted], [redacted] t/s (the engine skips replies < 50 tokens). Two sessions differ for known reasons: one where short replies dominate ([redacted]), one whose `cost-state` covers only [redacted] of its output tokens.
- Codex rule checked per response on one rollout: where the engine is longer than an `item_completed`-based estimate (e.g. [redacted]), the response streamed a tool call (patch) for [redacted] after its reasoning, which `item_completed` does not mark until the tool runs; the engine's response-item timestamps include it.
- Production after upgrading (index version 4 rebuilt automatically, ~4 s): `codeusage cost --days 7` prints "[redacted]"; 30 days: [redacted] (older models, many via relay, were much slower), [redacted], Antigravity none (no durations in its records). Codex detail page (UI Automation): 输出速度 22.4 t/s; per model [redacted].
- Demo (`PrintWindow`): overview card footer "233.1M Token · 24.2 t/s"; Codex detail with the fourth figure, 费用 / Token / 速度 chart toggle ("最高 27.3 t/s"), model list speed; large size 速度 metric; third-party card "输出速度 71 t/s". Demo speeds are illustrative numbers.
- Not verified: OpenCode timing (no local data); Kimi / Pi / Copilot / Antigravity records carry no durations, so no speed is shown for them.

## 1.0.1 medium size without charts, surface colour palette

- `--self-test` **50/50** (new: hex parsing incl. `#abc`, HSV round trip for 6 colours, pure hues, WCAG contrast 21 : 1 for white/black, default text/background > 15 : 1, invalid `SurfaceColor` falls back to the default).
- Medium size (demo, `PrintWindow`): Codex (one window) shows the weekly row, pace, exact reset time and reset credits, no chart; Claude shows its two windows; Pi (no quota) shows 近 7 天 / 30 天 Token / 主要模型; DeepSeek shows its balance.
- Palette (demo, UI Automation, no mouse): settings → 背景颜色 opens the palette; preset 深海蓝 turns the panel and the settings window surface to exactly #0F1B2D (pixel read at 0 % transparency); typing #2a1218 in the hex field applies on focus change; Save writes `SurfaceColor: "#2A1218"` and the panel keeps it; a light colour (#B8C4D0) shows contrast 1.6 : 1 with the warning.
- Not verified: dragging in the colour square and strips with a physical mouse (the mouse handlers use capture inside the popup; only keyboard-free UI Automation paths were exercised), and how a tinted surface looks over the live DWM blur at high transparency (`PrintWindow` does not capture the backdrop).

Validated on Windows 11 Pro x64 ([redacted]), 2026-09-29 / 2026-09-30.

## 1.0 native usage engine, periods, interactive charts, background picture

- Build: both executables compile with no errors or warnings; `--self-test` **49/49** (new or rewritten: history from hourly indexes, price lookup and tiers, Codex reader and fast tier, Claude de-duplication — larger copy wins, progress lines, missing id — third-party cost from the index, exact window, unpriced tokens, Grok / Kimi / Pi line parsers).
- The package no longer contains `ccusage.exe` or CodexBar source; the upstream sources studied during development were moved out of `source/` into a local `reference/` folder that is not shipped. The only CodexBar files shipped are the provider logos in `icons/` (byte-identical to its `ProviderIcon-*.svg`, credited in `THIRD-PARTY-NOTICES.md`).
- **Engine parity on real data** (this machine, 2026-08-31 … 2026-09-30, [redacted]): ccusage 20.0.26 was run once as an external oracle and compared per day and per agent. Token components (fresh input, output, cache read, cache write) are **identical on all 51**; cost identical on 50. The one difference is today's Codex `gpt-6.1-sol` ([redacted] here, $0 in ccusage 20.0.26, whose table has no price for it). Totals: Claude [redacted] / [redacted], Codex [redacted] / [redacted], Antigravity [redacted] / [redacted], ZCode [redacted] / [redacted], Grok [redacted] / [redacted]. A cold scan of all agents takes about 4 s.
- UI (demo, UI Automation + `PrintWindow`): overview hero with period button, stacked chart and provider chips; card header (name, plan and refresh on one line; freshness and account right-aligned below); Codex detail usage block with period button, figures, composition, stacked model chart, model list and quota windows; clicking a bar pins its detail; 费用 / Token toggle; period picker (preset 当天 applied; calendar and hour grid; days outside the retained month disabled); background picture in full and medium sizes; settings 外观 card (transparency, picture, fill mode, dim); large compact size with period button and chart.
- Production (real data, UI Automation, no mouse): after the upgrade the old indexes were rebuilt automatically and `history.json` switched to engine `native-1`; all 50 past agent-days equal the earlier values (only today's still-growing Claude total differs). Codex 当天 → 13 hourly bars (00:00–12:00), [redacted], [redacted] — identical to ccusage for that day; Codex 近 30 天 → 30 daily bars, [redacted]; overview 近 24 小时 → 25 hourly bars. Ranges were set back to 30 days afterwards.
- Package: extracted zip passes `--self-test` 49/49, rebuilds from its own `source/Windows` (icons and `pricing.json` copied), starts and quits in `--demo`; no `data/`, `verification/`, `tools/` or `reference/`; no personal paths or account strings.
- **Not verified**: Pi, GitHub Copilot CLI, OpenCode and Kimi local logs (none on this machine; parsers are covered by fixture tests only); picture tiling with very large images on low-memory systems.

## 0.9 display sizes, settings window, card refresh and connect

- Build: both executables compile with no errors or warnings; `--self-test` 46/46 (new: solid → opaque Acrylic migration, per-size geometry normalisation, every size's default inside its resize limits).
- Chart filter: UI Automation reads the Codex card's automation runtime id before and after clicking the "图表：Codex" chip — unchanged, i.e. the provider cards (and their meters) are not rebuilt, only the hero card.
- Display sizes (demo, captured with `PrintWindow` at 0 % transparency so the backdrop does not show windows behind): small / medium / large × overview / Claude / Codex / Grok (not connected); the header size button → 中尺寸 gives a 360×180 window; resized geometries (small 250², medium 600×300, large 520×760) re-lay out (more rows, taller chart, token composition); a 100×100 small layout is clamped to the 160×160 minimum.
- Interactions (demo, UI Automation): headline click cycles Claude 5 小时 → 每周 (2/2); large row click expands the reset time and usage; per-provider refresh buttons invoke without error; switching pages via the icon strip.
- Connect inside a size: medium Grok → terminal-login panel; small and medium OpenCode → key box and 保存并连接 (demo disables saving); full panel OpenCode / Grok pages show the same panels.
- Settings window: opens as its own window titled "codeusagemonit 设置" with 显示尺寸 (小/中/大/完整), 背景材质 (毛玻璃/Mica) and 界面透明度.
- Not verified here: live key entry against real provider APIs from a card, the GitHub device flow end-to-end from a card, launching `codex login` / `grok login` in a real terminal (the demo disables these), and dragging window edges with a physical mouse (geometry was set through the saved layout instead).

## 0.5 widgets, CLI, opacity

- Desktop widgets (demo): small, medium and large added from Settings via UI Automation; each rendered (captured with `PrintWindow`, since they sit behind other windows); switching provider inside the medium and large widgets updates them (Claude two-window layout, DeepSeek balance, Pi local-only). Dragging and provider changes persisted to `settings.json`.
- Opacity slider: panel background brightness 69 at 25% and 24 at 100% (the opaque smoke colour), measured from a screen capture; Cancel keeps the saved 66%.
- CLI against real data: `status`, `cost --days 5`, `providers`, `thirdparty`, `status -p codex --json`; an unknown command exits with code 2.
- Not verified: the widget backdrop appearance at different opacities (`PrintWindow` does not capture DWM backdrops); Win+D behaviour.

## 0.5 additions (new providers, custom providers, chart filter)

- **43 self-tests passed**, adding fixtures for Copilot (percent_remaining, derived percent, placeholder and unlimited pools, monthly reset), Kimi (ratio pools, legacy counts, 5-hour rate-limit window, membership tier), OpenCode Go (percent aliases, relative resets, missing-field failure), GLM Coding Plan (unit multipliers, count-based percent, MCP allowance, error envelope), custom-provider validation (public HTTP, user info, two metric forms, missing limit, bad path, forbidden header, unknown key, no output) and mapping (array paths, string numbers, ratios, relative/epoch resets, currency path, missing fields).
- Custom provider, end to end against a local mock API on `127.0.0.1` (UI Automation, isolated preview data): a 302 response is rejected before any follow-up; a public `http://` URL is rejected before any request; Test maps used/limit, a ratio, the balance and the plan from the live response; Save lists the provider, gives it a tab and a quota card after refresh; the definition file holds no secret and the key file is DPAPI ciphertext.
- Demo screenshots checked: two-row tab strip with 14 tabs, chart filter chips (All → Claude only), the new Accounts & keys card, the custom provider list.
- **Not verified live**: Copilot device login, Kimi, OpenCode Go and GLM quota calls (no credentials configured on this machine). Their requests and field mappings follow the upstream CodexBar implementations and are covered by fixture tests only.

## 0.4 additions (third-party endpoints)

- **37 self-tests passed**, including: Codex buckets carry provider/model and the subscription window excludes relays; Claude de-duplication by id + requestId and by id + timestamp when the request id is missing; `<synthetic>` entries skipped; TOML provider/profile/base_url lookup; host normalisation and key fingerprints; timeline attribution (before tracking → unattributed, official → skipped, relay → endpoint, missing request id → suspected relay).
- Claude hourly index vs a fresh ccusage run: **identical on [redacted] with usage** (2026-08-31 … 2026-09-29), after adopting ccusage's timestamp fallback (one day had [redacted] without request id).
- Real data: both clients currently official; the app recorded that in `endpoints.json`, read [redacted] entries from CC Switch, and grouped [redacted] Claude tokens (2026-08-31 … 09-20) without request id as a suspected relay. Evidence that request id absence marks a relay: the same client version logged request ids against the official API on 09-22 but not on 09-02; some records carry `entrypoint: claude-desktop-3p` or non-Anthropic message ids.
- Live switch, isolated: with `CLAUDE_CONFIG_DIR` pointed at a scratch folder, rewriting its `settings.json` to a relay produced a new mark within 4 s (host normalised, `Since` equal to the file write time, key stored only as a 6-hex fingerprint). The user's real configuration was not modified.

## Automated

- Compiled with the installed .NET Framework C# compiler (`/langversion:5 /codepage:65001`).
- **33 self-tests passed** (`--self-test`), 27 carried over from 0.2 plus: ccusage token-composition fields, history merge (past days never shrink, today replaced, time-zone change resets), Codex log reader (duplicate `token_count` skipped, `info: null` ignored, >256 KB lines skipped, unfinished tail left for the next scan), exact-window clipping and per-day pricing, epoch-millisecond timestamps, quota-cache round trip.
- **7 UI Automation checks passed** on the isolated `--demo` preview (no mouse movement): every tab switches pages; Codex detail fields present; settings open inside the panel (no second window); material preview applies live and is reverted by back/cancel (DWM backdrop 1 → 3); Save persists scale, always-on-top and provider selection; a disabled provider leaves the tab strip; production `data/settings.json` hash unchanged.
- `verify-interaction.ps1` was updated for the in-panel settings page and saved with a BOM (the 0.2 file failed to parse in Windows PowerShell 5.1). Its physical mouse drag/resize steps were **not run** in this session because the desktop was in use.

## Data accuracy

- Codex hourly index vs ccusage daily totals: **identical on [redacted]** from 2026-09-08 to 2026-09-29 (difference 0 tokens each day).
- Incremental rescan: [redacted]; cold full scan of [redacted] of session logs: [redacted] (OS cache warm).
- CC Switch comparison for 2026-09-29: token components identical; cost difference fully explained by the GPT-6 Astra > 272K long-context tier (recomputed [redacted] vs ccusage [redacted]; flat pricing reproduces CC Switch's [redacted]). Official rule confirmed on the OpenAI model page.

## Visual

- Demo and real-data screenshots inspected: overview hero with stacked 30-day chart, provider cards, detail page (period table, composition, chart, quota windows), settings page, 32-bit tray icon at 16–48 px.

Personal caches and screenshots containing live usage are not included in distributable archives.
