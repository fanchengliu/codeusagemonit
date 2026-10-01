'use strict';
/* codeusagemonit website v3. Figures in the demos are illustrative demo data, consistent with the
   app's own --demo mode. The pricing table is the real pricing.json. The replica window speaks the
   app's language (Simplified Chinese); the page around it follows the language switch. */

// ── Providers ─────────────────────────────────────────────────────────
// c: the app's dark-theme colour (the terminal is always dark). Elsewhere the page uses var(--p-<id>).
const P = {
  codex: { name: 'Codex', c: '#63D5E5' }, claude: { name: 'Claude', c: '#E8AB8C' }, cursor: { name: 'Cursor', c: '#80DDAB' },
  antigravity: { name: 'Antigravity', c: '#B8A0F5' }, deepseek: { name: 'DeepSeek', c: '#7FA9FF' }, grok: { name: 'Grok', c: '#D8DEE9' },
  copilot: { name: 'Copilot', c: '#E58FD0' }, kimi: { name: 'Kimi', c: '#F4C95D' }, opencode: { name: 'OpenCode', c: '#A6D96A' },
  zcode: { name: 'ZCode', c: '#F2874E' }, pi: { name: 'Pi', c: '#A9B4C6' }
};
const IDS = Object.keys(P);
const cv = id => `var(--p-${id})`;
const svgOf = id => { const g = ICONS[id]; return g ? `<svg viewBox="${g.vb}" fill="${g.fill}"${g.rule ? ` fill-rule="${g.rule}"` : ''} aria-hidden="true">${g.inner}</svg>` : ''; };
const icon = (id, extra = '') => `<span class="pi ${extra}" style="--c:${cv(id)}">${svgOf(id)}</span>`;
const esc = s => String(s).replace(/[&<>"]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch]));
const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
const $ = id => document.getElementById(id);
const sleep = ms => new Promise(r => setTimeout(r, ms));

// ── Formatting ────────────────────────────────────────────────────────
const trim = (v, d) => String(+v.toFixed(d));
const usd = v => '$' + v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const compact = n => n >= 1e9 ? trim(n / 1e9, 2) + 'B' : n >= 1e6 ? trim(n / 1e6, 2) + 'M' : n >= 1e3 ? trim(n / 1e3, 1) + 'K' : String(Math.round(n));
const tps = v => (v >= 100 ? Math.round(v) : trim(v, 1)) + ' t/s';
const int = v => Math.round(v).toLocaleString('en-US');
const pad2 = n => String(n).padStart(2, '0');
const FMT = { usd, compact, tps, int };
function countTo(el, to, fmt, from) {
  if (!el) return;
  if (reduced || !isFinite(to)) { el.textContent = fmt(to); el._v = to; return; }
  const start = performance.now(), dur = 700, a = from ?? el._v ?? 0; el._v = to;
  const step = now => { const k = Math.min(1, (now - start) / dur), e = 1 - Math.pow(1 - k, 3); el.textContent = fmt(a + (to - a) * e); if (k < 1) requestAnimationFrame(step); };
  requestAnimationFrame(step);
  // Background tabs pause animation frames; make sure the final value lands.
  clearTimeout(el._t); el._t = setTimeout(() => { if (el._v === to) el.textContent = fmt(to); }, dur + 150);
}
const countAll = rootEl => rootEl.querySelectorAll('[data-to]').forEach(el => countTo(el, +el.dataset.to, FMT[el.dataset.fmt] || int, 0));

// ── Theme: light / dark / follow the system (default) ─────────────────
const root = document.documentElement;
const lightMq = matchMedia('(prefers-color-scheme: light)');
let themePref = root.getAttribute('data-theme-pref') || 'system';
const resolveTheme = pref => pref === 'system' ? (lightMq.matches ? 'light' : 'dark') : pref;
function paintTheme() {
  root.setAttribute('data-theme', resolveTheme(themePref)); root.setAttribute('data-theme-pref', themePref);
  document.querySelectorAll('[data-theme-set]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.themeSet === themePref)));
}
function setTheme(pref, origin) {
  const before = root.getAttribute('data-theme'); themePref = pref;
  try { localStorage.setItem('codeusagemonit-theme', pref); } catch { }
  if (before === resolveTheme(pref) || reduced || !document.startViewTransition || !origin) { paintTheme(); return; }
  const r = origin.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
  const radius = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y));
  const tr = document.startViewTransition(paintTheme);
  tr.ready.then(() => root.animate({ clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
    { duration: 560, easing: 'cubic-bezier(.2,.75,.2,1)', pseudoElement: '::view-transition-new(root)' })).catch(() => { });
}
$('theme').addEventListener('click', e => { const b = e.target.closest('[data-theme-set]'); if (b) setTheme(b.dataset.themeSet, b); });
lightMq.addEventListener('change', () => { if (themePref === 'system') paintTheme(); });
paintTheme();

// ── i18n ──────────────────────────────────────────────────────────────
const zh = {};
document.querySelectorAll('[data-i18n]').forEach(el => { zh[el.dataset.i18n] = el.textContent; });
document.querySelectorAll('[data-i18n-ph]').forEach(el => { zh[el.dataset.i18nPh] = el.placeholder; });
Object.assign(zh, {
  copied: '已复制', copyFailed: '复制失败，请手动选择', copyLabel: '复制',
  tipCost: '费用', tipTokens: 'Token', tipReq: '请求', tipSpeed: '输出速度',
  noteDaily: '每日', noteHourly: '每小时', noteSpeed: '输出速度按 Token 加权：Σ输出 ÷ Σ耗时',
  capQuota: '额度', capLocal: '本机用量', capSpeed: '输出速度', docs: '连接说明 →', pdConnect: '连接', pdQuota: '额度',
  customName: '自定义接口',
  vSmall: '一个大数字、一条额度条，占一个图标的位置。点数字在额度窗口之间切换，滚轮或 ←/→ 翻页。',
  vMedium: '左边是主额度，右边是其他窗口；只有一个窗口时补充节奏、重置时间和限额重置额度。',
  vLarge: '完整的额度窗口与节奏估算，加上本机用量：时间段、费用、Token、请求、速度和可以点的柱状图。',
  vFull: '所有平台的卡片、每个平台的详情页、第三方中转站页和设置。拖动边缘可以随意调整大小。',
  sSmallShort: '一个数字，一条额度条', sMediumShort: '主额度加其他窗口', sLargeShort: '额度、节奏和本机用量', sFullShort: '全部平台、详情页与设置',
  keySmall: '小', keyMedium: '中', keyLarge: '大', keyFull: '全',
  viewerPrev: '上一张', viewerNext: '下一张', viewerClose: '关闭',
  pgTop: '首页', pgProviders: '接入平台', pgConsole: '额度与用量', pgSizes: '尺寸设计', pgInstall: '安装', pgTrust: '隐私与问题', pgDownload: '下载',
  themeLight: '浅色', themeDark: '深色', themeSystem: '跟随系统', menu: '菜单',
  vendorAll: '全部', priceNone: '没有匹配的模型', tagLong: '长上下文 >{k}K', defaultCache: '官方未单列，按输入价的 10% 估算',
  priceTotal: '共 {n} 款 · 可滚动查看', priceMatch: '{n} 款匹配', tagUse: '{tool} 在用',
  relayNote: '<b>{name}</b> · 近 30 天 {tok} Token · {n} 次请求 · {speed}',
  relayOfficial: '官方', relayA: '中转 A', relayB: '中转 B',
  instScoopIntro: '先把本仓库加为 bucket，再安装。', instScoop1: '添加 bucket', instScoop2: '安装', instScoop3: '以后升级', optional: '可选',
  instFootScoop: '会创建开始菜单快捷方式，并把 codeusage 加进 PATH；data 放在 Scoop 的 persist 目录，升级不丢。还没装 Scoop 时，先运行 <code>irm get.scoop.sh | iex</code>。',
  instPsIntro: '不装 Scoop 也行：一条命令从 GitHub Releases 下载最新版并装好。', instPsRun: '在 PowerShell 里运行',
  instPs2: '装到 %LOCALAPPDATA%\\Programs\\codeusagemonit，不需要管理员权限', instPs3: '把 codeusage 加进用户 PATH，创建开始菜单快捷方式', instPs4: '再运行一次就是升级，data 文件夹保留',
  instFootPs: '卸载时删除 %LOCALAPPDATA%\\Programs\\codeusagemonit，并从用户 PATH 里去掉它。',
  instSetupIntro: '图形界面安装，适合不常用终端的人。', instSetup1: '下载安装程序', instSetup2: '按向导选择目录和选项', instSetup3: '完成后从开始菜单打开',
  instSetupB1: '默认装到 %LOCALAPPDATA%\\Programs\\codeusagemonit，也可以换目录', instSetupB2: '桌面快捷方式、开机启动和 PATH 都可选',
  instSetupBtn: '下载 codeusagemonit-setup-1.0.0.exe', instZipLink: '或者下载免安装 zip',
  instFootSetup: '设置保存在 %LOCALAPPDATA%\\codeusagemonit，升级保留；在“Windows 设置 → 应用”里卸载，删除设置前会询问。',
  instCliIntro: '装好后终端里直接用 codeusage。点一条命令，右边就运行它。',
  instFootCli: '加上 <code>--json</code> 输出 JSON，接进脚本或状态栏。<code>codeusage help</code> 查看全部命令。',
  cmdStatus: '缓存里的额度与用量，不联网', cmdCost: '每天的费用与 Token，以及各工具的速度', cmdThird: '第三方接口的用量与速度',
  winScoop: 'Windows PowerShell · Scoop', winPs: 'Windows PowerShell · install.ps1', winSetup: '安装 - codeusagemonit 1.0.0', winCli: 'Windows PowerShell · codeusage',
  tipRefresh: '刷新', tipSize: '四种尺寸', tipPin: '置顶', tipSettings: '设置', tipClose: '收起到托盘',
  toastRefreshed: '已刷新 · 所有平台刚刚更新', toastPinOn: '已置顶：会盖在其他窗口上面', toastPinOff: '已取消置顶', toastGrok: 'Grok 已连接（演示）',
  toastSize: '右键窗口也能切换小 / 中 / 大 / 完整', toastAuto: '失焦自动收起：{s}', on: '开', off: '关'
});
const en = {
  skip: 'Skip to content', navProviders: 'Providers', navConsole: 'Quotas & usage', navSizes: 'Sizes', navInstall: 'Install', navTrust: 'Privacy & FAQ', navDownload: 'Download',
  chip: 'Release 1.0 · multi-device sync, English and Chinese', heroA: 'Every AI quota,', heroB: 'at a glance.',
  heroLead: 'Remaining quota, reset times, token usage and output speed for Codex, Claude, Cursor and 8 more AI coding tools, in one small window in your Windows tray.',
  download: 'Download installer', termInstall: 'Install from the terminal', factWin: 'Windows', factLocal: 'Counted on your PC only', factSource: 'View source',
  demoTag: 'Demo', demoFoot: 'Demo data · no account read', trayBack: 'In the tray · click to open',
  stageHint: 'Everything here is clickable: tools, quotas, periods, bars and the buttons at the top right',
  eyProviders: 'Providers', pA: '11 coding tools supported,', pB: 'plus your own endpoint', pLead: 'Reuses the sign-in each tool already has: CLI login, editor session or API key. No browser cookies, no stored passwords.',
  eyConsole: 'Quotas · usage · pricing', bA: 'What’s left, what it cost, how fast it ran,', bB: 'in one panel',
  bLead: 'Quotas in segments, with your pace read against the reset; usage straight from local session logs, every request priced at the official API rate, and the price table synced daily.',
  pToday: 'Today', p7: '7 days', p30: '30 days', colQuota: 'Quotas · left / reset / pace', boardHint: 'Click a quota for details · the tick marks an even pace to the reset', colUsage: 'Usage · local logs',
  fCost: 'API-equivalent cost', fReq: 'Requests', fSpeed: 'Output speed', demoData: 'Demo data',
  prA: 'Accurate numbers,', prB: 'official prices', syncOk: 'In sync', syncDate: 'prices as of', syncEvery: 'Checked every 24 hours',
  priceTitle: 'The latest models, priced in sync with the vendors', priceSub: 'Official API prices, including long-context tiers, cache reads and writes, and fast-mode multipliers.',
  priceSearch: 'Search models, e.g. gpt-6, opus, gemini', thModel: 'Model', thIn: 'Input', thOut: 'Output', thCache: 'Cache read', priceUnit: 'USD per 1M tokens · vendors’ official prices, compiled by LiteLLM and models.dev',
  speedTitle: 'Real output speed', speedSub: 'Output tokens ÷ the whole request time, including time to first token and relay latency, per tool. Click a row to switch to that tool.',
  relayTitle: 'Relays add up too', relaySub: 'Switch between third-party providers as you like; every call is still attributed to its endpoint, clearly.', relayOfficial: 'Official', relayA: 'Relay A', relayB: 'Relay B',
  eySizes: 'Size design', sA: 'Four sizes.', sB: 'One window.', sLead: 'Right-click to switch between small, medium, large and full. Compact sizes sit on the desktop layer, out of your way; every size can be resized.',
  sFull: 'Full', sLarge: 'Large', sMedium: 'Medium', sSmall: 'Small', viewerTry: 'Buttons, tools and bars inside the window all respond.',
  eyInstall: 'Install & CLI', cA: 'One command to install,', cB: 'the same numbers in your terminal',
  cLead: 'Scoop, PowerShell or the installer. The bundled codeusage command shares the app’s cache and settings, and ‑‑json feeds scripts or a status bar.',
  recommended: 'Recommended', instSetup: 'Installer', instCli: 'Commands', replay: 'Replay', copy: 'Copy command',
  eyPrivacy: 'Privacy', vA: 'Your data stays', vB: 'on your PC',
  vText: 'Only token counts, requests, durations and model names are recorded on your PC, never conversation content; each tool’s sign-in stays in its own folder and is read, not copied; the price table refreshes itself every day.',
  v1t: 'Numbers only', v1: 'Only tokens, request counts, durations and model names go into a local hourly index. No conversation content.',
  v2t: 'Sign-ins stay put', v2: 'Each tool’s login tokens stay in its own folder. They are read, never copied.',
  v3t: 'Encrypted keys', v3: 'API keys you enter are encrypted with Windows DPAPI; only your Windows user can decrypt them.',
  v4t: 'No telemetry', v4: 'No server of our own. Quota checks go only to each provider; the price table is fetched from GitHub once a day with no account or usage data, and can be turned off in settings.',
  eyFaq: 'FAQ',
  fq1: 'How do I install it? What does it need?', fa1: 'Download the installer (choose a folder and a Start menu shortcut; desktop, startup and PATH are optional; uninstall from Windows Settings → Apps), or use Scoop, the PowerShell one-liner, or the zip extracted into a writable folder. The default needs no admin rights. No Node.js; the .NET Framework 4.8 that ships with Windows is enough.',
  fq7: 'How do I update or uninstall?', fa7: 'Installer: run the new installer; settings stay in %LOCALAPPDATA%\\codeusagemonit. Uninstall from Windows Settings → Apps, which asks before deleting settings. Scoop: scoop update codeusagemonit / scoop uninstall codeusagemonit; data lives in Scoop’s persist folder. PowerShell script: run it again to update; to uninstall, delete %LOCALAPPDATA%\\Programs\\codeusagemonit and remove it from your user PATH.',
  fq2: 'Edge says the file “isn’t commonly downloaded”, or Windows says it “protected your PC”?', fa2: 'That is Windows’ reputation check for new files, not a virus alert: the installer is not code-signed yet, and a newly released version with few downloads gets these prompts in Edge and SmartScreen. It is built from the open source, and the release page has SHA256SUMS.txt to check it. In Edge, open “…” on the download → Keep → Show more → Keep anyway; when running it, click “More info → Run anyway”. Installing with Scoop or the PowerShell one-liner usually avoids these prompts.',
  fq3: 'Do I have to sign in to every account again?', fa3: 'No. Codex, Claude Code, Cursor, Antigravity and Grok reuse the sign-in already on your PC; DeepSeek shows local usage from DeepSeek Harness without a key (add an API key for the balance); Kimi, OpenCode and ZCode take an API key; Copilot uses GitHub device login.',
  fq9: 'Can I see two computers’ usage together?', fa9: 'Yes. In Settings › Data, export one computer’s usage as SQL and import it on the other: it is added to what is there, and importing twice, or the same logs already present, is de-duplicated automatically. Or point several computers at one WebDAV folder (Nextcloud, Jianguoyun…) to sync. Only tokens, requests, costs and durations travel, never conversations or keys.',
  fq8: 'How does the price table stay in sync with official prices?', fa8: 'The repository’s pricing.json is rebuilt every day by GitHub Actions from LiteLLM and models.dev, which track the API prices each vendor publishes. The app checks every 24 hours, downloads and validates a newer table and recalculates with it; if the download fails or you turn it off in settings, it keeps the built-in prices.',
  dlTitle: 'Join in now!', dlMeta: 'Completely free · for Windows · Version 1.0.0', dlBtn: 'Download the latest installer', feedback: 'Feedback', notices: 'Notices',
  footerNote: 'An independent open-source project, not affiliated with OpenAI, Anthropic, Cursor or any other provider. Names and logos belong to their owners.',
  copied: 'Copied', copyFailed: 'Copy failed — select it manually', copyLabel: 'Copy',
  tipCost: 'Cost', tipTokens: 'Tokens', tipReq: 'Requests', tipSpeed: 'Output speed',
  noteDaily: 'Daily', noteHourly: 'Hourly', noteSpeed: 'Speed is token-weighted: Σ output ÷ Σ time',
  capQuota: 'Quotas', capLocal: 'Local usage', capSpeed: 'Output speed', docs: 'How to connect →', pdConnect: 'Connects', pdQuota: 'Quotas', customName: 'Custom endpoint',
  vSmall: 'One big number and one meter, the footprint of an icon. Click the number to cycle quota windows; scroll or ←/→ to page.',
  vMedium: 'The main quota on the left, other windows on the right; a single window adds pace, reset time and reset credits.',
  vLarge: 'Full quota windows with pace, plus local usage: period, cost, tokens, requests, speed and a clickable chart.',
  vFull: 'Cards for every provider, a detail page for each, the third-party endpoints page and settings. Drag the edges to any size.',
  sSmallShort: 'One number, one meter', sMediumShort: 'Main quota plus the other windows', sLargeShort: 'Quotas, pace and local usage', sFullShort: 'Every provider, detail pages, settings',
  keySmall: 'S', keyMedium: 'M', keyLarge: 'L', keyFull: 'F',
  viewerPrev: 'Previous', viewerNext: 'Next', viewerClose: 'Close',
  pgTop: 'Top', pgProviders: 'Providers', pgConsole: 'Quotas & usage', pgSizes: 'Size design', pgInstall: 'Install', pgTrust: 'Privacy & FAQ', pgDownload: 'Download',
  themeLight: 'Light', themeDark: 'Dark', themeSystem: 'Match system', menu: 'Menu',
  vendorAll: 'All', priceNone: 'No matching models', tagLong: 'long ctx >{k}K', defaultCache: 'Not listed; estimated at 10% of the input price',
  priceTotal: '{n} models · scroll for more', priceMatch: '{n} matches', tagUse: 'used by {tool}',
  relayNote: '<b>{name}</b> · 30 days {tok} tokens · {n} requests · {speed}',
  instScoopIntro: 'Add this repository as a bucket, then install.', instScoop1: 'Add the bucket', instScoop2: 'Install', instScoop3: 'Update later', optional: 'optional',
  instFootScoop: 'You get a Start menu shortcut and codeusage on PATH; data lives in Scoop’s persist directory, so updates keep it. No Scoop yet? Run <code>irm get.scoop.sh | iex</code> first.',
  instPsIntro: 'No Scoop needed: one command downloads the latest release from GitHub and installs it.', instPsRun: 'Run in PowerShell',
  instPs2: 'Installs to %LOCALAPPDATA%\\Programs\\codeusagemonit, no admin rights', instPs3: 'Adds codeusage to your user PATH and a Start menu shortcut', instPs4: 'Run it again to update; the data folder is kept',
  instFootPs: 'To uninstall, delete %LOCALAPPDATA%\\Programs\\codeusagemonit and remove it from your user PATH.',
  instSetupIntro: 'A graphical installer, for when you’d rather not use a terminal.', instSetup1: 'Download the installer', instSetup2: 'Pick a folder and options in the wizard', instSetup3: 'Open it from the Start menu',
  instSetupB1: 'Installs to %LOCALAPPDATA%\\Programs\\codeusagemonit by default; any folder works', instSetupB2: 'Desktop shortcut, start with Windows and PATH are optional',
  instSetupBtn: 'Download codeusagemonit-setup-1.0.0.exe', instZipLink: 'or get the portable zip',
  instFootSetup: 'Settings stay in %LOCALAPPDATA%\\codeusagemonit across updates; uninstall from Windows Settings → Apps, which asks before deleting them.',
  instCliIntro: 'Once installed, use codeusage in any terminal. Click a command to run it on the right.',
  instFootCli: 'Add <code>--json</code> for scripts or a status bar. <code>codeusage help</code> lists every command.',
  cmdStatus: 'Quotas and usage from the cache, offline', cmdCost: 'Daily cost and tokens, plus each tool’s speed', cmdThird: 'Usage and speed of third-party endpoints',
  winScoop: 'Windows PowerShell · Scoop', winPs: 'Windows PowerShell · install.ps1', winSetup: 'Setup - codeusagemonit 1.0.0', winCli: 'Windows PowerShell · codeusage',
  tipRefresh: 'Refresh', tipSize: 'Four sizes', tipPin: 'Keep on top', tipSettings: 'Settings', tipClose: 'Hide to tray',
  toastRefreshed: '已刷新 · 所有平台刚刚更新', toastPinOn: '已置顶：会盖在其他窗口上面', toastPinOff: '已取消置顶', toastGrok: 'Grok 已连接（演示）',
  toastSize: '右键窗口也能切换小 / 中 / 大 / 完整', toastAuto: '失焦自动收起：{s}', on: '开', off: '关'
};
const ja = {
  skip: '本文へ移動', navProviders: 'サービス', navConsole: '利用枠と使用量', navSizes: 'サイズ', navInstall: 'インストール', navTrust: 'プライバシーと FAQ', navDownload: 'ダウンロード',
  chip: '正式版 · 複数 PC の同期と中英の画面', heroA: '残りの利用枠が', heroB: 'ひと目でわかる',
  heroLead: 'Codex、Claude、Cursor など 11 の AI コーディングツールの残り利用枠、リセット時刻、トークン使用量、出力速度を、Windows のトレイにある小さなウィンドウひとつで。',
  download: 'インストーラーをダウンロード', termInstall: 'ターミナルからインストール', factWin: 'Windows', factLocal: '集計は PC 内だけ', factSource: 'ソースを見る',
  demoTag: 'デモ', demoFoot: 'デモデータ · アカウントは読みません', trayBack: 'トレイにあります · クリックで開く',
  stageHint: 'すべて操作できます：ツール、利用枠、期間、グラフ、右上のボタン',
  eyProviders: '対応サービス', pA: '11 のコーディングツールに対応', pB: 'あなた自身の API も', pLead: '各ツールの既存のログインを利用：CLI、エディターのセッション、API キー。ブラウザーの Cookie は読まず、パスワードも保存しません。',
  eyConsole: '利用枠 · 使用量 · 料金', bA: '残り、費用、速度を', bB: 'ひとつのパネルで',
  bLead: '利用枠はセグメントで表示し、今のペースから使い方を分析。使用量は PC 内のセッションログを直接読み、費用はリクエストごとに公式 API 単価で計算、料金表は毎日同期します。',
  pToday: '今日', p7: '7 日間', p30: '30 日間', colQuota: '利用枠 · 残り / リセット / ペース', boardHint: '利用枠をクリックで詳細 · 縦線は均等に使った場合の位置', colUsage: '使用量 · ローカルログ',
  fCost: 'API 換算の費用', fReq: 'リクエスト', fSpeed: '出力速度', demoData: 'デモデータ',
  prA: '正確な計算', prB: '料金は公式に合わせて', syncOk: '同期済み', syncDate: '料金日付', syncEvery: '24 時間ごとに確認',
  priceTitle: '最新モデル、公式と同期した料金', priceSub: '公式 API 単価。長コンテキストの段階料金、キャッシュの読み書き、Fast モードの倍率にも対応。',
  priceSearch: 'モデルを検索（例：gpt-6、opus、gemini）', thModel: 'モデル', thIn: '入力', thOut: '出力', thCache: 'キャッシュ読取', priceUnit: '米ドル / 100 万トークン · 各社の公式料金（LiteLLM・models.dev による集計）',
  speedTitle: '実際の出力速度', speedSub: '出力トークン ÷ リクエスト全体の所要時間。最初のトークンまでの待ち時間や中継の遅延も含み、ツールごとに計算。行をクリックでそのツールに切り替え。',
  relayTitle: '中継サービスも正確に', relaySub: 'サードパーティの接続先を切り替えても、呼び出しは接続先ごとにはっきり集計されます。', relayOfficial: '公式', relayA: '中継 A', relayB: '中継 B',
  eySizes: 'サイズ設計', sA: '4 つのサイズ', sB: 'ひとつのウィンドウ', sLead: '右クリックで小・中・大・全体を切り替え。コンパクトなサイズはデスクトップ層に置かれ、作業の邪魔をしません。どのサイズもサイズ変更できます。',
  sFull: '全体', sLarge: '大', sMedium: '中', sSmall: '小', viewerTry: 'ウィンドウ内のボタン、ツール、グラフはすべて操作できます。',
  eyInstall: 'インストールと CLI', cA: '1 行でインストール', cB: 'ターミナルでも同じデータを',
  cLead: 'Scoop、PowerShell、またはインストーラーで。同梱の codeusage コマンドはアプリとキャッシュ・設定を共有し、‑‑json でスクリプトやステータスバーに渡せます。',
  recommended: 'おすすめ', instSetup: 'インストーラー', instCli: 'コマンド', replay: '再生', copy: 'コマンドをコピー',
  eyPrivacy: 'プライバシー', vA: 'データは', vB: 'あなたの PC に',
  vText: '記録するのはトークン数、リクエスト数、所要時間、モデル名だけで、会話内容は保存しません。各ツールのログインは元のフォルダーにあり読み取るだけ、料金表は毎日自動で更新します。',
  v1t: '数値だけ', v1: 'トークン数、リクエスト数、所要時間、モデル名だけをローカルの時間別インデックスに記録。会話内容は保存しません。',
  v2t: 'ログインはそのまま', v2: '各ツールのログイントークンは元のフォルダーに残ります。読み取るだけで、コピーしません。',
  v3t: 'キーは暗号化', v3: '入力した API キーは Windows DPAPI で暗号化。復号できるのは現在の Windows ユーザーだけです。',
  v4t: 'テレメトリーなし', v4: '独自サーバーはありません。利用枠の照会は各サービスにだけ送信。料金表は 1 日 1 回 GitHub から取得し、アカウントや使用量の情報は送りません。設定でオフにできます。',
  eyFaq: 'よくある質問',
  fq1: 'インストール方法と必要な環境は？', fa1: 'インストーラー（インストール先とスタートメニュー、任意のデスクトップ・ログオン時起動・PATH。アンインストールは Windows の設定 → アプリ）、Scoop、PowerShell の 1 行、または zip を書き込み可能なフォルダーに展開して実行できます。既定では管理者権限は不要です。Node.js は不要。Windows 標準の .NET Framework 4.8 で動きます。',
  fq7: '更新とアンインストールは？', fa7: 'インストーラー：新しいインストーラーを実行すれば更新。設定は %LOCALAPPDATA%\\codeusagemonit に残り、アンインストール時に削除するか確認します。Scoop：scoop update codeusagemonit / scoop uninstall codeusagemonit。PowerShell：もう一度実行すれば更新。アンインストールは %LOCALAPPDATA%\\Programs\\codeusagemonit を削除し、ユーザー PATH から外します。',
  fq2: 'ダウンロード時に「一般的にダウンロードされていません」、実行時に「Windows によって PC が保護されました」と出るのは？', fa2: '新しいファイルに対する Windows の評判チェックで、ウイルス警告ではありません。インストーラーはまだコード署名されておらず、公開直後でダウンロード数が少ない版では Edge と SmartScreen がこう表示します。ソースから自動ビルドされ、リリースページの SHA256SUMS.txt で確認できます。Edge ではダウンロード項目の「…」→ 保存 → 詳細表示 → 保持する、実行時は「詳細情報 → 実行」を選んでください。Scoop や PowerShell の 1 行コマンドでインストールすると、通常これらの表示は出ません。',
  fq3: 'すべてのアカウントに再ログインが必要ですか？', fa3: 'いいえ。Codex、Claude Code、Cursor、Antigravity、Grok は PC 上の既存のログインを利用します。DeepSeek は DeepSeek Harness を使っていればキーなしでローカル使用量を表示します（残高には API キー）。Kimi、OpenCode、ZCode は API キー、Copilot は GitHub のデバイスログインです。',
  fq9: '2 台の PC の使用量をまとめて見られますか？', fa9: 'はい。設定 › データで片方の使用量を SQL に書き出し、もう片方で読み込むと既存のデータに加算されます。同じファイルの再読み込みや、同じログがすでにある場合は自動で重複を除きます。複数の PC で同じ WebDAV フォルダを指定して同期することもできます。送られるのはトークン数・リクエスト数・費用・所要時間だけで、会話やキーは含みません。',
  fq8: '料金表はどうやって公式と同期していますか？', fa8: 'リポジトリの pricing.json は、GitHub Actions が毎日 LiteLLM と models.dev から作り直しています。アプリは 24 時間ごとに確認し、新しい料金表があればダウンロード・検証して再計算します。取得に失敗した場合や設定でオフにした場合は、内蔵の料金表を使い続けます。',
  dlTitle: '今すぐ始めよう！', dlMeta: '完全無料 · Windows 用 · バージョン 1.0.0', dlBtn: '最新版のインストーラーをダウンロード', feedback: 'フィードバック', notices: 'サードパーティ表記',
  footerNote: '独立したオープンソースプロジェクトであり、OpenAI、Anthropic、Cursor などのサービスとは関係ありません。名称とロゴは各所有者に帰属します。',
  copied: 'コピーしました', copyFailed: 'コピーできませんでした。手動で選択してください', copyLabel: 'コピー',
  tipCost: '費用', tipTokens: 'トークン', tipReq: 'リクエスト', tipSpeed: '出力速度',
  noteDaily: '日別', noteHourly: '時間別', noteSpeed: '速度はトークン加重：Σ出力 ÷ Σ所要時間',
  capQuota: '利用枠', capLocal: 'ローカル使用量', capSpeed: '出力速度', docs: '接続方法 →', pdConnect: '接続', pdQuota: '利用枠', customName: 'カスタム API',
  vSmall: '大きな数字と 1 本のメーター。アイコンひとつ分の広さです。数字をクリックで利用枠を切り替え、ホイールか ←/→ でページ送り。',
  vMedium: '左にメインの利用枠、右にほかの枠。枠が 1 つだけならペース、リセット時刻、リセットクレジットを表示。',
  vLarge: '利用枠とペースに加え、ローカル使用量：期間、費用、トークン、リクエスト、速度、クリックできるグラフ。',
  vFull: '全サービスのカード、サービスごとの詳細、サードパーティ API のページ、設定。端をドラッグして自由にサイズ変更できます。',
  sSmallShort: '数字ひとつ、メーター 1 本', sMediumShort: 'メインの枠とほかの枠', sLargeShort: '利用枠、ペース、ローカル使用量', sFullShort: '全サービス、詳細、設定',
  keySmall: '小', keyMedium: '中', keyLarge: '大', keyFull: '全',
  viewerPrev: '前へ', viewerNext: '次へ', viewerClose: '閉じる',
  pgTop: 'トップ', pgProviders: '対応サービス', pgConsole: '利用枠と使用量', pgSizes: 'サイズ設計', pgInstall: 'インストール', pgTrust: 'プライバシーと FAQ', pgDownload: 'ダウンロード',
  themeLight: 'ライト', themeDark: 'ダーク', themeSystem: 'システムに合わせる', menu: 'メニュー',
  vendorAll: 'すべて', priceNone: '一致するモデルはありません', tagLong: '長コンテキスト >{k}K', defaultCache: '未掲載のため入力単価の 10% で推定',
  priceTotal: '全 {n} 件 · スクロールで表示', priceMatch: '{n} 件一致', tagUse: '{tool} が使用',
  relayNote: '<b>{name}</b> · 30 日間 {tok} トークン · {n} リクエスト · {speed}',
  instScoopIntro: 'このリポジトリを bucket として追加してからインストール。', instScoop1: 'bucket を追加', instScoop2: 'インストール', instScoop3: '今後の更新', optional: '任意',
  instFootScoop: 'スタートメニューのショートカットと PATH 上の codeusage が作られ、data は Scoop の persist に置かれるので更新しても残ります。Scoop が未導入なら先に <code>irm get.scoop.sh | iex</code>。',
  instPsIntro: 'Scoop がなくても、1 行で GitHub Releases から最新版をダウンロードしてインストールします。', instPsRun: 'PowerShell で実行',
  instPs2: '%LOCALAPPDATA%\\Programs\\codeusagemonit にインストール（管理者権限不要）', instPs3: 'codeusage をユーザー PATH に追加し、スタートメニューにショートカットを作成', instPs4: 'もう一度実行すれば更新。data フォルダーは保持',
  instFootPs: 'アンインストールは %LOCALAPPDATA%\\Programs\\codeusagemonit を削除し、ユーザー PATH から外します。',
  instSetupIntro: 'ターミナルを使わない方のためのグラフィカルなインストーラー。', instSetup1: 'インストーラーをダウンロード', instSetup2: 'ウィザードでフォルダーとオプションを選択', instSetup3: 'スタートメニューから起動',
  instSetupB1: '既定は %LOCALAPPDATA%\\Programs\\codeusagemonit。別のフォルダーも選べます', instSetupB2: 'デスクトップ、ログオン時起動、PATH は任意',
  instSetupBtn: 'codeusagemonit-setup-1.0.0.exe をダウンロード', instZipLink: 'またはインストール不要の zip',
  instFootSetup: '設定は %LOCALAPPDATA%\\codeusagemonit に残り、更新しても保持。アンインストールは Windows の設定 → アプリから（削除前に確認）。',
  instCliIntro: 'インストール後はターミナルで codeusage を使えます。コマンドをクリックすると右側で実行します。',
  instFootCli: '<code>--json</code> を付けると JSON になり、スクリプトやステータスバーに渡せます。<code>codeusage help</code> で全コマンドを表示。',
  cmdStatus: 'キャッシュの利用枠と使用量（オフライン）', cmdCost: '日別の費用とトークン、ツールごとの速度', cmdThird: 'サードパーティ API の使用量と速度',
  winScoop: 'Windows PowerShell · Scoop', winPs: 'Windows PowerShell · install.ps1', winSetup: 'セットアップ - codeusagemonit 1.0.0', winCli: 'Windows PowerShell · codeusage',
  tipRefresh: '更新', tipSize: '4 つのサイズ', tipPin: '最前面に固定', tipSettings: '設定', tipClose: 'トレイにしまう',
  toastRefreshed: '已刷新 · 所有平台刚刚更新', toastPinOn: '已置顶：会盖在其他窗口上面', toastPinOff: '已取消置顶', toastGrok: 'Grok 已连接（演示）',
  toastSize: '右键窗口也能切换小 / 中 / 大 / 完整', toastAuto: '失焦自动收起：{s}', on: '开', off: '关'
};
const DICT = { zh, en, ja };
let lang = 'zh';
const t = (key, vars) => { let s = DICT[lang][key] ?? zh[key] ?? key; if (vars) for (const k in vars) s = s.replace('{' + k + '}', vars[k]); return s; };
const LI = () => ({ zh: 0, en: 1, ja: 2 }[lang]);

// Provider facts (zh / en / ja). caps: quota, local usage, speed — 1 yes, 0 no, 2 experimental.
const PROV_INFO = {
  codex: { kind: ['复用登录', 'Existing sign-in', '既存のログイン'], connect: ['复用 Codex CLI 或 Codex 应用的登录', 'Reuses the Codex CLI or app sign-in', 'Codex CLI / アプリのログインを利用'], quota: ['5 小时、每周额度与限额重置额度', '5-hour and weekly windows, reset credits', '5 時間・週の利用枠とリセットクレジット'], caps: [1, 1, 1] },
  claude: { kind: ['复用登录', 'Existing sign-in', '既存のログイン'], connect: ['复用 Claude Code 的登录', 'Reuses the Claude Code sign-in', 'Claude Code のログインを利用'], quota: ['5 小时、每周及模型额度', '5-hour, weekly and per-model quotas', '5 時間・週・モデル別の利用枠'], caps: [1, 1, 1] },
  cursor: { kind: ['编辑器会话', 'Editor session', 'エディター'], connect: ['只读 Cursor 编辑器保存的会话', 'Reads the Cursor editor session (read-only)', 'Cursor エディターのセッションを読み取り'], quota: ['套餐总量、Auto、API 与 Grok Bot 每周额度；用量取自 Cursor 账户的逐次调用明细', 'Plan total, Auto, API and the weekly Grok Bot quota; usage from the per-call list in your Cursor account', 'プラン合計・Auto・API・Grok Bot の週間枠。使用量は Cursor アカウントの呼び出し明細から'], caps: [1, 1, 0] },
  antigravity: { kind: ['桌面应用', 'Desktop app', 'デスクトップ'], connect: ['读取正在运行的桌面应用，登录凭据作为回退', 'Talks to the running desktop app; saved sign-in as fallback', '起動中のデスクトップアプリから取得'], quota: ['周期与模型额度', 'Period and per-model quotas', '期間・モデル別の利用枠'], caps: [1, 1, 0] },
  deepseek: { kind: ['Harness · API Key', 'Harness · API key', 'Harness · API キー'], connect: ['本机用量读 DeepSeek Harness；查余额再填 API Key（可选）', 'Local usage from DeepSeek Harness; an API key (optional) adds the balance', 'ローカル使用量は DeepSeek Harness から。残高は API キー（任意）'], quota: ['API 账户余额（填 Key 时），按币种分别显示', 'API account balance per currency (with a key)', '通貨別の API アカウント残高（キーがある場合）'], caps: [1, 1, 1] },
  grok: { kind: ['复用登录', 'Existing sign-in', '既存のログイン'], connect: ['复用 Grok Build CLI 的登录（grok login）', 'Reuses the Grok Build CLI sign-in (grok login)', 'Grok Build CLI のログインを利用'], quota: ['当前账期的订阅额度', 'Subscription allowance for the billing period', '請求期間のサブスクリプション枠'], caps: [1, 1, 1] },
  copilot: { kind: ['设备码', 'Device login', 'デバイス認証'], connect: ['GitHub 设备码登录，或复用官方插件已保存的授权', 'GitHub device login, or an official client’s saved authorisation', 'GitHub デバイスログイン、または公式クライアントの保存済み認証'], quota: ['每月高级请求与对话额度', 'Monthly premium requests and chat', '月間のプレミアムリクエストとチャット枠'], caps: [1, 2, 0] },
  kimi: { kind: ['API Key', 'API key', 'API キー'], connect: ['Kimi Code API Key，可选国内或国际', 'Kimi Code API key, China or international', 'Kimi Code の API キー（中国版 / 国際版）'], quota: ['5 小时、每周、每月', '5-hour, weekly, monthly', '5 時間・週・月'], caps: [1, 2, 2] },
  opencode: { kind: ['API Key', 'API key', 'API キー'], connect: ['OpenCode Go 的 API Key', 'OpenCode Go API key', 'OpenCode Go の API キー'], quota: ['5 小时、每周、每月', '5-hour, weekly, monthly', '5 時間・週・月'], caps: [1, 2, 2] },
  zcode: { kind: ['API Key', 'API key', 'API キー'], connect: ['智谱 / Z.ai API Key（GLM 编码套餐）', 'Zhipu / Z.ai API key (GLM Coding Plan)', 'Zhipu / Z.ai の API キー（GLM Coding Plan）'], quota: ['5 小时、每周、MCP 工具调用', '5-hour, weekly, MCP tool calls', '5 時間・週・MCP ツール呼び出し'], caps: [1, 1, 1] },
  pi: { kind: ['本机日志', 'Local logs', 'ローカルログ'], connect: ['无需登录', 'No sign-in needed', 'ログイン不要'], quota: ['没有账户额度，只统计本机用量', 'No account quota; local usage only', 'アカウントの利用枠はなく、ローカル使用量のみ'], caps: [0, 1, 1] },
  custom: { kind: ['JSON 接口', 'JSON endpoint', 'JSON API'], connect: ['任意返回 JSON 的 GET 接口，密钥 DPAPI 加密', 'Any GET endpoint returning JSON; key encrypted with DPAPI', 'JSON を返す任意の GET API（キーは DPAPI で暗号化）'], quota: ['最多 6 个额度窗口，或余额', 'Up to 6 quota windows, or a balance', '最大 6 つの利用枠、または残高'], caps: [1, 0, 0] }
};
const AUTH = {
  codex: ['OAuth', 'OAuth', 'OAuth'], claude: ['OAuth', 'OAuth', 'OAuth'], cursor: ['本地会话 · Cookie', 'Local session · Cookie', 'ローカルセッション · Cookie'],
  antigravity: ['本地服务 · OAuth', 'Local service · OAuth', 'ローカルサービス · OAuth'], deepseek: ['Harness · API Key', 'Harness · API key', 'Harness · API キー'], grok: ['CLI 会话', 'CLI session', 'CLI セッション'],
  copilot: ['OAuth 设备流', 'OAuth device flow', 'OAuth デバイスフロー'], kimi: ['API Key', 'API key', 'API キー'], opencode: ['API Key', 'API key', 'API キー'],
  zcode: ['API Key', 'API key', 'API キー'], pi: ['本地文件', 'Local files', 'ローカルファイル'], custom: ['API Key · 自定义', 'API key · Custom', 'API キー · カスタム']
};

// ── Demo data (consistent with the app's --demo mode) ─────────────────
function prng(seed) { return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let x = Math.imul(seed ^ seed >>> 15, 1 | seed); x = x + Math.imul(x ^ x >>> 7, 61 | x) ^ x; return ((x ^ x >>> 14) >>> 0) / 4294967296; }; }
const NOW = new Date(); NOW.setMinutes(0, 0, 0);
const HOUR = NOW.getHours();
const TOOLS = {
  codex: { cost: 477.86, tokens: 233.1e6, today: 25.83, perReq: 38000, speed: 24.2, spread: .14, seed: 7 },
  claude: { cost: 24.59, tokens: 22.35e6, today: .83, perReq: 12000, speed: 89, spread: .12, seed: 11 },
  kimi: { cost: 21.9, tokens: 18.6e6, today: 1.12, perReq: 15000, speed: 38.5, spread: .15, seed: 41 },
  zcode: { cost: 20.63, tokens: 34.38e6, today: 1.34, perReq: 67000, speed: 48, spread: .16, seed: 23 },
  deepseek: { cost: 9.87, tokens: 41.2e6, today: .64, perReq: 26000, speed: 31.2, spread: .18, seed: 53 },
  pi: { cost: 4.28, tokens: 3.06e6, today: .21, perReq: 21000, speed: 52.4, spread: .12, seed: 31 }
};
const USAGE_IDS = Object.keys(TOOLS);
function buildSeries(id) {
  const cfg = TOOLS[id], r = prng(cfg.seed), days = [];
  for (let i = 29; i >= 0; i--) {
    const d = new Date(NOW); d.setHours(0); d.setDate(d.getDate() - i);
    const weekend = d.getDay() === 0 || d.getDay() === 6;
    let w = (0.35 + r()) * (weekend ? .45 : 1);
    if (id !== 'codex' && r() < .22) w = 0;
    days.push({ date: d, w, v: r() });
  }
  const past = days.slice(0, 29), sumW = past.reduce((s, x) => s + x.w, 0) || 1;
  const todayShare = cfg.today / cfg.cost;
  days.forEach((x, i) => {
    const share = i === 29 ? todayShare : (x.w / sumW) * (1 - todayShare);
    x.cost = cfg.cost * share; x.tokens = cfg.tokens * share;
    x.req = x.tokens > 0 ? Math.max(1, Math.round(x.tokens / cfg.perReq * (.85 + x.v * .3))) : 0;
    x.out = x.tokens * .0075;
    x.speed = x.tokens <= 0 ? null : cfg.speed * (1 - cfg.spread + x.v * cfg.spread * 2);
    x.sec = x.speed ? x.out / x.speed : 0;
  });
  const today = days[29], hours = [];
  const shape = h => (h < 8 ? .05 : h < 12 ? 1 : h < 14 ? .5 : h < 19 ? 1.1 : h < 23 ? .6 : .15);
  const hr = prng(cfg.seed * 13);
  const weights = []; for (let h = 0; h <= HOUR; h++) weights.push(today.tokens > 0 ? shape(h) * (.4 + hr()) : 0);
  const hw = weights.reduce((s, x) => s + x, 0) || 1;
  weights.forEach((w, h) => {
    const d = new Date(NOW); d.setHours(h);
    const share = w / hw, v = hr();
    const x = { date: d, hourly: true, cost: today.cost * share, tokens: today.tokens * share };
    x.req = x.tokens > 0 ? Math.max(1, Math.round(today.req * share)) : 0;
    x.out = x.tokens * .0075; x.speed = x.tokens <= 0 ? null : cfg.speed * (1 - cfg.spread + v * cfg.spread * 2); x.sec = x.speed ? x.out / x.speed : 0;
    hours.push(x);
  });
  return { days, hours };
}
const SERIES = Object.fromEntries(USAGE_IDS.map(id => [id, buildSeries(id)]));
const sum = (arr, k) => arr.reduce((s, x) => s + (x[k] || 0), 0);
const speedOf = (arr, id) => { const sec = sum(arr, 'sec'); return sec >= 1 ? sum(arr, 'out') / sec : (TOOLS[id] ? TOOLS[id].speed : 30); };

// Quota windows as the app shows them. pace: headroom (+) or overspend (−) in points.
const QUOTAS = {
  codex: { plan: 'Pro 20x', windows: [{ label: '5 小时', rem: 71, reset: '2小时 14分', len: '5 小时', pace: 26 }, { label: '每周', rem: 62, reset: '3天 4小时', len: '7 天', pace: 16 }], credits: 2 },
  claude: { plan: 'Max 5x', windows: [{ label: '5 小时', rem: 36, reset: '1小时 29分', len: '5 小时', pace: 6 }, { label: '每周', rem: 97, reset: '3天 4小时', len: '7 天', pace: 51 }, { label: '每周 · Opus', rem: 54, reset: '3天 4小时', len: '7 天', pace: 9 }] },
  cursor: { plan: 'Pro', windows: [{ label: '总量', rem: 4, reset: '12天 3小时', len: '30 天', pace: -12, empty: '约 2天 6小时后用尽' }, { label: 'Auto', rem: 58, reset: '12天 3小时', len: '30 天', pace: 18 }, { label: 'API', rem: 81, reset: '12天 3小时', len: '30 天', pace: 39 }, { label: 'Grok Bot · 每周', rem: 72, reset: '4天 6小时', len: '7 天', pace: 11 }] },
  antigravity: { plan: 'Pro', windows: [{ label: 'Gemini 3 Pro', rem: 80, reset: '2小时 10分', len: '5 小时', pace: 23 }, { label: 'Claude', rem: 64, reset: '2小时 10分', len: '5 小时', pace: 7 }] },
  deepseek: { balance: 'USD 8.62' },
  grok: { setup: true },
  copilot: { plan: 'Pro', windows: [{ label: '高级请求', rem: 58, reset: '18天 2小时', len: '每月', pace: 18 }, { label: '对话', rem: 100, reset: '18天 2小时', len: '每月', pace: 40 }] },
  kimi: { plan: 'Moderato', windows: [{ label: '5 小时', rem: 45, reset: '3小时 2分', len: '5 小时', pace: -6, empty: '约 1小时 50分后用尽' }, { label: '每周', rem: 76, reset: '4天 9小时', len: '7 天', pace: 14 }] },
  opencode: { plan: 'Go', windows: [{ label: '5 小时', rem: 88, reset: '4小时 1分', len: '5 小时', pace: 8 }, { label: '每周', rem: 91, reset: '5天 1小时', len: '7 天', pace: 20 }] },
  zcode: { plan: 'GLM Pro', windows: [{ label: '5 小时', rem: 69, reset: '2小时 44分', len: '5 小时', pace: 14 }, { label: '每周', rem: 83, reset: '4天 20小时', len: '7 天', pace: 12 }, { label: 'MCP 调用', rem: 90, reset: '10天 23小时', len: '30 天', pace: 53 }] },
  pi: { local: true }
};
const GROK_CONNECTED = { plan: 'SuperGrok', windows: [{ label: '账期额度', rem: 83, reset: '9天 7小时', len: '30 天', pace: 21 }] };
// Models each tool used (share of tokens, output speed). Keys match pricing.json.
const MODELS = {
  codex: [['gpt-6-astra', 62, 19.9], ['gpt-6-sol', 24, 43.3], ['gpt-6-luna', 14, 51]],
  claude: [['claude-opus-5-5', 71, 92.4], ['claude-sonnet-5-5', 22, 84.1], ['claude-haiku-4-5', 7, 131]],
  kimi: [['kimi-k2.7-code', 81, 36.2], ['kimi-k2.7-code-highspeed', 19, 58.7]],
  zcode: [['glm-5.2', 100, 48]],
  deepseek: [['deepseek-v4-pro', 68, 28.4], ['deepseek-v4-flash', 32, 39.5]],
  pi: [['claude-sonnet-5-5', 58, 55.1], ['gpt-6-luna', 42, 48.6]],
  cursor: [['claude-sonnet-5-5', 46, 71.2], ['gpt-6-sol', 38, 45.8], ['gemini-3.1-pro-preview', 16, 62.3]],
  antigravity: [['gemini-3.1-pro-preview', 74, 58.2], ['claude-sonnet-5-5', 26, 69.5]],
  copilot: [['gpt-6-sol', 55, 41.7], ['claude-sonnet-5-5', 45, 66.3]],
  opencode: [['kimi-k2.7-code', 60, 34.9], ['glm-5.2', 40, 44.1]],
  grok: []
};
const meter = (rem, id, cls = '', mark = null) => {
  const on = Math.round(Math.max(0, Math.min(100, rem)) / 100 * 24); let s = '';
  for (let i = 0; i < 24; i++) s += `<i class="${i < on ? 'on' : ''}"></i>`;
  if (mark != null) s += `<span class="mark" style="left:${mark}%"></span>`;
  return `<div class="meter ${cls}" style="--c:${rem < 10 ? 'var(--low)' : cv(id)}">${s}</div>`;
};
const paceText = w => w.pace == null ? '' : Math.abs(w.pace) < 1 ? '<span class="dim">进度均衡</span>' : w.pace >= 0 ? `<span class="good">余量 ${w.pace}%</span> · 按当前速度可持续到重置` : `<span class="warn">超前消耗 ${-w.pace}%</span> · ${w.empty || ''}`;
const resetDate = s => { const n = re => +((re.exec(s) || [])[1] || 0); return new Date(Date.now() + ((n(/(\d+)天/) * 24 + n(/(\d+)小时/)) * 60 + n(/(\d+)分/)) * 60e3); };
const WEEK = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];
const fmtReset = d => `${d.getMonth() + 1}月${d.getDate()}日 ${WEEK[d.getDay()]} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
function animateMeters(rootEl) {
  if (reduced) return;
  rootEl.querySelectorAll('.meter').forEach(m => {
    const cells = [...m.children].filter(c => c.tagName === 'I'), lit = cells.map(c => c.classList.contains('on'));
    cells.forEach(c => c.classList.remove('on'));
    lit.forEach((on, i) => { if (on) setTimeout(() => cells[i].classList.add('on'), 80 + i * 22); });
  });
}

// Usage for tools without a SERIES (Cursor, Copilot, …): shaped like Codex's days.
const EXTRA_USAGE = { cursor: [18.42, 6.2e6, .64, 58.1, 248], antigravity: [11.3, 8.4e6, .48, 60.4, 176], copilot: [7.85, 2.1e6, .22, 52.9, 312], grok: [6.4, 4.7e6, .31, 44.2, 98], opencode: [8.16, 5.4e6, .27, 37.5, 154] };
const packs = {};
function usagePack(id) {
  if (packs[id]) return packs[id];
  if (SERIES[id]) {
    const days = SERIES[id].days.map(d => ({ date: d.date, cost: d.cost, tokens: d.tokens, req: d.req, speed: d.speed || TOOLS[id].speed, sec: d.sec, out: d.out }));
    return (packs[id] = { cost: TOOLS[id].cost, tokens: TOOLS[id].tokens, today: TOOLS[id].today, speed: TOOLS[id].speed, req: Math.max(1, sum(days, 'req')), days, hours: SERIES[id].hours });
  }
  const [cost, tokens, today, speed, req] = EXTRA_USAGE[id] || [4.2, 1.8e6, .18, 27.5, 86];
  const r = prng(id.split('').reduce((s, ch) => s + ch.charCodeAt(0), 0) || 9);
  const weights = SERIES.codex.days.map(() => .35 + r());
  const sumW = weights.reduce((a, b) => a + b, 0) || 1;
  const days = SERIES.codex.days.map((d, i) => ({ date: d.date, cost: cost * weights[i] / sumW, tokens: tokens * weights[i] / sumW, req: Math.max(1, Math.round(req * weights[i] / sumW)), speed: speed * (.88 + r() * .24) }));
  days[29].cost = today;
  const hours = SERIES.codex.hours.map((h, i) => ({ date: h.date, hourly: true, cost: today * (h.cost / (SERIES.codex.days[29].cost || 1)), tokens: days[29].tokens * (h.tokens / (SERIES.codex.days[29].tokens || 1)), req: Math.max(h.tokens > 0 ? 1 : 0, Math.round(days[29].req * (h.tokens / (SERIES.codex.days[29].tokens || 1)))), speed: speed * (.9 + r() * .2) }));
  return (packs[id] = { cost, tokens, today, speed, req, days, hours });
}
function rowsFor(id, period) { const p = usagePack(id); return period === 'today' ? p.hours : period === '7d' ? p.days.slice(-7) : p.days; }
const speedOfRows = (rows, id) => { const sec = sum(rows, 'sec'); if (sec >= 1) return sum(rows, 'out') / sec; const w = rows.filter(x => x.tokens > 0); return w.length ? w.reduce((s, x) => s + (x.speed || 0) * x.tokens, 0) / sum(w, 'tokens') : usagePack(id).speed; };
const PERIOD_ZH = { today: '当天', '7d': '近 7 天', '30d': '近 30 天' };
const dayLabel = d => `${d.getMonth() + 1}/${d.getDate()}`;
const axisOf = rows => { if (!rows.length) return ''; const n = rows.length, idx = n <= 8 ? rows.map((_, i) => i) : [0, Math.round(n * .2), Math.round(n * .4), Math.round(n * .6), Math.round(n * .8), n - 1]; return `<div class="mini-axis">${[...new Set(idx)].map(i => `<span>${rows[i].hourly ? pad2(rows[i].date.getHours()) + ':00' : i === n - 1 && n === 30 ? '今天' : dayLabel(rows[i].date)}</span>`).join('')}</div>`; };

// ── Hero: the full panel, flat, animated, every card clickable ────────
const stage = $('stage'), app = $('app'), body = $('app-body'), tabs = $('app-tabs'), toastEl = $('app-toast'), sheet = $('app-sheet');
const PAGE_ORDER = ['overview', ...IDS];
const hero = { page: 'overview', period: { overview: '30d' }, hidden: new Set(), open: new Set() };
const periodOf = id => hero.period[id] || '30d';
const OVERVIEW_ICON = '<svg class="pi" viewBox="0 0 18 18" style="background:none;-webkit-mask:none;mask:none"><rect x="2" y="2" width="14" height="14" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M2 7h14M2 11.5h14M7 2v14M11.5 2v14" stroke="currentColor" stroke-width="1.1"/></svg>';
const CHEV = '<svg class="chev" viewBox="0 0 8 8"><path d="m1.5 3 2.5 2.5L6.5 3" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>';
let toastTimer = 0;
function toast(text) { toastEl.textContent = text; toastEl.classList.add('on'); clearTimeout(toastTimer); toastTimer = setTimeout(() => toastEl.classList.remove('on'), 2200); }
function renderTabs() {
  tabs.innerHTML = PAGE_ORDER.map(id => `<button type="button" class="app-tab" role="tab" data-page="${id}" aria-selected="${id === hero.page}">${id === 'overview' ? OVERVIEW_ICON : icon(id)}<span>${id === 'overview' ? '概览' : P[id].name}</span></button>`).join('');
}
const barsHtml = (rows, color, max) => { max = max || Math.max(...rows.map(d => d.cost), .01); return `<div class="mini-chart" data-rows>${rows.map((d, i) => `<button type="button" class="b" data-i="${i}" style="--i:${i};height:${Math.max(3, d.cost / max * 100)}%"><i style="height:100%;background:${color}"></i></button>`).join('')}</div>`; };
function quotaLines(id, expandable, limit) {
  const q = QUOTAS[id];
  if (q.setup) return `<div class="q-line"><p class="dim" style="margin:6px 0 0">尚未连接 · 在终端运行 grok login</p><button type="button" class="a-chip" data-act="connect" style="margin-top:10px">在终端登录</button></div>`;
  if (q.balance) return `<div class="q-line"><div class="row"><span class="dim">可用余额</span><b class="num">${q.balance}</b></div></div>`;
  if (q.local) return `<div class="q-line"><p class="dim" style="margin:6px 0 0">没有账户额度 · 只统计本机日志</p></div>`;
  const list = limit ? q.windows.slice(0, limit) : q.windows;
  return list.map((w, i) => {
    const key = id + i, used = 100 - w.rem, even = Math.max(0, Math.min(100, used + w.pace));
    const inner = `<div class="row"><span>${w.label} <b class="num">${w.rem}%</b> 剩余</span><span class="faint">${w.reset}后重置</span></div>${meter(w.rem, id)}<div class="pace">${paceText(w)}</div>`;
    if (!expandable) return `<div class="q-line">${inner}</div>`;
    return `<button type="button" class="q-line" data-q="${key}" aria-expanded="${hero.open.has(key)}">${inner}<div class="q-detail"><div><dl><div><dt>已用 / 剩余</dt><dd>${used}% / ${w.rem}%</dd></div><div><dt>重置时间</dt><dd>${fmtReset(resetDate(w.reset))}</dd></div><div><dt>匀速应已用</dt><dd>${even}%</dd></div></dl></div></div></button>`;
  }).join('') + (q.credits ? `<div class="row" style="margin-top:10px"><span>限额重置额度</span><b>${q.credits} 次可用</b></div>` : '');
}
function providerCard(id, i) {
  const q = QUOTAS[id], pk = usagePack(id);
  const foot = `<div class="row faint card-foot"><span class="num">今日 ${usd(pk.today)} · 30 天 ${usd(pk.cost)}</span><span class="num">${compact(pk.tokens)} Token · ${tps(pk.speed)}</span></div>`;
  return `<div class="app-card go" role="button" tabindex="0" data-go="${id}" style="--c:${cv(id)};--i:${i}"><div class="card-head">${icon(id)}<b>${P[id].name}</b>${q.plan ? `<span class="plan">${q.plan}</span>` : ''}<span class="faint" style="margin-left:auto" data-fresh>刚刚更新</span></div>${quotaLines(id, false, 2)}${foot}<span class="go-hint">查看详情 →</span></div>`;
}
function overviewHtml() {
  const per = periodOf('overview'), ids = USAGE_IDS.filter(k => !hero.hidden.has(k));
  const base = rowsFor('codex', per);
  const bars = base.map((b, i) => ({ date: b.date, hourly: b.hourly, parts: ids.map(id => ({ id, cost: (rowsFor(id, per)[i] || {}).cost || 0 })) }));
  const tot = ids.reduce((s, id) => s + sum(rowsFor(id, per), 'cost'), 0), tok = ids.reduce((s, id) => s + sum(rowsFor(id, per), 'tokens'), 0);
  const max = Math.max(.01, ...bars.map(b => b.parts.reduce((s, x) => s + x.cost, 0)));
  const chart = `<div class="mini-chart" data-chart="overview">${bars.map((b, i) => { const t0 = b.parts.reduce((s, x) => s + x.cost, 0); return `<button type="button" class="b" data-i="${i}" style="--i:${i};height:${Math.max(2, t0 / max * 100)}%">${b.parts.filter(x => x.cost > 0).map(x => `<i style="height:${x.cost / (t0 || 1) * 100}%;background:${cv(x.id)}"></i>`).join('')}</button>`; }).join('')}</div>`;
  const summary = `<div class="app-card" style="--i:0"><div class="row"><span class="dim">全部平台 · API 等价费用</span><button type="button" class="a-chip" data-act="period" data-scope="overview">${PERIOD_ZH[per]}${CHEV}</button></div>
    <div class="row" style="margin-top:6px"><span class="big" data-to="${tot}" data-fmt="usd">${usd(tot)}</span><span class="num"><span data-to="${tok}" data-fmt="compact">${compact(tok)}</span> Token</span></div>
    <div class="faint">${per === '30d' ? `今日 ${usd(ids.reduce((s, k) => s + TOOLS[k].today, 0))} · ` : ''}${ids.length} 个平台有记录 · 点图例可隐藏</div>
    ${chart}${axisOf(base)}
    <div class="legend">${USAGE_IDS.map(k => `<button type="button" data-act="legend" data-id="${k}" aria-pressed="${!hero.hidden.has(k)}"><i style="background:${cv(k)}"></i>${P[k].name}</button>`).join('')}</div></div>`;
  return summary + ['codex', 'claude', 'cursor', 'kimi', 'zcode', 'antigravity', 'copilot', 'opencode', 'deepseek', 'grok', 'pi'].map((id, i) => providerCard(id, i + 1)).join('');
}
function detailHtml(id) {
  const q = QUOTAS[id], per = periodOf(id), rows = rowsFor(id, per);
  const quota = `<div class="app-card" style="--i:0;--c:${cv(id)}"><div class="card-head">${icon(id)}<b>${P[id].name}</b>${q.plan ? `<span class="plan">${q.plan}</span>` : ''}<span class="faint" style="margin-left:auto" data-fresh>刚刚更新</span></div>${quotaLines(id, true)}</div>`;
  const c = sum(rows, 'cost'), tk = sum(rows, 'tokens'), rq = sum(rows, 'req'), sp = speedOfRows(rows, id);
  const usage = `<div class="app-card" style="--i:1"><div class="row"><b style="font-size:13px">本机用量</b><button type="button" class="a-chip" data-act="period" data-scope="${id}">${PERIOD_ZH[per]}${CHEV}</button></div>
    <div class="figs4">${[['费用', c, 'usd'], ['Token', tk, 'compact'], ['请求', rq, 'int'], ['速度', sp, 'tps']].map(([k, v, f]) => `<div><div class="faint">${k}</div><div class="num" data-to="${v}" data-fmt="${f}">${FMT[f](v)}</div></div>`).join('')}</div>
    ${barsHtml(rows, cv(id))}${axisOf(rows)}</div>`;
  const ms = MODELS[id] || [];
  const models = ms.length ? `<div class="app-card" style="--i:2"><div class="row"><b style="font-size:13px">模型</b><span class="faint">Token 占比 · 输出速度</span></div><div style="margin-top:6px">${ms.map(([m, share, speed]) => `<div class="mrow"><span class="mn" title="${m}">${m}</span><span class="mb"><i style="--w:${share}%;--c:${cv(id)}"></i></span><span class="mp">${share}%</span><span class="ms">${tps(speed)}</span></div>`).join('')}</div></div>` : '';
  return quota + usage + models;
}
function renderPage(anim) {
  const id = hero.page, st = body.scrollTop;
  const page = document.createElement('div');
  page.className = 'app-page' + (anim === 'still' ? ' still' : anim ? ' ' + anim : '');
  page.innerHTML = id === 'overview' ? overviewHtml() : detailHtml(id);
  appTip = null; body.replaceChildren(page);
  if (anim === 'still') { body.scrollTop = st; page.querySelectorAll('.app-card').forEach(c => { c.style.animation = 'none'; }); }
  else { body.scrollTop = 0; animateMeters(page); }
  countAll(page);
}
function selectPage(id, user) {
  if (id === hero.page) return;
  const dir = PAGE_ORDER.indexOf(id) > PAGE_ORDER.indexOf(hero.page) ? 'from-r' : 'from-l';
  hero.page = id; renderTabs(); renderPage(dir);
  tabs.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  if (user) stopTour();
}
tabs.addEventListener('click', e => { const b = e.target.closest('[data-page]'); if (b) selectPage(b.dataset.page, true); });
let appTip = null;
body.addEventListener('click', e => {
  const act = e.target.closest('[data-act]'), go = e.target.closest('[data-go]'), q = e.target.closest('[data-q]'), bar = e.target.closest('.mini-chart .b');
  stopTour();
  if (act) {
    e.stopPropagation();
    const a = act.dataset.act;
    if (a === 'period') { const s = act.dataset.scope, order = ['30d', '7d', 'today']; hero.period[s] = order[(order.indexOf(periodOf(s)) + 1) % 3]; renderPage('still'); }
    else if (a === 'legend') { const k = act.dataset.id; if (!hero.hidden.delete(k) && hero.hidden.size < USAGE_IDS.length - 1) hero.hidden.add(k); renderPage('still'); }
    else if (a === 'connect') { act.classList.add('busy'); act.textContent = '正在打开终端…'; setTimeout(() => { QUOTAS.grok = GROK_CONNECTED; renderPage('still'); animateMeters(body); toast(t('toastGrok')); }, 1300); }
    return;
  }
  if (q) { const k = q.dataset.q; if (!hero.open.delete(k)) hero.open.add(k); q.setAttribute('aria-expanded', String(hero.open.has(k))); return; }
  if (bar) { const was = bar.classList.contains('on'); bar.parentElement.querySelectorAll('.b.on').forEach(x => x.classList.remove('on')); if (!was) bar.classList.add('on'); showAppTip(bar); return; }
  if (go) selectPage(go.dataset.go, true);
});
body.addEventListener('keydown', e => { const go = e.target.closest('[data-go]'); if (go && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); selectPage(go.dataset.go, true); } });
function showAppTip(b) {
  const chartEl = b.parentElement, i = +b.dataset.i;
  let text;
  if (chartEl.dataset.chart === 'overview') {
    const per = periodOf('overview'), ids = USAGE_IDS.filter(k => !hero.hidden.has(k)), base = rowsFor('codex', per)[i];
    const parts = ids.map(k => [k, (rowsFor(k, per)[i] || {}).cost || 0]).filter(x => x[1] > 0);
    text = `<b>${base.hourly ? pad2(base.date.getHours()) + ':00' : dayLabel(base.date)}</b> · ${usd(parts.reduce((s, x) => s + x[1], 0))}` + parts.map(([k, v]) => `<br>${P[k].name}  ${usd(v)}`).join('');
  } else {
    const id = hero.page, d = rowsFor(id, periodOf(id))[i]; if (!d) return;
    text = `<b>${d.hourly ? pad2(d.date.getHours()) + ':00' : dayLabel(d.date)}</b><br>${usd(d.cost)} · ${compact(d.tokens)} Token<br>${int(Math.max(d.tokens > 0 ? 1 : 0, d.req || 0))} 次请求 · ${tps(d.speed || usagePack(id).speed)}`;
  }
  if (!appTip) { appTip = document.createElement('div'); appTip.className = 'app-tip'; body.appendChild(appTip); }
  appTip.innerHTML = text;
  const br = b.getBoundingClientRect(), pr = body.getBoundingClientRect();
  const x = br.left - pr.left + br.width / 2, y = br.top - pr.top + body.scrollTop - 8;
  appTip.style.left = Math.min(body.clientWidth - appTip.offsetWidth - 6, Math.max(6, x - appTip.offsetWidth / 2)) + 'px';
  appTip.style.top = Math.max(body.scrollTop + 4, y - appTip.offsetHeight) + 'px';
}
body.addEventListener('pointerover', e => { const b = e.target.closest('.mini-chart .b'); if (b) showAppTip(b); });
body.addEventListener('pointerout', e => { if (e.target.closest('.mini-chart .b') && appTip && !e.relatedTarget?.closest?.('.mini-chart .b') && !body.querySelector('.mini-chart .b.on')) { appTip.remove(); appTip = null; } });
['wheel', 'touchstart', 'keydown'].forEach(type => body.addEventListener(type, () => stopTour(), { passive: true }));
// Toolbar
$('app-refresh').addEventListener('click', e => {
  const btn = e.currentTarget; stopTour(); btn.classList.remove('spin'); void btn.offsetWidth; btn.classList.add('spin');
  body.querySelectorAll('[data-fresh]').forEach(el => { el.textContent = '正在刷新…'; });
  setTimeout(() => { renderPage('still'); animateMeters(body); toast(t('toastRefreshed')); }, 750);
});
$('app-size').addEventListener('click', () => { stopTour(); toast(t('toastSize')); setTimeout(() => $('sizes').scrollIntoView({ behavior: reduced ? 'auto' : 'smooth' }), 650); });
function goSize(k) { $('sizes').scrollIntoView({ behavior: reduced ? 'auto' : 'smooth' }); setTimeout(() => selectSize(k, stackEl.querySelector(`[data-size="${k}"]`)), reduced ? 0 : 650); }
$('app-pin').addEventListener('click', e => { stopTour(); const on = e.currentTarget.getAttribute('aria-pressed') !== 'true'; e.currentTarget.setAttribute('aria-pressed', String(on)); app.classList.toggle('pinned', on); toast(t(on ? 'toastPinOn' : 'toastPinOff')); });
const appSet = { alpha: 100, size: 'full', autohide: true };
function renderSheet() {
  sheet.innerHTML = `<h5>设置</h5>
    <div class="set-row"><span>界面透明度</span><span style="display:flex;align-items:center;gap:8px"><input type="range" min="0" max="60" value="${100 - appSet.alpha}" data-set="alpha" aria-label="界面透明度"><b class="num" style="width:34px;text-align:right">${100 - appSet.alpha}%</b></span></div>
    <div class="set-row"><span>显示尺寸</span><span class="set-seg">${[['small', '小'], ['medium', '中'], ['large', '大'], ['full', '完整']].map(([k, n]) => `<button type="button" data-set-size="${k}" aria-pressed="${k === appSet.size}">${n}</button>`).join('')}</span></div>
    <div class="set-row"><span>失焦自动收起</span><button type="button" class="switch" role="switch" data-set="autohide" aria-checked="${appSet.autohide}" aria-label="失焦自动收起"></button></div>`;
}
$('app-gear').addEventListener('click', e => { stopTour(); const open = sheet.hidden; if (open) renderSheet(); sheet.hidden = !open; e.currentTarget.setAttribute('aria-expanded', String(open)); });
sheet.addEventListener('input', e => { if (e.target.dataset.set !== 'alpha') return; appSet.alpha = 100 - +e.target.value; app.style.setProperty('--app-a', String(appSet.alpha / 100)); app.classList.toggle('glass', appSet.alpha < 100); e.target.nextElementSibling.textContent = (100 - appSet.alpha) + '%'; });
sheet.addEventListener('click', e => {
  const sw = e.target.closest('[data-set="autohide"]'), sz0 = e.target.closest('[data-set-size]');
  if (sw) { appSet.autohide = !appSet.autohide; sw.setAttribute('aria-checked', String(appSet.autohide)); toast(t('toastAuto', { s: t(appSet.autohide ? 'on' : 'off') })); }
  if (sz0) { const k = sz0.dataset.setSize; if (k === 'full') return; sheet.hidden = true; $('app-gear').setAttribute('aria-expanded', 'false'); goSize(k); }
});
$('app-close').addEventListener('click', () => { stopTour(); sheet.hidden = true; $('app-gear').setAttribute('aria-expanded', 'false'); app.classList.add('gone'); setTimeout(() => { $('tray').hidden = false; }, 380); });
$('tray').addEventListener('click', () => { $('tray').hidden = true; app.classList.remove('gone'); renderPage('from-r'); });
// Auto tour until the visitor touches the demo.
const TOUR = ['overview', 'codex', 'claude', 'cursor', 'zcode'];
let tourTimer = null, tourStep = 0, touched = false;
function startTour() { if (reduced || tourTimer || touched) return; tourTimer = setInterval(() => { if (document.hidden) return; tourStep = (tourStep + 1) % TOUR.length; selectPage(TOUR[tourStep], false); }, 5200); }
function stopTour() { clearInterval(tourTimer); tourTimer = null; touched = true; stage.classList.add('touched'); }
new IntersectionObserver(([entry]) => { if (entry.isIntersecting) startTour(); else { clearInterval(tourTimer); tourTimer = null; } }).observe(stage);

// ── Copy helpers ──────────────────────────────────────────────────────
const COPY_ICON = '<svg viewBox="0 0 16 16" aria-hidden="true"><rect x="5" y="5" width="8.5" height="8.5" rx="2" fill="none" stroke="currentColor" stroke-width="1.4"/><path d="M11 3.2V3a1.5 1.5 0 0 0-1.5-1.5H4A1.5 1.5 0 0 0 2.5 3v5.5A1.5 1.5 0 0 0 4 10h.2" fill="none" stroke="currentColor" stroke-width="1.4"/></svg>';
const OK_ICON = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="m3.5 8.5 3 3 6-7" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/></svg>';
async function writeClip(text) { try { await navigator.clipboard.writeText(text); return true; } catch { return false; } }
async function copyText(text, label, restore) { label.textContent = (await writeClip(text)) ? t('copied') : t('copyFailed'); setTimeout(() => { label.textContent = restore(); }, 1600); }
async function copyIcon(btn, text) { const ok = await writeClip(text); btn.innerHTML = ok ? OK_ICON : COPY_ICON; btn.classList.toggle('ok', ok); btn.title = ok ? t('copied') : t('copyFailed'); setTimeout(() => { btn.innerHTML = COPY_ICON; btn.classList.remove('ok'); btn.title = t('copyLabel'); }, 1600); }
const cmdText = text => esc(text).replace(/\//g, '/<wbr>');
document.addEventListener('click', e => { const b = e.target.closest('[data-copy]'); if (b) { e.stopPropagation(); copyIcon(b, b.dataset.copy); } }, true);

// ── 01 Providers: the v1 list — logo, name and how it connects; each links to its docs ──
const REPO = 'https://github.com/fanchengliu/codeusagemonit';
function renderProviders() {
  $('p-grid').innerHTML = [...IDS, 'custom'].map(id => {
    const name = id === 'custom' ? t('customName') : P[id].name;
    const mark = id === 'custom' ? '<span class="plus">+</span>' : icon(id);
    return `<li><a class="provider-item" href="${REPO}/blob/main/docs/providers/${id}.md"><span class="provider-mark" aria-hidden="true">${mark}</span><span><strong>${esc(name)}</strong><small>${esc(AUTH[id][LI()])}</small></span></a></li>`;
  }).join('');
}

// ── 02 Console: one tool's quotas and usage; pricing, speed and relays follow it ──
const BOARD_IDS = ['claude', 'codex', 'cursor', 'zcode', 'kimi', 'deepseek', 'pi'];
const dash = { tool: 'claude', period: '7d', metric: 'cost' };
const toolPick = $('tool-pick'), chart = $('chart'), axis = $('axis'), tip = $('tip'), boardQuota = $('board-quota');
const expanded = new Set();
toolPick.innerHTML = BOARD_IDS.map(id => `<button type="button" data-tool="${id}" style="--c:${cv(id)}" aria-pressed="${id === dash.tool}">${icon(id)}${P[id].name}</button>`).join('');
function renderBoardQuota(animate) {
  const id = dash.tool, q = QUOTAS[id];
  let html = `<div class="bq-head">${icon(id)}<b>${P[id].name}</b>${q.plan ? `<span class="plan">${q.plan}</span>` : ''}<span class="fresh">刚刚更新</span></div>`;
  if (q.balance) html += `<div class="q-empty"><strong>${q.balance}</strong>可用余额 · DeepSeek 只提供账户余额，没有周期额度；右边的用量照常统计。</div>`;
  else if (q.local) html += `<div class="q-empty"><strong>∞</strong>Pi 没有账户额度，只统计本机日志里的用量和输出速度。</div>`;
  else {
    html += q.windows.map((w, i) => {
      const key = id + i, used = 100 - w.rem, elapsed = Math.max(0, Math.min(100, used + w.pace)), even = 100 - elapsed;
      return `<button type="button" class="q-win" data-key="${key}" aria-expanded="${expanded.has(key)}"><div class="row"><span>${w.label} <b>${w.rem}%</b> 剩余</span><span class="cd">${w.reset}后重置</span></div>
        ${meter(w.rem, id, 'lg', even)}<div class="pace">${paceText(w)}</div>
        <div class="q-more"><div><div class="q-facts"><div><span>已用 / 剩余</span><em>${used}% / ${w.rem}%</em></div><div><span>窗口长度</span><em>${w.len}</em></div><div><span>重置时间</span><em>${fmtReset(resetDate(w.reset))}</em></div><div><span>匀速应已用</span><em>${elapsed}%</em></div><div class="q-note">节奏按本周期平均速度线性估算，不是官方承诺。</div></div></div></div></button>`;
    }).join('');
    if (q.credits) html += `<div class="q-extra"><span>限额重置额度</span><b>${q.credits} 次可用</b></div>`;
  }
  boardQuota.innerHTML = html;
  if (animate) animateMeters(boardQuota);
}
boardQuota.addEventListener('click', e => { const b = e.target.closest('.q-win'); if (!b) return; const k = b.dataset.key; if (!expanded.delete(k)) expanded.add(k); b.setAttribute('aria-expanded', String(expanded.has(k))); });
const periodBars = () => rowsFor(dash.tool, dash.period);
const valueOf = (x, m) => m === 'cost' ? x.cost : m === 'tokens' ? x.tokens : (x.speed || 0);
function renderDash() {
  const bars = periodBars(), m = dash.metric;
  $('board-panel').style.setProperty('--c', cv(dash.tool));
  toolPick.querySelectorAll('button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.tool === dash.tool)));
  document.querySelectorAll('#period-seg button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.period === dash.period)));
  document.querySelectorAll('.dash-figs button.fig').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.metric === m)));
  countTo($('f-cost'), sum(bars, 'cost'), usd);
  countTo($('f-tokens'), sum(bars, 'tokens'), compact);
  countTo($('f-req'), sum(bars, 'req'), int);
  countTo($('f-speed'), speedOfRows(bars, dash.tool), tps);
  const max = Math.max(1e-9, ...bars.map(x => valueOf(x, m)));
  chart.innerHTML = bars.map((x, i) => `<div class="bar${valueOf(x, m) <= 0 ? ' empty' : ''}${i === bars.length - 1 ? ' now' : ''}" data-i="${i}"><i style="--h:0%"></i></div>`).join('');
  requestAnimationFrame(() => requestAnimationFrame(() => chart.querySelectorAll('.bar').forEach((el, i) => { el.firstChild.style.setProperty('--h', (valueOf(bars[i], m) / max * 100).toFixed(2) + '%'); })));
  const ticks = bars.length <= 8 ? bars.map((_, i) => i) : [0, Math.round(bars.length * .25), Math.round(bars.length * .5), Math.round(bars.length * .75), bars.length - 1];
  axis.innerHTML = [...new Set(ticks)].map(i => `<span>${bars[i].hourly ? pad2(bars[i].date.getHours()) + ':00' : dayLabel(bars[i].date)}</span>`).join('');
  $('dash-note').textContent = (dash.period === 'today' ? t('noteHourly') : t('noteDaily')) + ' · ' + (dash.period === 'today' ? t('pToday') : dash.period === '7d' ? t('p7') : t('p30')) + (m === 'speed' ? ' · ' + t('noteSpeed') : '');
  tip.hidden = true;
  paintSpeedSel();
  if (prices.length) renderPrices();
}
function pickTool(id) { if (id === dash.tool) return; dash.tool = id; renderBoardQuota(true); renderDash(); }
toolPick.addEventListener('click', e => { const b = e.target.closest('[data-tool]'); if (b) pickTool(b.dataset.tool); });
$('period-seg').addEventListener('click', e => { const b = e.target.closest('[data-period]'); if (b) { dash.period = b.dataset.period; renderDash(); } });
document.querySelector('.dash-figs').addEventListener('click', e => { const b = e.target.closest('button.fig'); if (b) { dash.metric = b.dataset.metric; renderDash(); } });
function showTip(barEl) {
  const bars = periodBars(), x = bars[+barEl.dataset.i]; if (!x) return;
  chart.querySelectorAll('.hot').forEach(el => el.classList.remove('hot')); barEl.classList.add('hot');
  const label = x.hourly ? `${dayLabel(x.date)} ${pad2(x.date.getHours())}:00` : x.date.toLocaleDateString(lang === 'zh' ? 'zh-CN' : lang === 'ja' ? 'ja-JP' : 'en-US', { month: 'short', day: 'numeric', weekday: 'short' });
  tip.innerHTML = `<b>${label}</b><div><span>${t('tipCost')}</span><span>${usd(x.cost)}</span></div><div><span>${t('tipTokens')}</span><span>${compact(x.tokens)}</span></div><div><span>${t('tipReq')}</span><span>${int(Math.max(0, x.req || 0))}</span></div><div><span>${t('tipSpeed')}</span><span>${tps(x.speed || (x.tokens > 0 ? usagePack(dash.tool).speed : 0))}</span></div>`;
  const cr = chart.getBoundingClientRect(), br = barEl.getBoundingClientRect(), wrap = chart.parentElement.getBoundingClientRect();
  tip.hidden = false;
  const left = Math.min(wrap.width - tip.offsetWidth / 2 - 4, Math.max(tip.offsetWidth / 2 + 4, br.left - wrap.left + br.width / 2));
  const barTop = barEl.firstChild.getBoundingClientRect().top - wrap.top, chartTop = cr.top - wrap.top;
  tip.style.left = left + 'px'; tip.style.top = Math.max(chartTop + 6, barTop - tip.offsetHeight - 12) + 'px';
}
chart.addEventListener('pointerover', e => { const b = e.target.closest('.bar'); if (b) showTip(b); });
chart.addEventListener('pointerleave', () => { tip.hidden = true; chart.querySelectorAll('.hot').forEach(el => el.classList.remove('hot')); });
chart.addEventListener('click', e => { const b = e.target.closest('.bar'); if (b) showTip(b); });

// Pricing: the real pricing.json. The selected tool's models float to the top and are marked.
const VENDORS = ['all', 'anthropic', 'openai', 'google', 'xai', 'deepseek', 'moonshot', 'zhipu', 'qwen'];
const VENDOR_NAME = { anthropic: 'Anthropic', openai: 'OpenAI', google: 'Google', xai: 'xAI', deepseek: 'DeepSeek', moonshot: 'Kimi', zhipu: 'GLM', qwen: 'Qwen' };
const vendorOf = key => { const n = key.split('/').pop(); return /^claude/.test(n) ? 'anthropic' : /^(gpt|o\d|codex|chatgpt)/.test(n) ? 'openai' : /^gemini/.test(n) ? 'google' : /^grok/.test(n) ? 'xai' : /^deepseek/.test(n) ? 'deepseek' : /^(kimi|moonshot)/.test(n) ? 'moonshot' : /^glm/.test(n) ? 'zhipu' : /^(qwen|qwq)/.test(n) ? 'qwen' : 'other'; };
const FEATURED = ['gpt-6-astra', 'gpt-6.1-sol', 'gpt-6-sol', 'gpt-6-luna', 'claude-opus-5-5', 'claude-fable-5-1', 'claude-sonnet-5-5', 'gemini-3.1-pro-preview', 'gemini-3.8-flash', 'gpt-5.6-sol', 'gpt-5.5', 'gpt-5.3-codex', 'deepseek-v4-pro', 'kimi-k2.7-code', 'glm-5.2', 'claude-haiku-4-5', 'qwen3-coder-plus'];
const PRICE_FALLBACK = { updated: '2026-09-30', models: { 'claude-opus-5-5': { in: 4, out: 20, cr: .2, cw: 5, fast: 2 }, 'claude-fable-5-1': { in: 10, out: 50, cr: .25, cw: 12.5 }, 'claude-sonnet-5-5': { in: 2, out: 10, cr: .2, cw: 2.5 }, 'gpt-5.5': { in: 5, out: 30, cr: .5, long: { at: 272000 }, fast: 2.5 }, 'gpt-5.3-codex': { in: 1.75, out: 14, cr: .175, fast: 2 }, 'gemini-3.1-pro-preview': { in: 2, out: 12, cr: .2, long: { at: 200000 }, fast: 1.8 }, 'deepseek-v4-pro': { in: 1.32, out: 3.96, cr: .044 }, 'kimi-k2.7-code': { in: .95, out: 4, cr: .19 }, 'glm-5.2': { in: 1.4, out: 4.4, cr: .28 }, 'claude-haiku-4-5': { in: 1, out: 5, cr: .1, cw: 1.25 }, 'qwen3-coder-plus': { in: 1, out: 5 } } };
let prices = [], priceVendor = 'all', priceQ = '';
function loadPrices(json) {
  const rank = k => { const i = FEATURED.indexOf(k); return i < 0 ? 999 : i; };
  prices = Object.entries(json.models).map(([key, v]) => ({ key, v, vendor: vendorOf(key), rank: rank(key) }))
    .sort((a, b) => a.rank - b.rank || VENDORS.indexOf(a.vendor) - VENDORS.indexOf(b.vendor) || a.key.localeCompare(b.key, 'en', { numeric: true }));
  $('price-date').textContent = String(json.updated || '').slice(0, 10) || '2026-09-30';
  renderVendors(); renderPrices();
}
function renderVendors() { $('vendors').innerHTML = VENDORS.filter(v => v === 'all' || prices.some(p => p.vendor === v)).map(v => `<button type="button" data-vendor="${v}" aria-pressed="${v === priceVendor}">${v === 'all' ? t('vendorAll') : VENDOR_NAME[v]}</button>`).join(''); }
const money = v => '$' + String(+v.toFixed(3));
function renderPrices() {
  const words = priceQ.toLowerCase().split(/\s+/).filter(Boolean);
  const mine = (MODELS[dash.tool] || []).map(m => m[0]);
  const filtered = priceQ || priceVendor !== 'all';
  let rows = prices.filter(p => (priceVendor === 'all' || p.vendor === priceVendor) && words.every(w => p.key.includes(w)));
  rows = [...rows.filter(p => mine.includes(p.key)), ...rows.filter(p => !mine.includes(p.key))];
  const shown = rows;
  $('ptable-wrap').style.setProperty('--c', cv(dash.tool));
  $('price-rows').innerHTML = shown.length ? shown.map(({ key, v }) => {
    const use = mine.includes(key) ? `<span class="tag use">${esc(t('tagUse', { tool: P[dash.tool].name }))}</span>` : '';
    const tags = use + (v.long && v.long.at ? `<span class="tag">${t('tagLong', { k: Math.round(v.long.at / 1000) })}</span>` : '') + (v.fast && v.fast > 1 ? `<span class="tag fast">Fast ×${trim(v.fast, 1)}</span>` : '');
    const cr = v.cr != null ? money(v.cr) : `<span class="none" title="${esc(t('defaultCache'))}">${money(v.in * .1)}*</span>`;
    return `<tr class="${use ? 'mine' : ''}"><td title="${esc(key)}">${esc(key)}${tags}</td><td>${money(v.in)}</td><td>${money(v.out)}</td><td>${cr}</td></tr>`;
  }).join('') : `<tr><td colspan="4" style="text-align:center;color:var(--ink-3);font-family:var(--sans)">${t('priceNone')}</td></tr>`;
  $('price-count').textContent = filtered ? t('priceMatch', { n: rows.length }) : t('priceTotal', { n: prices.length });
}
$('vendors').addEventListener('click', e => { const b = e.target.closest('[data-vendor]'); if (!b) return; priceVendor = b.dataset.vendor; renderVendors(); renderPrices(); });
$('price-q').addEventListener('input', e => { priceQ = e.target.value.trim(); renderPrices(); });
const getJson = (url, ms) => { const ac = new AbortController(), timer = setTimeout(() => ac.abort(), ms); return fetch(url, { cache: 'no-cache', signal: ac.signal }).then(r => { clearTimeout(timer); if (!r.ok) throw new Error(r.status); return r.json(); }); };
getJson('https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/source/Windows/pricing.json', 5000)
  .then(j => { if (!j || !j.models || Object.keys(j.models).length < 100) throw new Error('short'); return j; })
  .catch(() => getJson('pricing.json', 5000)).then(loadPrices).catch(() => loadPrices(PRICE_FALLBACK));

// Speed rows double as a tool picker.
const speedList = $('speed-list');
const SPEED_IDS = ['claude', 'codex', 'cursor', 'zcode', 'kimi', 'deepseek'];
const speedBase = id => TOOLS[id] ? TOOLS[id].speed : usagePack(id).speed;
speedList.innerHTML = SPEED_IDS.map(id => `<button type="button" class="sp-row" data-tool="${id}" style="--c:${cv(id)}"><span class="sp-name">${icon(id)}${P[id].name}</span><span class="sp-track"><i></i></span><span class="sp-val" data-sp="${id}">0 t/s</span></button>`).join('');
const paintSpeedSel = () => speedList.querySelectorAll('.sp-row').forEach(r => r.setAttribute('aria-pressed', String(r.dataset.tool === dash.tool)));
speedList.addEventListener('click', e => { const r = e.target.closest('[data-tool]'); if (r) pickTool(r.dataset.tool); });
let speedLive = null;
function fillSpeeds(jitter) {
  speedList.querySelectorAll('.sp-row').forEach(row => {
    const id = row.dataset.tool, v = speedBase(id) * (jitter ? 1 + (Math.random() - .5) * .08 : 1);
    row.querySelector('i').style.setProperty('--w', Math.min(100, v).toFixed(1) + '%');
    const el = row.querySelector('[data-sp]'); jitter ? (el.textContent = tps(v), el._v = v) : countTo(el, v, tps);
  });
}
new IntersectionObserver(([entry]) => {
  if (entry.isIntersecting) { if (!speedLive) { fillSpeeds(false); if (!reduced) speedLive = setInterval(() => { if (!document.hidden) fillSpeeds(true); }, 1800); } }
  else { clearInterval(speedLive); speedLive = null; }
}, { threshold: .3 }).observe(speedList);

// Relay routes: the active one carries the flow; click one to follow it.
const flows = [...document.querySelectorAll('#relay .flow')], relayNodes = [...document.querySelectorAll('#relay .hit .node')];
const RELAY = [{ key: 'relayOfficial', tok: '12.4M', n: 1833, speed: 89 }, { key: 'relayA', tok: '41.0M', n: 228, speed: 71 }, { key: 'relayB', tok: '8.6M', n: 97, speed: 64 }];
let relayOn = 1, relayHold = 0;
function paintRelay() {
  flows.forEach((f, i) => f.classList.toggle('on', i === relayOn)); relayNodes.forEach((n, i) => n.classList.toggle('on', i === relayOn));
  const r = RELAY[relayOn]; $('relay-note').innerHTML = t('relayNote', { name: esc(t(r.key)), tok: r.tok, n: int(r.n), speed: tps(r.speed) });
}
document.getElementById('relay').addEventListener('click', e => { const g = e.target.closest('[data-relay]'); if (!g) return; relayOn = +g.dataset.relay; relayHold = Date.now() + 9000; paintRelay(); });
if (!reduced) setInterval(() => { if (document.hidden || Date.now() < relayHold) return; relayOn = [1, 0, 1, 2][(Date.now() / 2600 | 0) % 4]; paintRelay(); }, 2600);

// ── 02 Sizes: four windows stacked small to large; the chosen one opens inside the frame ──
const SIZE_PAGES = ['kimi', 'zcode', 'claude', 'codex', 'cursor', 'deepseek', 'pi'];
const sz = { small: { id: 'kimi', wi: 0 }, medium: { id: 'zcode', wi: 0 }, large: { id: 'cursor', period: '7d', open: -1 }, full: { page: 'overview', period: '7d', id: 'codex' } };
const periodsHtml = cur => ['today', '7d', '30d'].map(p => `<button type="button" data-period="${p}" aria-pressed="${p === cur}">${PERIOD_ZH[p]}</button>`).join('');
const iconBtns = (cur, ids) => `<div class="sz-icons">${ids.map(id => `<button type="button" data-prov="${id}" aria-pressed="${id === cur}" aria-label="${P[id].name}">${icon(id)}</button>`).join('')}</div>`;
const chromeOf = (id, right) => `<div class="sz-head">${id === 'overview' ? OVERVIEW_ICON : icon(id)}<span class="sz-name">${id === 'overview' ? '概览' : P[id].name}</span>${right || ''}</div>`;
function smallHtml() {
  const st = sz.small, q = QUOTAS[st.id], pk = usagePack(st.id);
  let bodyHtml;
  if (q.windows) { const w = q.windows[st.wi % q.windows.length]; bodyHtml = `<button type="button" class="sz-big" data-cycle><b>${w.rem}%</b><span>${w.label}剩余  ${st.wi % q.windows.length + 1}/${q.windows.length}</span></button>${meter(w.rem, st.id)}<p class="sz-reset">${w.reset}后重置</p>`; }
  else if (q.balance) bodyHtml = `<div class="sz-big"><b>${q.balance.replace('USD ', '$')}</b><span>可用余额</span></div>`;
  else bodyHtml = `<div class="sz-big"><b>${compact(pk.tokens)}</b><span>近 30 天 Token</span></div><p class="sz-reset">${int(pk.req)} 次请求 · ${tps(pk.speed)}</p>`;
  const dots = SIZE_PAGES.map(id => `<button type="button" data-prov="${id}" aria-pressed="${id === st.id}" aria-label="${P[id].name}"></button>`).join('');
  return `${chromeOf(st.id, '')}<div class="sz-body" style="display:flex;flex-direction:column">${bodyHtml}<div class="sz-dots">${dots}</div></div>`;
}
function mediumHtml() {
  const st = sz.medium, q = QUOTAS[st.id], pk = usagePack(st.id);
  let left, right;
  if (q.windows) {
    const w = q.windows[st.wi % q.windows.length];
    left = `<button type="button" class="sz-big" data-cycle><b>${w.rem}%</b><span>${w.label}剩余</span></button><p class="sz-reset">${w.reset}后重置</p>`;
    right = q.windows.slice(0, 3).map((win, i) => `<button type="button" class="sz-row" data-win="${i}"><span class="when">${win.reset}</span>${win.label} <b>${win.rem}%</b>${meter(win.rem, st.id)}</button>`).join('');
  } else if (q.balance) {
    left = `<div class="sz-big"><b>${q.balance.replace('USD ', '$')}</b><span>可用余额</span></div>`;
    right = `<div class="sz-row">30 天 Token <b>${compact(pk.tokens)}</b></div><div class="sz-row">请求 <b>${int(pk.req)}</b></div><div class="sz-row">输出速度 <b>${tps(pk.speed)}</b></div>`;
  } else {
    left = `<div class="sz-big"><b>${usd(pk.cost)}</b><span>近 30 天</span></div><p class="sz-reset">今日 ${usd(pk.today)}</p>`;
    right = `<div class="sz-row">Token <b>${compact(pk.tokens)}</b></div><div class="sz-row">请求 <b>${int(pk.req)}</b></div><div class="sz-row">输出速度 <b>${tps(pk.speed)}</b></div>`;
  }
  return `${chromeOf(st.id, iconBtns(st.id, SIZE_PAGES))}<div class="sz-body"><div class="sz-split"><div>${left}</div><div class="sz-side">${right}</div></div></div><div class="sz-foot"><span>今日 ${usd(pk.today)} · 30 天 ${usd(pk.cost)} · ${tps(pk.speed)}</span></div>`;
}
function largeHtml() {
  const st = sz.large, q = QUOTAS[st.id], rows = rowsFor(st.id, st.period);
  let quotas = '';
  if (q.windows) quotas = q.windows.map((w, i) => `<button type="button" class="sz-q" data-open="${i}"><div class="lab">${w.label} <b>${w.rem}%</b> 剩余 <span class="faint">${w.reset}后重置</span></div>${meter(w.rem, st.id)}${st.open === i ? `<div class="sz-more">${paceText(w)} · 窗口 ${w.len}</div>` : ''}</button>`).join('');
  else if (q.balance) quotas = `<div class="sz-big"><b>${q.balance.replace('USD ', '$')}</b><span>可用余额</span></div>`;
  else if (q.local) quotas = `<p class="sz-reset">没有账户额度 · 只统计本机日志</p>`;
  else quotas = `<p class="sz-reset">尚未连接 · 下面是本机用量</p>`;
  const tb = SIZE_PAGES.map(id => `<button type="button" data-prov="${id}" aria-selected="${id === st.id}">${icon(id)}<span>${P[id].name}</span></button>`).join('');
  return `${chromeOf(st.id, '')}<div class="sz-tabs">${tb}</div><div class="sz-body sz-scroll">${quotas}<div class="row" style="margin-top:6px"><span class="dim">本机用量</span><div class="sz-periods">${periodsHtml(st.period)}</div></div><div class="sz-metrics"><div><span>费用</span><b>${usd(sum(rows, 'cost'))}</b></div><div><span>Token</span><b>${compact(sum(rows, 'tokens'))}</b></div><div><span>请求</span><b>${int(Math.max(1, sum(rows, 'req')))}</b></div><div><span>速度</span><b>${tps(speedOfRows(rows, st.id))}</b></div></div>${barsHtml(rows, cv(st.id))}</div>`;
}
function fullHtml() {
  const st = sz.full, page = st.page;
  const tb = ['overview', ...IDS].map(id => `<button type="button" data-page="${id}" aria-selected="${id === page}">${id === 'overview' ? '概览' : P[id].name}</button>`).join('');
  let inner;
  if (page === 'overview') {
    const rows = rowsFor('codex', st.period);
    const tot = USAGE_IDS.reduce((s, k) => s + sum(rowsFor(k, st.period), 'cost'), 0), tok = USAGE_IDS.reduce((s, k) => s + sum(rowsFor(k, st.period), 'tokens'), 0), rq = USAGE_IDS.reduce((s, k) => s + sum(rowsFor(k, st.period), 'req'), 0);
    inner = `<div class="app-card"><div class="row"><span class="dim">全部平台 · API 等价费用</span><div class="sz-periods">${periodsHtml(st.period)}</div></div><div class="row" style="margin-top:6px"><span class="big">${usd(tot)}</span><span class="num">${compact(tok)} Token</span></div><div class="faint">${int(rq)} 次请求</div>${barsHtml(rows, cv('codex'))}</div>${['claude', 'kimi', 'zcode', 'cursor'].map((id, i) => providerCard(id, i)).join('')}`;
  } else {
    const q = QUOTAS[page], rows = rowsFor(page, st.period);
    inner = `<div class="app-card"><div class="card-head">${icon(page)}<b>${P[page].name}</b>${q.plan ? `<span class="plan">${q.plan}</span>` : ''}</div>${quotaLines(page, false)}</div><div class="app-card"><div class="row"><b style="font-size:13px">本机用量</b><div class="sz-periods">${periodsHtml(st.period)}</div></div><div class="sz-metrics"><div><span>费用</span><b>${usd(sum(rows, 'cost'))}</b></div><div><span>Token</span><b>${compact(sum(rows, 'tokens'))}</b></div><div><span>请求</span><b>${int(Math.max(1, sum(rows, 'req')))}</b></div><div><span>速度</span><b>${tps(speedOfRows(rows, page))}</b></div></div>${barsHtml(rows, cv(page))}</div>`;
  }
  return `${chromeOf(page, '')}<div class="sz-tabs">${tb}</div><div class="sz-body sz-scroll">${inner}</div>`;
}
function renderWindow(kind, win) {
  win.innerHTML = kind === 'small' ? smallHtml() : kind === 'medium' ? mediumHtml() : kind === 'large' ? largeHtml() : fullHtml();
  win.dataset.kind = kind;
  const packId = kind === 'full' ? (sz.full.page === 'overview' ? 'codex' : sz.full.page) : sz[kind].id;
  win._rows = rowsFor(packId, sz[kind].period || '30d'); win._pack = packId;
  win.querySelectorAll('.go').forEach(el => el.classList.remove('go'));
}
function paintKind(kind) {
  document.querySelectorAll(`#stack .stack-card[data-size="${kind}"] .app`).forEach(win => renderWindow(kind, win));
  if (showApp.dataset.kind === kind) renderWindow(kind, showApp);
}
// Design sizes of the window mocks.
const SIZE_DESIGN = { small: [220, 220], medium: [440, 220], large: [440, 520], full: [480, 790] };
const SIZE_META = [{ key: 'small', title: 'sSmall', text: 'vSmall' }, { key: 'medium', title: 'sMedium', text: 'vMedium' }, { key: 'large', title: 'sLarge', text: 'vLarge' }, { key: 'full', title: 'sFull', text: 'vFull' }];
const stackEl = $('stack'), showWin = $('show-win'), showApp = $('show-app'), frameShow = $('frame-show');
let sizeSel = 'medium';
function layoutShow() {
  const [dw, dh] = SIZE_DESIGN[sizeSel], cs = getComputedStyle(frameShow);
  const w = frameShow.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
  const narrow = innerWidth < 1121, h = narrow ? dh : frameShow.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
  const scale = Math.max(.3, Math.min(1, w / dw, h / dh));
  showWin.style.setProperty('--vw', Math.round(dw * scale) + 'px'); showWin.style.setProperty('--vh', Math.round(dh * scale) + 'px');
  showWin.style.setProperty('--sz-w', dw + 'px'); showWin.style.setProperty('--sz-h', dh + 'px'); showWin.style.setProperty('--sz-s', String(scale));
}
function paintCaption() { const m = SIZE_META.find(x => x.key === sizeSel); $('size-caption').innerHTML = `<h3>${esc(t(m.title))}</h3><p>${esc(t(m.text))}</p>`; }
function selectSize(kind, from) {
  sizeSel = kind;
  stackEl.querySelectorAll('.stack-card').forEach(c => c.setAttribute('aria-selected', String(c.dataset.size === kind)));
  layoutShow(); renderWindow(kind, showApp); animateMeters(showApp); paintCaption();
  // The window grows out of the card that was clicked, inside the frame.
  if (from && !reduced) {
    const a = from.getBoundingClientRect(), b = showWin.getBoundingClientRect();
    showWin.style.transition = 'none';
    showWin.style.transform = `translate(${a.left - b.left}px, ${a.top - b.top}px) scale(${a.width / b.width}, ${a.height / b.height})`;
    showWin.style.opacity = '.4';
    void showWin.offsetWidth;
    showWin.style.transition = 'transform .5s cubic-bezier(.2,.75,.2,1), opacity .35s';
    showWin.style.transform = 'none'; showWin.style.opacity = '1';
  }
}
stackEl.addEventListener('click', e => {
  const c = e.target.closest('.stack-card'); if (!c) return;
  selectSize(c.dataset.size, c);
  // On narrow screens the frame stacks vertically; bring the opened window into view.
  if (innerWidth < 1121) setTimeout(() => showWin.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'nearest' }), 120);
});
stackEl.addEventListener('keydown', e => {
  if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
  const order = SIZE_META.map(m => m.key), i = order.indexOf(sizeSel), next = order[(i + (e.key === 'ArrowRight' ? 1 : order.length - 1)) % order.length];
  const card = stackEl.querySelector(`[data-size="${next}"]`); card.focus(); selectSize(next, card); e.preventDefault();
});
// Everything inside the shown window responds, like the real one.
showWin.addEventListener('click', e => {
  const kind = showApp.dataset.kind, st = sz[kind];
  const page = e.target.closest('[data-page]'), period = e.target.closest('[data-period]'), prov = e.target.closest('[data-prov]'), row = e.target.closest('[data-win]'), open = e.target.closest('[data-open]');
  if (page) sz.full.page = page.dataset.page;
  else if (period) st.period = period.dataset.period;
  else if (prov) { st.id = prov.dataset.prov; st.wi = 0; if ('open' in st) st.open = -1; }
  else if (e.target.closest('[data-cycle]')) { const q = QUOTAS[st.id]; if (q && q.windows) st.wi = (st.wi + 1) % q.windows.length; }
  else if (row) st.wi = +row.dataset.win;
  else if (open) st.open = st.open === +open.dataset.open ? -1 : +open.dataset.open;
  else return;
  paintKind(kind); animateMeters(showApp);
});
showWin.addEventListener('pointerover', e => {
  const b = e.target.closest('.mini-chart .b'); if (!b) return;
  const win = showApp, x = (win._rows || [])[+b.dataset.i]; if (!x) return;
  let tp = win.querySelector('.sz-tip'); if (!tp) { tp = document.createElement('div'); tp.className = 'sz-tip'; win.appendChild(tp); }
  tp.innerHTML = `<b>${x.hourly ? pad2(x.date.getHours()) + ':00' : dayLabel(x.date)}</b><br>${usd(x.cost)} · ${compact(x.tokens)} Token<br>${int(Math.max(0, x.req || 0))} 次请求 · ${tps(x.speed || usagePack(win._pack).speed)}`;
  const host = win.getBoundingClientRect(), br = b.getBoundingClientRect(), scale = host.width / (win.offsetWidth || host.width) || 1;
  tp.hidden = false;
  tp.style.left = Math.min(win.offsetWidth - 8, Math.max(8, (br.left - host.left + br.width / 2) / scale)) + 'px';
  tp.style.top = Math.max(8, (br.top - host.top) / scale - 8) + 'px'; tp.style.transform = 'translate(-50%, -100%)';
});
showWin.addEventListener('pointerout', e => { if (!e.target.closest('.mini-chart .b')) return; const tp = showApp.querySelector('.sz-tip'); if (tp && !e.relatedTarget?.closest?.('.mini-chart .b')) tp.hidden = true; });
addEventListener('resize', () => layoutShow());
function openFromHash() { const m = /^#size-(small|medium|large|full)$/.exec(location.hash); if (!m) return; $('sizes').scrollIntoView({ block: 'start' }); selectSize(m[1], stackEl.querySelector(`[data-size="${m[1]}"]`)); }
addEventListener('hashchange', openFromHash);

// ── 04 Install: the tab on the left drives the window on the right ────
const SETUP_URL = REPO + '/releases/download/v1.0.0/codeusagemonit-setup-1.0.0.exe';
const ZIP_URL = REPO + '/releases/download/v1.0.0/codeusagemonit-1.0.0-win-x64.zip';
const CMD = {
  bucket: 'scoop bucket add codeusagemonit ' + REPO, scoop: 'scoop install codeusagemonit', update: 'scoop update codeusagemonit',
  ps: 'irm https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/install.ps1 | iex'
};
const USAGE = [{ id: 'status', cmd: 'codeusage status', desc: 'cmdStatus' }, { id: 'cost', cmd: 'codeusage cost --days 7', desc: 'cmdCost' }, { id: 'thirdparty', cmd: 'codeusage thirdparty', desc: 'cmdThird' }];
const DL_ICON = '<svg viewBox="0 0 20 20" width="17" height="17" aria-hidden="true"><path d="M10 3v9m0 0-3.5-3.5M10 12l3.5-3.5M4 15.5h12" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const PS_ICON = '<svg viewBox="0 0 16 16" aria-hidden="true"><rect x="1" y="2" width="14" height="12" rx="2.5" fill="#1F4E8C"/><path d="m4 6 2.5 2L4 10m4 0h4" stroke="#fff" stroke-width="1.3" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const APP_ICON = '<img src="favicon.svg" width="14" height="14" alt="">';
let instKind = 'scoop', termCmd = 'status';
const cmdBox = (text, extra = '') => `<div class="cmd"${extra}><code><span class="pr">&gt; </span>${cmdText(text)}</code><button type="button" class="copy-btn" data-copy="${esc(text)}" aria-label="${t('copyLabel')}" title="${t('copyLabel')}">${COPY_ICON}</button></div>`;
const step = (label, inner) => `<li class="step"><div class="step-main"><p class="step-label">${label}</p>${inner || ''}</div></li>`;
function paintInstall() {
  let html;
  if (instKind === 'scoop') html = `<p class="inst-intro">${esc(t('instScoopIntro'))}</p><ol class="steps">${step(esc(t('instScoop1')), cmdBox(CMD.bucket))}${step(esc(t('instScoop2')), cmdBox(CMD.scoop))}${step(`${esc(t('instScoop3'))}<small>${esc(t('optional'))}</small>`, cmdBox(CMD.update))}</ol><p class="inst-foot">${t('instFootScoop')}</p>`;
  else if (instKind === 'ps') html = `<p class="inst-intro">${esc(t('instPsIntro'))}</p><ol class="steps">${step(esc(t('instPsRun')), cmdBox(CMD.ps) + `<ul class="bullets">${['instPs2', 'instPs3', 'instPs4'].map(k => `<li>${esc(t(k))}</li>`).join('')}</ul>`)}</ol><p class="inst-foot">${t('instFootPs')}</p>`;
  else if (instKind === 'setup') html = `<ol class="steps">${step(esc(t('instSetup1')), `<a class="btn btn-primary inst-dl" href="${SETUP_URL}">${DL_ICON}${esc(t('instSetupBtn'))}</a><span class="inst-alt"><a href="${ZIP_URL}">${esc(t('instZipLink'))}</a></span>`)}${step(esc(t('instSetup2')), `<ul class="bullets"><li>${esc(t('instSetupB1'))}</li><li>${esc(t('instSetupB2'))}</li></ul>`)}${step(esc(t('instSetup3')))}</ol><p class="inst-foot">${t('instFootSetup')}</p>`;
  else html = `<p class="inst-intro">${esc(t('instCliIntro'))}</p><ol class="steps">${USAGE.map(u => step(esc(t(u.desc)), cmdBox(u.cmd, ` data-cmd="${u.id}" role="button" tabindex="0" aria-pressed="${u.id === termCmd}"`).replace('class="cmd"', 'class="cmd pick"'))).join('')}</ol><p class="inst-foot">${t('instFootCli')}</p>`;
  const el = $('inst-body'); el.innerHTML = html; el.style.animation = 'none'; void el.offsetWidth; el.style.animation = '';
  document.querySelectorAll('[data-inst]').forEach(b => b.setAttribute('aria-selected', String(b.dataset.inst === instKind)));
  const title = { scoop: [PS_ICON, 'winScoop'], ps: [PS_ICON, 'winPs'], setup: [APP_ICON, 'winSetup'], cli: [PS_ICON, 'winCli'] }[instKind];
  $('win-title').innerHTML = title[0] + esc(t(title[1]));
  $('win').classList.toggle('is-wizard', instKind === 'setup');
}
const markStep = i => $('inst-body').querySelectorAll('.step').forEach((s, k) => s.classList.toggle('on', k === i));
$('inst-tabs').addEventListener('click', e => { const b = e.target.closest('[data-inst]'); if (!b || b.dataset.inst === instKind) return; instKind = b.dataset.inst; paintInstall(); runWin(); });
$('inst-body').addEventListener('click', e => { if (e.target.closest('[data-copy]')) return; const row = e.target.closest('[data-cmd]'); if (row) { termCmd = row.dataset.cmd; document.querySelectorAll('[data-cmd]').forEach(x => x.setAttribute('aria-pressed', String(x.dataset.cmd === termCmd))); runWin(); } });
$('inst-body').addEventListener('keydown', e => { const row = e.target.closest('[data-cmd]'); if (row && !e.target.closest('[data-copy]') && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); row.click(); } });

// Terminal output, mirroring scoop, install.ps1 and codeusage.
const widthOf = s => [...s].reduce((w, ch) => w + (/[\u1100-\u115F\u2E80-\uA4CF\uAC00-\uD7A3\uF900-\uFAFF\uFE30-\uFE4F\uFF00-\uFF60\uFFE0-\uFFE6]/.test(ch) ? 2 : 1), 0);
const padR = (s, w) => s + ' '.repeat(Math.max(1, w - widthOf(s)));
const padL = (s, w) => ' '.repeat(Math.max(1, w - widthOf(s))) + s;
const col = (s, c) => `<span style="color:${c}">${esc(s)}</span>`, dim = s => `<span class="d">${esc(s)}</span>`, bold = s => `<span class="b">${esc(s)}</span>`, green = s => `<span class="g">${esc(s)}</span>`;
const bar = (rem, c) => { const on = Math.round(rem / 100 * 24); return col('▮'.repeat(on), rem < 10 ? '#E6A083' : c) + dim('▯'.repeat(24 - on)); };
function stamp() { const d = new Date(); return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`; }
function quotaBlock(id) {
  const q = QUOTAS[id], lw = Math.max(8, Math.max(...q.windows.map(w => widthOf(w.label))) + 2), lines = [], pk = usagePack(id);
  lines.push(`<span style="color:${P[id].c};font-weight:600">${P[id].name}</span>  ${dim(q.plan)}  ${dim('de•••@example.com')}`);
  q.windows.forEach(w => lines.push('  ' + esc(padR(w.label, lw)) + bold(padL(w.rem + '%', 6)) + ' 剩余  ' + bar(w.rem, P[id].c) + '  ' + dim(w.reset.replace(' ', '') + '后重置')));
  lines.push('  ' + dim(`今日 ${usd(pk.today)} · 30 天 ${usd(pk.cost)} · ${compact(pk.tokens)} Token · ${tps(pk.speed)}`));
  return lines;
}
function costLines() {
  const ids = ['claude', 'kimi', 'zcode'], days = [23, 24, 25, 26, 27, 28, 29], md = d => `${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
  const lines = [bold('近 7 天 · 本机用量') + dim('（本机日志 × 官方 API 价目估算，不是订阅账单）'), dim(padR('日期', 8) + ids.map(id => padL(P[id].name, 11)).join('') + padL('合计', 11) + padL('Token', 9))];
  days.forEach(i => { const cells = ids.map(id => SERIES[id].days[i].cost), toks = ids.reduce((s, id) => s + SERIES[id].days[i].tokens, 0); lines.push(esc(padR(md(SERIES.codex.days[i].date), 8) + cells.map(v => padL(usd(v), 11)).join('')) + bold(padL(usd(cells.reduce((a, b) => a + b, 0)), 11)) + dim(padL(compact(toks), 9))); });
  const tot = ids.map(id => days.reduce((s, i) => s + SERIES[id].days[i].cost, 0));
  lines.push(bold(padR('合计', 8) + tot.map(v => padL(usd(v), 11)).join('') + padL(usd(tot.reduce((a, b) => a + b, 0)), 11)));
  lines.push(dim('输出速度  ') + ids.map(id => P[id].name + ' ' + bold(tps(speedOf(days.map(i => SERIES[id].days[i]), id)))).join(dim(' · ')));
  return lines;
}
const COMMANDS = {
  status: () => [bold('codeusagemonit') + dim(` V1.0.0 · ${stamp()} · 缓存 · 2 分钟前`), '', ...quotaBlock('claude'), '', ...quotaBlock('zcode')],
  cost: costLines,
  thirdparty: () => [bold('第三方 API 用量') + dim('（本机日志；服务商的周/月限额无法得知）'), '',
    `<span style="color:${P.claude.c};font-weight:600">示例中转 A</span>` + col('  使用中', '#5CC8E0') + '  ' + dim('Claude Code · relay-a.example.com'),
    '  今日 ' + bold('3.36M') + '  近 7 天 ' + bold('19.44M') + '  近 30 天 ' + bold('41.04M') + ' Token' + dim('  · 228 次请求 · 71 t/s · 官方价参考 ≈$49.25'), '',
    `<span style="color:${P.codex.c};font-weight:600">示例中转 B</span>` + '  ' + dim('Codex · api.relay-b.example.org'),
    '  今日 ' + bold('0') + '  近 7 天 ' + bold('11.44M') + '  近 30 天 ' + bold('84.24M') + ' Token' + dim('  · 468 次请求 · 29 t/s · 官方价参考 ≈$168.48')]
};
const LOCAL = 'C:\\Users\\you\\AppData\\Local\\Programs\\codeusagemonit';
// A script is a list of steps: { cmd, step } is typed at the prompt; { out } prints lines; { bar } animates a download bar.
function scriptFor(kind) {
  if (kind === 'scoop') return [
    { cmd: CMD.bucket, step: 0 }, { out: ['Checking repo... OK', 'The codeusagemonit bucket was added successfully.'] },
    { cmd: CMD.scoop, step: 1 }, { out: [`Installing 'codeusagemonit' (1.0.0) [64bit] from 'codeusagemonit' bucket`] },
    { bar: 'codeusagemonit-1.0.0-win-x64.zip (950.8 KB)' },
    { out: ['Checking hash of codeusagemonit-1.0.0-win-x64.zip ... ok.', 'Extracting codeusagemonit-1.0.0-win-x64.zip ... done.', 'Linking ~\\scoop\\apps\\codeusagemonit\\current => ~\\scoop\\apps\\codeusagemonit\\1.0.0', `Creating shim for 'codeusage'.`, 'Creating shortcut for codeusagemonit (codeusagemonit.exe)', 'Persisting data', green(`'codeusagemonit' (1.0.0) was installed successfully!`), '', 'Notes', '-----', 'Start codeusagemonit from the Start menu; the CLI is `codeusage`.'], raw: [6] }
  ];
  if (kind === 'ps') return [
    { cmd: CMD.ps, step: 0 },
    { out: ['codeusagemonit: looking up the release...', 'codeusagemonit: downloading codeusagemonit-1.0.0-win-x64.zip (0.93 MB)...', 'codeusagemonit: SHA-256 verified (ef2c9412…4093bda).', `codeusagemonit: added ${LOCAL} to your user PATH (new terminals pick it up).`, '', green(`codeusagemonit v1.0.0 installed to ${LOCAL}`), '  Start it from the Start menu, or run: codeusagemonit', '  Terminal: codeusage status, codeusage cost --days 7', '  Update: run this command again.'], raw: [5] }
  ];
  const u = USAGE.find(x => x.id === termCmd);
  return [{ cmd: u.cmd, step: USAGE.indexOf(u) }, { out: COMMANDS[termCmd](), html: true }];
}
const termBody = $('term-body'), wizard = $('wizard');
let winRun = 0, winSeen = false;
async function runWin() {
  const run = ++winRun, alive = () => run === winRun;
  if (instKind === 'setup') { termBody.hidden = true; wizard.hidden = false; return runWizard(alive); }
  termBody.hidden = false; wizard.hidden = true;
  const prompt = '<span class="p">PS C:\\Users\\you&gt;</span> ', caret = '<span class="caret"></span>';
  let html = '';
  const paint = tail => { termBody.innerHTML = html + (tail || ''); termBody.scrollTop = termBody.scrollHeight; };
  markStep(-1);
  for (const s of scriptFor(instKind)) {
    if (!alive()) return;
    if (s.cmd) {
      markStep(s.step);
      if (reduced) { html += prompt + esc(s.cmd) + '\n'; continue; }
      for (let i = 1; i <= s.cmd.length; i += s.cmd.length > 60 ? 2 : 1) { if (!alive()) return; paint(prompt + esc(s.cmd.slice(0, i)) + caret); await sleep(18 + Math.random() * 22); }
      html += prompt + esc(s.cmd) + '\n'; paint(caret); await sleep(380);
    } else if (s.bar) {
      for (let p = 0; p <= 100; p += reduced ? 100 : 5) { if (!alive()) return; const n = Math.round(p / 5); paint(esc(s.bar) + ' [' + '='.repeat(n) + (n < 20 ? '>' + ' '.repeat(19 - n) : '') + '] ' + p + '%\n'); await sleep(55); }
      html += esc(s.bar) + ' [' + '='.repeat(20) + '] 100%\n';
    } else if (s.out) {
      for (let k = 0; k < s.out.length; k++) {
        if (!alive()) return;
        const line = s.out[k], rawLine = s.html || (s.raw && s.raw.includes(k));
        html += (rawLine ? line : esc(line)) + '\n'; paint(caret); await sleep(reduced ? 0 : 70);
      }
      await sleep(240);
    }
  }
  if (!alive()) return;
  html += prompt; paint(caret);
}
async function runWizard(alive) {
  const L = lang;
  const T = L === 'en' ? { next: 'Next >', cancel: 'Cancel', install: 'Install', finish: 'Finish', browse: 'Browse…', welcome: 'Welcome to the codeusagemonit Setup Wizard', welcomeText: 'This will install codeusagemonit 1.0.0 on your computer. Close other applications before continuing.', dir: 'Select Destination Location', dirText: 'Setup will install codeusagemonit into the following folder.', tasks: 'Select Additional Tasks', tasksText: 'Select the additional tasks you would like Setup to perform.', desk: 'Create a desktop shortcut', auto: 'Start with Windows (tray only)', path: 'Add codeusage to your user PATH (new terminals can run it)', installing: 'Installing', installingText: 'Please wait while Setup installs codeusagemonit on your computer.', done: 'Completing the codeusagemonit Setup Wizard', doneText: 'Setup has finished installing codeusagemonit on your computer.', launch: 'Launch codeusagemonit' }
    : { next: '下一步(N) >', cancel: '取消', install: '安装(I)', finish: '完成(F)', browse: '浏览(R)…', welcome: '欢迎使用 codeusagemonit 安装向导', welcomeText: '现在将安装 codeusagemonit 1.0.0 到你的电脑中。建议在继续之前关闭所有其他应用程序。', dir: '选择目标位置', dirText: '安装程序将把 codeusagemonit 安装到以下文件夹中。', tasks: '选择附加任务', tasksText: '请选择在安装 codeusagemonit 期间安装程序要执行的附加任务。', desk: '创建桌面快捷方式', auto: '登录 Windows 后自动启动（只驻留托盘）', path: '将 codeusage 加入当前用户的 PATH（新开的终端可直接运行）', installing: '正在安装', installingText: '安装程序正在安装 codeusagemonit 到你的电脑中，请稍候。', done: 'codeusagemonit 安装完成', doneText: '安装程序已在你的电脑中安装了 codeusagemonit。', launch: '运行 codeusagemonit' };
  const top = (b, s) => `<div class="wz-top"><div><b>${esc(b)}</b><span>${esc(s)}</span></div><img src="favicon.svg" alt="" style="margin-left:auto"></div>`;
  const foot = (pri, back = true) => `<div class="wz-foot">${back ? '<span>&lt; ' + (L === 'en' ? 'Back' : '上一步(B)') + '</span>' : ''}<span class="pri">${esc(pri)}</span><span>${esc(T.cancel)}</span></div>`;
  const check = (on, text) => `<div class="wz-check"><i class="${on ? 'on' : ''}"></i><span>${esc(text)}</span></div>`;
  const pages = [
    () => top(T.welcome, 'codeusagemonit 1.0.0') + `<div class="wz-main"><p>${esc(T.welcomeText)}</p></div>` + foot(T.next, false),
    () => top(T.dir, T.dirText) + `<div class="wz-main"><p>${esc(T.dirText)}</p><div class="wz-path"><span>${esc(LOCAL)}</span><i>${esc(T.browse)}</i></div></div>` + foot(T.next),
    () => top(T.tasks, T.tasksText) + `<div class="wz-main">${check(false, T.desk)}${check(false, T.auto)}${check(true, T.path)}</div>` + foot(T.install)
  ];
  const FILES = ['codeusagemonit.exe', 'codeusage.exe', 'Panel.xaml', 'pricing.json', 'app.ico', 'icons\\codex.svg', 'icons\\claude.svg', 'icons\\cursor.svg', 'icons\\zcode.svg', 'LICENSE', '使用说明.md', 'setup-helper.ps1'];
  markStep(1);
  for (const pg of pages) { if (!alive()) return; wizard.innerHTML = pg(); await sleep(reduced ? 300 : 1700); }
  if (!alive()) return;
  wizard.innerHTML = top(T.installing, T.installingText) + `<div class="wz-main"><p>${esc(T.installingText)}</p><div class="wz-bar"><i style="--p:0%"></i></div><div class="wz-file"></div></div>` + foot(T.next);
  const barEl = wizard.querySelector('.wz-bar i'), fileEl = wizard.querySelector('.wz-file');
  for (let p = 0; p <= 100; p += 4) {
    if (!alive()) return;
    barEl.style.setProperty('--p', p + '%');
    fileEl.textContent = LOCAL + '\\' + FILES[Math.min(FILES.length - 1, Math.floor(p / 100 * FILES.length))];
    await sleep(reduced ? 0 : 190);
  }
  if (!alive()) return;
  markStep(2);
  wizard.innerHTML = top(T.done, '') + `<div class="wz-main"><p>${esc(T.doneText)}</p>${check(true, T.launch)}</div>` + foot(T.finish, false);
}
new IntersectionObserver(([entry], obs) => { if (entry.isIntersecting && !winSeen) { winSeen = true; runWin(); obs.disconnect(); } }, { threshold: .3 }).observe($('win'));
$('win-replay').addEventListener('click', () => runWin());
$('win-copy').addEventListener('click', e => {
  const text = instKind === 'scoop' ? CMD.bucket + '\n' + CMD.scoop : instKind === 'ps' ? CMD.ps : USAGE.find(u => u.id === termCmd).cmd;
  copyText(text, e.currentTarget, () => t('copy'));
});

// ── Pager, nav, mobile menu ───────────────────────────────────────────
const sections = [...document.querySelectorAll('[data-pager]')], pager = $('pager'), navLinks = [...document.querySelectorAll('.nav-links a')], nav = $('nav');
pager.innerHTML = sections.map((s, i) => `<li><a href="#${s.id}"><span class="pg-label" data-pg="${s.dataset.pager}"></span><b>${pad2(i)}</b><i></i></a></li>`).join('');
const pagerLinks = [...pager.querySelectorAll('a')];
let activeSection = -1, scrollFrame = 0;
function paintPagerLabels() { pager.querySelectorAll('[data-pg]').forEach(el => { el.textContent = t(el.dataset.pg); }); pagerLinks.forEach((a, i) => a.setAttribute('aria-label', t(sections[i].dataset.pager))); }
function onScroll() {
  scrollFrame = 0;
  const line = innerHeight * .38;
  let idx = 0; sections.forEach((s, i) => { if (s.getBoundingClientRect().top <= line) idx = i; });
  if (innerHeight + scrollY >= document.documentElement.scrollHeight - 4) idx = sections.length - 1;
  if (idx !== activeSection) {
    activeSection = idx;
    pagerLinks.forEach((a, i) => a.setAttribute('aria-current', String(i === idx)));
    navLinks.forEach(a => a.setAttribute('aria-current', String(a.getAttribute('href') === '#' + sections[idx].id)));
  }
  pager.parentElement.classList.toggle('on-hero', idx === 0);
  nav.classList.toggle('scrolled', scrollY > 8);
}
addEventListener('scroll', () => { if (!scrollFrame) scrollFrame = requestAnimationFrame(onScroll); }, { passive: true });
addEventListener('resize', () => { if (!scrollFrame) scrollFrame = requestAnimationFrame(onScroll); });
const menuBtn = $('menu-btn'), sheetNav = $('sheet');
function setMenu(open) { sheetNav.hidden = !open; menuBtn.setAttribute('aria-expanded', String(open)); }
menuBtn.addEventListener('click', () => setMenu(sheetNav.hidden));
sheetNav.addEventListener('click', e => { if (e.target.closest('a')) setMenu(false); });
addEventListener('resize', () => { if (innerWidth > 1120 && !sheetNav.hidden) setMenu(false); });

// ── Download band: a 24-cell meter that keeps filling ─────────────────
const dlMeter = $('dl-meter');
const GRAD = [[109, 224, 214], [143, 200, 255], [201, 178, 250]];
dlMeter.innerHTML = Array.from({ length: 24 }, (_, i) => { const k = i / 23, a = k < .5 ? GRAD[0] : GRAD[1], b = k < .5 ? GRAD[1] : GRAD[2], f = k < .5 ? k * 2 : (k - .5) * 2; return `<i style="--cc:rgb(${a.map((v, j) => Math.round(v + (b[j] - v) * f)).join(',')})"></i>`; }).join('');
let dlFill = 0, dlTimer = null;
const dlCells = [...dlMeter.children];
function stepMeter() { if (document.hidden) return; dlFill = (dlFill + 1) % 34; dlCells.forEach((c, i) => c.classList.toggle('on', i < Math.min(24, dlFill))); }
new IntersectionObserver(([entry]) => { if (entry.isIntersecting && !dlTimer) { if (reduced) dlCells.forEach(c => c.classList.add('on')); else dlTimer = setInterval(stepMeter, 90); } else if (!entry.isIntersecting) { clearInterval(dlTimer); dlTimer = null; } }).observe(dlMeter);

// ── Language ──────────────────────────────────────────────────────────
const META = {
  zh: ['codeusagemonit — Windows 上的 AI 编程额度与用量监控', document.querySelector('meta[name="description"]').content],
  en: ['codeusagemonit — AI coding quotas and usage on Windows', 'A Windows tray app for the remaining quota, reset times, local token usage and output speed of Codex, Claude, Cursor and 8 more AI coding tools. Priced at official API rates, synced daily. Open source; data stays on your PC.'],
  ja: ['codeusagemonit — Windows で AI コーディングの利用枠と使用量を確認', 'Codex、Claude、Cursor など 11 の AI コーディングツールの残り利用枠、リセット時刻、トークン使用量、出力速度を Windows のトレイで。公式 API 単価で計算し、料金表は毎日同期。オープンソースで、データは PC 内で集計します。']
};
let jaFont = false;
function setLang(value) {
  lang = DICT[value] ? value : 'zh';
  if (lang === 'ja' && !jaFont) { jaFont = true; const l = document.createElement('link'); l.rel = 'stylesheet'; l.href = 'https://fonts.googleapis.cn/css2?family=Noto+Sans+JP:wght@400;500;600;700&display=swap'; document.head.appendChild(l); }
  root.lang = { zh: 'zh-CN', en: 'en', ja: 'ja' }[lang];
  document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
  document.querySelectorAll('[data-i18n-ph]').forEach(el => { el.placeholder = t(el.dataset.i18nPh); });
  document.querySelectorAll('[data-theme-set]').forEach(b => { const k = { light: 'themeLight', dark: 'themeDark', system: 'themeSystem' }[b.dataset.themeSet]; b.title = b.ariaLabel = t(k); });
  document.querySelectorAll('[data-tip]').forEach(b => { b.title = b.ariaLabel = t(b.dataset.tip); });
  menuBtn.ariaLabel = t('menu');
  document.querySelectorAll('.lang [data-lang]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.lang === lang)));
  document.title = META[lang][0]; document.querySelector('meta[name="description"]').content = META[lang][1];
  paintPagerLabels(); renderProviders(); renderDash(); paintCaption(); paintInstall(); paintRelay();
  if (prices.length) { renderVendors(); renderPrices(); }
  if (winSeen) runWin();
  try { localStorage.setItem('codeusagemonit-language', lang); } catch { }
}
document.querySelector('.lang').addEventListener('click', e => { const b = e.target.closest('[data-lang]'); if (b) setLang(b.dataset.lang); });

// ── Reveal on scroll ──────────────────────────────────────────────────
const revealer = new IntersectionObserver(entries => entries.forEach(entry => {
  if (!entry.isIntersecting) return;
  entry.target.classList.add('in'); revealer.unobserve(entry.target);
  if (entry.target.id === 'board-panel') { renderBoardQuota(true); renderDash(); }
}), { threshold: .12, rootMargin: '0px 0px -40px 0px' });
document.querySelectorAll('.reveal').forEach(el => revealer.observe(el));

// ── Start ─────────────────────────────────────────────────────────────
renderTabs(); renderPage('from-r'); renderBoardQuota(false);
document.querySelectorAll('#stack .stack-card').forEach(card => renderWindow(card.dataset.size, card.querySelector('.app')));
selectSize(sizeSel);
setTimeout(openFromHash, 300);
let saved; try { saved = localStorage.getItem('codeusagemonit-language'); } catch { }
setLang(saved || (/^ja/i.test(navigator.language) ? 'ja' : /^zh/i.test(navigator.language) ? 'zh' : 'en'));
onScroll();
