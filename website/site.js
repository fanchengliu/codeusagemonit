'use strict';
/* codeusagemonit website. Figures in the demos are illustrative demo data, taken from (or
   consistent with) the app's own --demo mode. The pricing table is the real pricing.json. */

// ── Providers ─────────────────────────────────────────────────────────
// c: the app's dark-theme colour (the terminal is always dark). Everywhere else the page
// uses var(--p-<id>), which style.css defines for both themes.
const P = {
  codex: { name: 'Codex', c: '#63D5E5' }, claude: { name: 'Claude', c: '#E8AB8C' }, cursor: { name: 'Cursor', c: '#80DDAB' },
  antigravity: { name: 'Antigravity', c: '#B8A0F5' }, deepseek: { name: 'DeepSeek', c: '#7FA9FF' }, grok: { name: 'Grok', c: '#D8DEE9' },
  copilot: { name: 'Copilot', c: '#E58FD0' }, kimi: { name: 'Kimi', c: '#F4C95D' }, opencode: { name: 'OpenCode', c: '#A6D96A' },
  zcode: { name: 'ZCode', c: '#F2874E' }, pi: { name: 'Pi', c: '#A9B4C6' }
};
const IDS = Object.keys(P);
const cv = id => `var(--p-${id})`;
const icon = (id, extra = '') => `<span class="pi ${extra}" style="--icon:url(assets/icons/${id}.svg);--c:${cv(id)}"></span>`;
const esc = s => String(s).replace(/[&<>"]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch]));
const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
const $ = id => document.getElementById(id);

// ── Formatting ────────────────────────────────────────────────────────
const trim = (v, d) => String(+v.toFixed(d));
const usd = v => '$' + v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const compact = n => n >= 1e9 ? trim(n / 1e9, 2) + 'B' : n >= 1e6 ? trim(n / 1e6, 2) + 'M' : n >= 1e3 ? trim(n / 1e3, 1) + 'K' : String(Math.round(n));
const tps = v => (v >= 100 ? Math.round(v) : trim(v, 1)) + ' t/s';
const pad2 = n => String(n).padStart(2, '0');

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
  // A circular reveal of the new theme, growing from the button that was pressed.
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
  pConnect: '连接方式', pQuota: '显示的额度', capQuota: '额度', capLocal: '本机用量', capSpeed: '输出速度', capBeta: '（实验性）', docs: '连接说明 →', custom: '自定义', customLong: '自定义平台',
  vSmall: '一个大数字、一条额度条，占一个图标的位置。点数字在额度窗口之间切换，滚轮或 ←/→ 翻页。',
  vMedium: '左边是主额度，右边是其他窗口；只有一个窗口时补充节奏、重置时间和限额重置额度。中尺寸只放额度，不放图表。',
  vLarge: '完整的额度窗口与节奏估算，加上本机用量：时间段、费用、Token、请求、速度和可以点的柱状图。',
  vFull: '所有平台的卡片、每个平台的详情页、第三方中转站页和设置。拖动边缘可以随意调整大小。',
  vFullDim: '默认 420 × 790，可调整',
  pgTop: '首页', pgProviders: '平台', pgBoard: '额度与用量', pgPricing: '计价', pgSizes: '尺寸', pgInstall: '安装', pgPrivacy: '隐私', pgFaq: '问题', pgDownload: '下载',
  themeLight: '浅色', themeDark: '深色', themeSystem: '跟随系统',
  vendorAll: '全部', priceShown: '显示 {n} / {total} 款', priceNone: '没有匹配的模型', tagLong: '长上下文 >{k}K', defaultCache: '官方未单列，按输入价的 10% 估算',
  instScoopNote: '和 Homebrew 的 tap 一样：先把本仓库加成 bucket，再安装。会创建开始菜单快捷方式，并把 codeusage 加进 PATH；data 文件夹放在 Scoop 的 persist 目录，升级不丢。',
  instUpdate: '以后升级', instNoScoop: '还没装 Scoop？先运行：',
  instPs1: '从 GitHub Releases 下载最新版，并核对 SHA-256', instPs2: '装到 %LOCALAPPDATA%\\Programs\\codeusagemonit，不需要管理员权限', instPs3: '把 codeusage 加进用户 PATH，创建开始菜单快捷方式', instPs4: '再运行一次就是升级，data 文件夹保留',
  instZipBtn: '下载 codeusagemonit-1.2.0-win-x64.zip', instZip1: '完整解压到一个可写的文件夹，例如 D:\\Tools\\codeusagemonit', instZip2: '运行 codeusagemonit.exe；命令行用同目录的 codeusage.exe', instZip3: '升级时用新版本覆盖旧文件，data 文件夹保留'
});
const en = {
  skip: 'Skip to content', navProviders: 'Providers', navBoard: 'Quotas & usage', navPricing: 'Pricing', navSizes: 'Sizes', navInstall: 'Install', navDownload: 'Download',
  chip: 'Prices now sync daily with the vendors', heroA: 'Every AI quota,', heroB: 'at a glance.',
  heroLead: 'Remaining quota, reset times, token usage and output speed for Codex, Claude, Cursor and 8 more AI coding tools, in one small window in your Windows tray.',
  download: 'Download for Windows', source: 'View source', quickLabel: 'Or install from the terminal', heroMeta: 'v1.2.0 · Windows 10 / 11 · No installer · MIT', quickMore: 'More ways to install ↓',
  stageHint: 'Try it: switch tools, scroll the panel, hover the bars, open a quota',
  eyProviders: 'Providers', pA: '11 coding tools supported,', pB: 'plus your own endpoint', pLead: 'Reuses the sign-in each tool already has: CLI login, editor session or API key. No browser cookies, no stored passwords.',
  eyBoard: 'Quotas & usage', bA: 'What’s left, what it cost, how fast it ran,', bB: 'in one panel',
  bLead: 'Quotas first: each window is a 24-cell meter, lit cells are what’s left, and the line below tells you whether your current pace lasts until the reset. Then usage, read straight from local session logs, numbers only: cost, tokens, requests and output speed by hour or by day.',
  pToday: 'Today', p7: '7 days', p30: '30 days', colQuota: 'Quotas · left / reset / pace', boardHint: 'Click a quota for details · the white line marks an even pace', colUsage: 'Usage · local logs',
  fCost: 'API-equivalent cost', fReq: 'Requests', fSpeed: 'Output speed', demoData: 'Demo data',
  eyPricing: 'Pricing & speed', prA: 'Accurate numbers,', prB: 'official prices',
  prLead: 'Cost = tokens of each request in your local logs × that model’s official API price. The price table syncs with the vendors once a day and falls back to the built-in copy offline; speed is timed end to end per request; calls through relays are attributed to the exact endpoint.',
  priceTitle: 'models, priced in sync with the vendors', priceSub: 'Official API prices, including long-context tiers, cache reads and writes, and fast-mode multipliers.',
  syncOk: 'In sync', syncDate: 'prices as of', syncEvery: 'Checked for updates every 24 hours', priceSearch: 'Search models, e.g. opus, gpt-5.5, glm',
  thModel: 'Model', thIn: 'Input', thOut: 'Output', thCache: 'Cache read', priceUnit: 'USD per 1M tokens · Source: vendors’ official prices, compiled by LiteLLM and models.dev',
  speedTitle: 'Real output speed', speedSub: 'Output tokens ÷ the whole request time, including time to first token and relay latency, measured per tool.',
  relayTitle: 'Relays add up too', relaySub: 'Switch providers with CC Switch as often as you like; every call is still attributed to its endpoint.', relayOfficial: 'Official', relayA: 'Relay A', relayB: 'Relay B',
  eySizes: 'Sizes', sA: 'Four sizes.', sB: 'One window.', sLead: 'Right-click to switch between small, medium, large and full. Compact sizes sit on the desktop layer, out of your way; every size can be resized.',
  sFull: 'Full', sLarge: 'Large', sMedium: 'Medium', sSmall: 'Small', deckHint: 'Click a card to take a closer look',
  eyInstall: 'Install & CLI', cA: 'One command to install,', cB: 'the same numbers in your terminal',
  cLead: 'Install with Scoop or a PowerShell one-liner; it adds itself to PATH and the Start menu. The bundled codeusage command shares the app’s cache and settings, meters are 24 cells here too, and --json feeds scripts or a status bar.',
  recommended: 'Recommended', instZip: 'Zip', cmdStatus: 'Quotas and usage from the cache, offline', cmdCost: 'Daily cost and tokens, plus each tool’s speed', cmdThird: 'Usage and speed of third-party endpoints', copy: 'Copy command', copyShort: 'Copy',
  eyPrivacy: 'Privacy', vA: 'Your data stays', vB: 'on your PC',
  v1t: 'Numbers only', v1: 'Only tokens, request counts, durations and model names go into a local hourly index. No conversation content.',
  v2t: 'Sign-ins stay put', v2: 'Each tool’s login tokens stay in its own folder. They are read, never copied.',
  v3t: 'Encrypted keys', v3: 'API keys you enter are encrypted with Windows DPAPI; only your Windows user can decrypt them.',
  v4t: 'No telemetry', v4: 'No server of our own. Quota checks go only to each provider; the price table is fetched from GitHub once a day with no account or usage data, and can be turned off in settings.',
  eyFaq: 'FAQ', fqA: 'Questions',
  fq1: 'How do I install it? What does it need?', fa1: 'Pick one: Scoop, the PowerShell one-liner, or the zip — extract it into a writable folder (e.g. D:\\Tools) and run codeusagemonit.exe. No installer, Node.js or admin rights; the .NET Framework 4.8 that ships with Windows 10 / 11 is enough.',
  fq7: 'How do I update or uninstall?', fa7: 'Scoop: scoop update codeusagemonit / scoop uninstall codeusagemonit; data lives in Scoop’s persist folder and survives updates. PowerShell script: run it again to update; to uninstall, delete %LOCALAPPDATA%\\Programs\\codeusagemonit and remove it from your user PATH. Zip: overwrite the old files and keep the data folder.',
  fq2: 'Why does Windows say it “protected your PC”?', fa2: 'The app has no paid code-signing certificate. Click “More info → Run anyway”, or check the download against the SHA-256 below first. Scoop and the PowerShell script check the SHA-256 for you.',
  fq3: 'Do I have to sign in to every account again?', fa3: 'No. Codex, Claude Code, Cursor, Antigravity and Grok reuse the sign-in already on your PC; DeepSeek, Kimi, OpenCode and ZCode take an API key; Copilot uses GitHub device login.',
  fq4: 'Is the cost a real bill?', fa4: 'No. Cost = tokens in your local logs × official API prices, an API-equivalent reference. Subscriptions are billed monthly and don’t produce this bill.',
  fq8: 'How does the price table stay in sync with official prices?', fa8: 'The repository’s pricing.json is rebuilt every day by GitHub Actions from LiteLLM and models.dev, which track the API prices each vendor publishes. The app checks every 24 hours, downloads and validates a newer table and recalculates with it; if the download fails or you turn it off in settings, it keeps the built-in prices.',
  fq5: 'How is output speed measured?', fa5: 'Output tokens (incl. thinking) ÷ time from sending the request to its last output, so time to first token and network or relay latency are included. Only requests with at least 50 output tokens count, and each tool is measured on its own.',
  fq6: 'What language is the app? Is there a macOS version?', fa6: 'The app’s interface is in Simplified Chinese and it runs on Windows only. On macOS, try CodexBar, which inspired this project’s interface.',
  dlTitle: 'Put it in your tray', dlAll: 'All releases', feedback: 'Feedback', notices: 'Notices',
  footerNote: 'An independent open-source project, not affiliated with OpenAI, Anthropic, Cursor or any other provider. Names and logos belong to their owners.',
  copied: 'Copied', copyFailed: 'Copy failed — select it manually', copyLabel: 'Copy',
  tipCost: 'Cost', tipTokens: 'Tokens', tipReq: 'Requests', tipSpeed: 'Output speed',
  noteDaily: 'Daily', noteHourly: 'Hourly', noteSpeed: 'Speed is token-weighted: Σ output ÷ Σ time',
  pConnect: 'Connects with', pQuota: 'Quotas shown', capQuota: 'Quotas', capLocal: 'Local usage', capSpeed: 'Output speed', capBeta: ' (experimental)', docs: 'How to connect →', custom: 'Custom', customLong: 'Custom provider',
  vSmall: 'One big number and one meter, the footprint of an icon. Click the number to cycle quota windows; scroll or ←/→ to page.',
  vMedium: 'The main quota on the left, other windows on the right; a single window adds pace, reset time and reset credits. Medium shows quotas only, no charts.',
  vLarge: 'Full quota windows with pace, plus local usage: period, cost, tokens, requests, speed and a clickable chart.',
  vFull: 'Cards for every provider, a detail page for each, the third-party endpoints page and settings. Drag the edges to any size.',
  vFullDim: 'Default 420 × 790, resizable',
  pgTop: 'Top', pgProviders: 'Providers', pgBoard: 'Quotas & usage', pgPricing: 'Pricing', pgSizes: 'Sizes', pgInstall: 'Install', pgPrivacy: 'Privacy', pgFaq: 'FAQ', pgDownload: 'Download',
  themeLight: 'Light', themeDark: 'Dark', themeSystem: 'Match system',
  vendorAll: 'All', priceShown: '{n} of {total} models', priceNone: 'No matching models', tagLong: 'long ctx >{k}K', defaultCache: 'Not listed; estimated at 10% of the input price',
  instScoopNote: 'Like a Homebrew tap: add this repository as a bucket, then install. You get a Start menu shortcut and codeusage on PATH; the data folder lives in Scoop’s persist directory, so updates keep it.',
  instUpdate: 'Update later', instNoScoop: 'No Scoop yet? Install it first:',
  instPs1: 'Downloads the latest release from GitHub and checks its SHA-256', instPs2: 'Installs to %LOCALAPPDATA%\\Programs\\codeusagemonit, no admin rights', instPs3: 'Adds codeusage to your user PATH and a Start menu shortcut', instPs4: 'Run it again to update; the data folder is kept',
  instZipBtn: 'Download codeusagemonit-1.2.0-win-x64.zip', instZip1: 'Extract everything into a writable folder, e.g. D:\\Tools\\codeusagemonit', instZip2: 'Run codeusagemonit.exe; use codeusage.exe in the same folder for the CLI', instZip3: 'To update, overwrite with the new version and keep the data folder'
};
const ja = {
  skip: '本文へ移動', navProviders: 'サービス', navBoard: '利用枠と使用量', navPricing: '料金', navSizes: 'サイズ', navInstall: 'インストール', navDownload: 'ダウンロード',
  chip: '料金表が毎日公式と同期', heroA: '残りの利用枠が', heroB: 'ひと目でわかる',
  heroLead: 'Codex、Claude、Cursor など 11 の AI コーディングツールの残り利用枠、リセット時刻、トークン使用量、出力速度を、Windows のトレイにある小さなウィンドウひとつで。',
  download: 'Windows 版をダウンロード', source: 'ソースを見る', quickLabel: 'ターミナルから 1 行でインストール', heroMeta: 'v1.2.0 · Windows 10 / 11 · インストーラー不要 · MIT', quickMore: 'ほかのインストール方法 ↓',
  stageHint: '触ってみてください：ツールの切り替え、パネルのスクロール、グラフのホバー、利用枠の展開',
  eyProviders: 'サービス', pA: '11 のコーディングツールに対応', pB: 'あなた自身の API も', pLead: '各ツールの既存のログインを利用：CLI、エディターのセッション、API キー。ブラウザーの Cookie は読まず、パスワードも保存しません。',
  eyBoard: '利用枠と使用量', bA: '残り、費用、速度を', bB: 'ひとつのパネルで',
  bLead: 'まず利用枠。枠ごとに 24 マスのメーターで、点灯しているのが残り。その下の行で、今のペースでリセットまで持つかがわかります。次に使用量。PC 内のセッションログを直接読み、数値だけを記録。費用・トークン・リクエスト・出力速度を時間別・日別に確認できます。',
  pToday: '今日', p7: '7 日間', p30: '30 日間', colQuota: '利用枠 · 残り / リセット / ペース', boardHint: '利用枠をクリックで詳細 · 白い線は均等に使った場合の位置', colUsage: '使用量 · ローカルログ',
  fCost: 'API 換算の費用', fReq: 'リクエスト', fSpeed: '出力速度', demoData: 'デモデータ',
  eyPricing: '料金と速度', prA: '正確な計算', prB: '料金は公式に合わせて',
  prLead: '費用 = ローカルログにある各リクエストのトークン数 × そのモデルの公式 API 単価。料金表は 1 日 1 回公式価格と同期し、オフライン時は内蔵の料金表を使います。速度はリクエストごとにエンドツーエンドで計測し、中継サービス経由の呼び出しも接続先ごとに集計します。',
  priceTitle: 'モデル、公式と同期した料金で計算', priceSub: '公式 API 単価。長コンテキストの段階料金、キャッシュの読み書き、Fast モードの倍率にも対応。',
  syncOk: '同期済み', syncDate: '料金日付', syncEvery: '24 時間ごとに更新を確認', priceSearch: 'モデルを検索（例：opus、gpt-5.5、glm）',
  thModel: 'モデル', thIn: '入力', thOut: '出力', thCache: 'キャッシュ読取', priceUnit: '米ドル / 100 万トークン · 出典：各社の公式料金（LiteLLM・models.dev による集計）',
  speedTitle: '実際の出力速度', speedSub: '出力トークン ÷ リクエスト全体の所要時間。最初のトークンまでの待ち時間や中継の遅延も含み、ツールごとに計算します。',
  relayTitle: '中継サービスも正確に', relaySub: 'CC Switch で接続先を切り替えても、呼び出しは接続先ごとに集計されます。', relayOfficial: '公式', relayA: '中継 A', relayB: '中継 B',
  eySizes: 'サイズ', sA: '4 つのサイズ', sB: 'ひとつのウィンドウ', sLead: '右クリックで小・中・大・全体を切り替え。コンパクトなサイズはデスクトップ層に置かれ、作業の邪魔をしません。どのサイズもサイズ変更できます。',
  sFull: '全体', sLarge: '大', sMedium: '中', sSmall: '小', deckHint: 'カードをクリックして拡大表示',
  eyInstall: 'インストールと CLI', cA: '1 行でインストール', cB: 'ターミナルでも同じデータを',
  cLead: 'Scoop か PowerShell の 1 行でインストールでき、PATH とスタートメニューに自動で登録されます。同梱の codeusage コマンドはアプリとキャッシュ・設定を共有。メーターはここでも 24 マスで、--json を付ければスクリプトやステータスバーに渡せます。',
  recommended: 'おすすめ', instZip: 'zip', cmdStatus: 'キャッシュの利用枠と使用量（オフライン）', cmdCost: '日別の費用とトークン、ツールごとの速度', cmdThird: 'サードパーティ API の使用量と速度', copy: 'コマンドをコピー', copyShort: 'コピー',
  eyPrivacy: 'プライバシー', vA: 'データは', vB: 'あなたの PC に',
  v1t: '数値だけ', v1: 'トークン数、リクエスト数、所要時間、モデル名だけをローカルの時間別インデックスに記録。会話内容は保存しません。',
  v2t: 'ログインはそのまま', v2: '各ツールのログイントークンは元のフォルダーに残ります。読み取るだけで、コピーしません。',
  v3t: 'キーは暗号化', v3: '入力した API キーは Windows DPAPI で暗号化。復号できるのは現在の Windows ユーザーだけです。',
  v4t: 'テレメトリーなし', v4: '独自サーバーはありません。利用枠の照会は各サービスにだけ送信。料金表は 1 日 1 回 GitHub から取得し、アカウントや使用量の情報は送りません。設定でオフにできます。',
  eyFaq: 'FAQ', fqA: 'よくある質問',
  fq1: 'インストール方法と必要な環境は？', fa1: '方法は 3 つ：Scoop、PowerShell の 1 行スクリプト、または zip を書き込み可能なフォルダー（例：D:\\Tools）に展開して codeusagemonit.exe を実行。インストーラー、Node.js、管理者権限は不要。Windows 10 / 11 標準の .NET Framework 4.8 で動きます。',
  fq7: '更新とアンインストールは？', fa7: 'Scoop：scoop update codeusagemonit / scoop uninstall codeusagemonit。データは Scoop の persist フォルダーにあり、更新しても残ります。PowerShell スクリプト：もう一度実行すれば更新。アンインストールは %LOCALAPPDATA%\\Programs\\codeusagemonit を削除し、ユーザー PATH から外します。zip：新しいファイルで上書きし、data フォルダーは残してください。',
  fq2: '「Windows によって PC が保護されました」と表示されるのは？', fa2: 'コード署名証明書を購入していないためです。「詳細情報 → 実行」を選んでください。下の SHA-256 でファイルを確認することもできます。Scoop と PowerShell スクリプトは SHA-256 を自動で確認します。',
  fq3: 'すべてのアカウントに再ログインが必要ですか？', fa3: 'いいえ。Codex、Claude Code、Cursor、Antigravity、Grok は PC 上の既存のログインを利用します。DeepSeek、Kimi、OpenCode、ZCode は API キー、Copilot は GitHub のデバイスログインです。',
  fq4: '表示される費用は実際の請求額ですか？', fa4: 'いいえ。ローカルログのトークン数 × 公式 API 価格で計算した API 換算の参考値です。サブスクリプションは月額課金なので、この金額は請求されません。',
  fq8: '料金表はどうやって公式と同期していますか？', fa8: 'リポジトリの pricing.json は、GitHub Actions が毎日 LiteLLM と models.dev（各社が公開する API 単価を収録）から作り直しています。アプリは 24 時間ごとに確認し、新しい料金表があればダウンロード・検証して再計算します。取得に失敗した場合や設定でオフにした場合は、内蔵の料金表を使い続けます。',
  fq5: '出力速度はどう計算していますか？', fa5: '出力トークン（思考を含む）÷ リクエスト送信から最後の出力までの時間。最初のトークンまでの待ち時間やネットワーク・中継サービスの遅延も含みます。出力 50 トークン以上のリクエストのみを対象に、ツールごとに計算します。',
  fq6: 'アプリの言語は？ macOS 版はありますか？', fa6: 'アプリの画面は簡体字中国語で、Windows 専用です。macOS では、本プロジェクトの画面設計の参考にした CodexBar をお試しください。',
  dlTitle: 'トレイに置いておこう', dlAll: 'すべてのリリース', feedback: 'フィードバック', notices: 'サードパーティ表記',
  footerNote: '独立したオープンソースプロジェクトであり、OpenAI、Anthropic、Cursor などのサービスとは関係ありません。名称とロゴは各所有者に帰属します。',
  copied: 'コピーしました', copyFailed: 'コピーできませんでした。手動で選択してください', copyLabel: 'コピー',
  tipCost: '費用', tipTokens: 'トークン', tipReq: 'リクエスト', tipSpeed: '出力速度',
  noteDaily: '日別', noteHourly: '時間別', noteSpeed: '速度はトークン加重：Σ出力 ÷ Σ所要時間',
  pConnect: '接続方法', pQuota: '表示する利用枠', capQuota: '利用枠', capLocal: 'ローカル使用量', capSpeed: '出力速度', capBeta: '（試験的）', docs: '接続方法 →', custom: 'カスタム', customLong: 'カスタムサービス',
  vSmall: '大きな数字と 1 本のメーター。アイコンひとつ分の広さです。数字をクリックで利用枠を切り替え、ホイールか ←/→ でページ送り。',
  vMedium: '左にメインの利用枠、右にほかの枠。枠が 1 つだけならペース、リセット時刻、リセットクレジットを表示。中サイズは利用枠だけで、グラフはありません。',
  vLarge: '利用枠とペースに加え、ローカル使用量：期間、費用、トークン、リクエスト、速度、クリックできるグラフ。',
  vFull: '全サービスのカード、サービスごとの詳細、サードパーティ API のページ、設定。端をドラッグして自由にサイズ変更できます。',
  vFullDim: '既定 420 × 790、サイズ変更可',
  pgTop: 'トップ', pgProviders: 'サービス', pgBoard: '利用枠と使用量', pgPricing: '料金', pgSizes: 'サイズ', pgInstall: 'インストール', pgPrivacy: 'プライバシー', pgFaq: 'FAQ', pgDownload: 'ダウンロード',
  themeLight: 'ライト', themeDark: 'ダーク', themeSystem: 'システムに合わせる',
  vendorAll: 'すべて', priceShown: '{n} / {total} 件', priceNone: '一致するモデルはありません', tagLong: '長コンテキスト >{k}K', defaultCache: '未掲載のため入力単価の 10% で推定',
  instScoopNote: 'Homebrew の tap と同じく、まずこのリポジトリを bucket として追加してからインストール。スタートメニューのショートカットと PATH 上の codeusage が作られ、data フォルダーは Scoop の persist ディレクトリに置かれるので更新しても残ります。',
  instUpdate: '今後の更新', instNoScoop: 'Scoop が未導入なら先に：',
  instPs1: 'GitHub Releases から最新版をダウンロードし、SHA-256 を確認', instPs2: '%LOCALAPPDATA%\\Programs\\codeusagemonit にインストール（管理者権限不要）', instPs3: 'codeusage をユーザー PATH に追加し、スタートメニューにショートカットを作成', instPs4: 'もう一度実行すれば更新。data フォルダーは保持',
  instZipBtn: 'codeusagemonit-1.2.0-win-x64.zip をダウンロード', instZip1: '書き込み可能なフォルダー（例：D:\\Tools\\codeusagemonit）にすべて展開', instZip2: 'codeusagemonit.exe を実行。CLI は同じフォルダーの codeusage.exe', instZip3: '更新は新しいバージョンで上書きし、data フォルダーは残す'
};
const DICT = { zh, en, ja };
let lang = 'zh';
const t = key => DICT[lang][key] ?? zh[key] ?? key;
const LI = () => ({ zh: 0, en: 1, ja: 2 }[lang]);

// Provider facts for the providers panel (zh / en / ja).
const PROV_INFO = {
  codex: { kind: ['复用登录', 'Existing sign-in', '既存のログイン'], connect: ['复用 Codex CLI 或 Codex 应用的登录', 'Reuses the Codex CLI or app sign-in', 'Codex CLI / アプリのログインを利用'], quota: ['账户返回的周期额度（如 5 小时 / 每周）、限额重置额度', 'Quota windows the account reports (e.g. 5-hour / weekly), reset credits', 'アカウントが返す利用枠（5 時間・週など）とリセットクレジット'], caps: [1, 1, 1] },
  claude: { kind: ['复用登录', 'Existing sign-in', '既存のログイン'], connect: ['复用 Claude Code 的登录', 'Reuses the Claude Code sign-in', 'Claude Code のログインを利用'], quota: ['5 小时、每周及模型额度', '5-hour, weekly and per-model quotas', '5 時間・週・モデル別の利用枠'], caps: [1, 1, 1] },
  cursor: { kind: ['编辑器会话', 'Editor session', 'エディター'], connect: ['只读 Cursor 编辑器保存的会话', 'Reads the Cursor editor session (read-only)', 'Cursor エディターのセッションを読み取り（読み取りのみ）'], quota: ['套餐总量、Auto 与 API 用量', 'Plan total, Auto and API usage', 'プラン合計・Auto・API の使用量'], caps: [1, 0, 0] },
  antigravity: { kind: ['桌面应用', 'Desktop app', 'デスクトップ'], connect: ['读取正在运行的桌面应用，登录凭据作为回退', 'Talks to the running desktop app; saved sign-in as fallback', '起動中のデスクトップアプリから取得（保存済みログインを予備に使用）'], quota: ['周期与模型额度', 'Period and per-model quotas', '期間・モデル別の利用枠'], caps: [1, 1, 0] },
  deepseek: { kind: ['API Key', 'API key', 'API キー'], connect: ['API Key（或环境变量 DEEPSEEK_API_KEY）', 'API key (or DEEPSEEK_API_KEY)', 'API キー（または DEEPSEEK_API_KEY）'], quota: ['API 账户余额，按币种分别显示', 'API account balance per currency', '通貨別の API アカウント残高'], caps: [1, 1, 1] },
  grok: { kind: ['复用登录', 'Existing sign-in', '既存のログイン'], connect: ['复用 Grok Build CLI 的登录（grok login）', 'Reuses the Grok Build CLI sign-in (grok login)', 'Grok Build CLI のログインを利用（grok login）'], quota: ['当前账期的订阅额度', 'Subscription allowance for the billing period', '請求期間のサブスクリプション枠'], caps: [1, 1, 1] },
  copilot: { kind: ['设备码', 'Device login', 'デバイス認証'], connect: ['GitHub 设备码登录，或复用官方插件已保存的授权', 'GitHub device login, or an official client’s saved authorisation', 'GitHub デバイスログイン、または公式クライアントの保存済み認証'], quota: ['每月高级请求与对话额度', 'Monthly premium requests and chat', '月間のプレミアムリクエストとチャット枠'], caps: [1, 2, 0] },
  kimi: { kind: ['API Key', 'API key', 'API キー'], connect: ['Kimi Code API Key，可选国内或国际', 'Kimi Code API key, China or international', 'Kimi Code の API キー（中国版 / 国際版）'], quota: ['5 小时、每周、每月', '5-hour, weekly, monthly', '5 時間・週・月'], caps: [1, 2, 2] },
  opencode: { kind: ['API Key', 'API key', 'API キー'], connect: ['OpenCode Go 的 API Key', 'OpenCode Go API key', 'OpenCode Go の API キー'], quota: ['5 小时、每周、每月', '5-hour, weekly, monthly', '5 時間・週・月'], caps: [1, 2, 2] },
  zcode: { kind: ['API Key', 'API key', 'API キー'], connect: ['智谱 / Z.ai API Key（GLM 编码套餐）', 'Zhipu / Z.ai API key (GLM Coding Plan)', 'Zhipu / Z.ai の API キー（GLM Coding Plan）'], quota: ['5 小时、每周、MCP 工具调用', '5-hour, weekly, MCP tool calls', '5 時間・週・MCP ツール呼び出し'], caps: [1, 1, 1] },
  pi: { kind: ['本机日志', 'Local logs', 'ローカルログ'], connect: ['无需登录', 'No sign-in needed', 'ログイン不要'], quota: ['没有账户额度，只统计本机用量', 'No account quota; local usage only', 'アカウントの利用枠はなく、ローカル使用量のみ'], caps: [0, 1, 1] },
  custom: { kind: ['JSON 接口', 'JSON endpoint', 'JSON API'], connect: ['任意返回 JSON 的 GET 接口，密钥 DPAPI 加密', 'Any GET endpoint returning JSON; key encrypted with DPAPI', 'JSON を返す任意の GET API（キーは DPAPI で暗号化）'], quota: ['最多 6 个额度窗口，或余额', 'Up to 6 quota windows, or a balance', '最大 6 つの利用枠、または残高'], caps: [1, 0, 0] }
};

// ── Demo data (consistent with the app's --demo mode) ─────────────────
function prng(seed) { return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let x = Math.imul(seed ^ seed >>> 15, 1 | seed); x = x + Math.imul(x ^ x >>> 7, 61 | x) ^ x; return ((x ^ x >>> 14) >>> 0) / 4294967296; }; }
const NOW = new Date(); NOW.setMinutes(0, 0, 0);
const HOUR = NOW.getHours();
// Every tool has a speed here: the demo shows what the app shows once requests carry timings.
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
  // Today by hour: a working-day shape up to the current hour.
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
const speedOf = (arr, id) => { const sec = sum(arr, 'sec'); return sec >= 1 ? sum(arr, 'out') / sec : TOOLS[id].speed; };

// Quota windows, as the app shows them. pace: headroom (+) or overspend (−) in points.
const QUOTAS = {
  codex: { plan: 'Pro 20x', windows: [{ label: '5 小时', rem: 71, reset: '2小时 14分', len: '5 小时', pace: 26 }, { label: '每周', rem: 62, reset: '3天 4小时', len: '7 天', pace: 16 }], credits: 2 },
  claude: { plan: '演示账户', windows: [{ label: '5 小时', rem: 36, reset: '1小时 29分', len: '5 小时', pace: 6 }, { label: '每周', rem: 97, reset: '3天 4小时', len: '7 天', pace: 51 }] },
  cursor: { plan: 'Pro', windows: [{ label: '总量', rem: 4, reset: '12天 3小时', len: '30 天', pace: -12, empty: '约 2天 6小时后用尽' }, { label: 'Auto', rem: 58, reset: '12天 3小时', len: '30 天', pace: 18 }, { label: 'API', rem: 81, reset: '12天 3小时', len: '30 天', pace: 39 }] },
  antigravity: { plan: 'Pro', windows: [{ label: 'Gemini 3 Pro', rem: 80, reset: '2小时 10分', len: '5 小时', pace: 23 }, { label: 'Claude', rem: 64, reset: '2小时 10分', len: '5 小时', pace: 7 }] },
  deepseek: { balance: 'USD 8.62' },
  grok: { setup: true },
  copilot: { plan: 'Pro', windows: [{ label: '高级请求', rem: 58, reset: '18天 2小时', len: '每月', pace: 18 }, { label: '对话', rem: 100, reset: '18天 2小时', len: '每月', pace: 40 }] },
  kimi: { plan: 'Moderato', windows: [{ label: '5 小时', rem: 45, reset: '3小时 2分', len: '5 小时', pace: -6, empty: '约 1小时 50分后用尽' }, { label: '每周', rem: 76, reset: '4天 9小时', len: '7 天', pace: 14 }] },
  opencode: { plan: 'Go', windows: [{ label: '5 小时', rem: 88, reset: '4小时 1分', len: '5 小时', pace: 8 }, { label: '每周', rem: 91, reset: '5天 1小时', len: '7 天', pace: 20 }] },
  zcode: { plan: 'GLM Pro', windows: [{ label: '5 小时', rem: 69, reset: '2小时 44分', len: '5 小时', pace: 14 }, { label: '每周', rem: 83, reset: '4天 20小时', len: '7 天', pace: 12 }, { label: 'MCP 调用', rem: 90, reset: '10天 23小时', len: '30 天', pace: 53 }] },
  pi: { local: true }
};
const meter = (rem, id, cls = '', mark = null) => {
  const on = Math.round(Math.max(0, Math.min(100, rem)) / 100 * 24); let s = '';
  for (let i = 0; i < 24; i++) s += `<i class="${i < on ? 'on' : ''}"></i>`;
  if (mark != null) s += `<span class="mark" style="left:${mark}%" title="匀速用到重置时应剩的位置"></span>`;
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
    lit.forEach((on, i) => { if (on) setTimeout(() => cells[i].classList.add('on'), 60 + i * 22); });
  });
}

// ── Hero: the full panel, flat and clickable ──────────────────────────
const stage = $('stage'), body = $('app-body'), tabs = $('app-tabs');
let appPage = 'overview', appTip = null;
const OVERVIEW_ICON = '<svg class="pi" viewBox="0 0 18 18" style="background:none;-webkit-mask:none;mask:none"><rect x="2" y="2" width="14" height="14" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M2 7h14M2 11.5h14M7 2v14M11.5 2v14" stroke="currentColor" stroke-width="1.1"/></svg>';
function renderTabs() {
  tabs.innerHTML = ['overview', ...IDS].map(id => `<button type="button" class="app-tab" role="tab" data-page="${id}" aria-selected="${id === appPage}">${id === 'overview' ? OVERVIEW_ICON : icon(id)}<span>${id === 'overview' ? '概览' : P[id].name}</span></button>`).join('');
}
const dayLabel = d => `${d.getMonth() + 1}/${d.getDate()}`;
const miniAxis = days => `<div class="mini-axis">${[0, 5, 10, 15, 20, 25].map(i => `<span>${dayLabel(days[i].date)}</span>`).join('')}<span>今天</span></div>`;
function overviewChart() {
  const bars = SERIES.codex.days.map((d, i) => USAGE_IDS.map(id => ({ id, cost: SERIES[id].days[i].cost })));
  const max = Math.max(...bars.map(b => b.reduce((s, x) => s + x.cost, 0)));
  return `<div class="mini-chart" data-chart="overview">${bars.map((b, i) => { const tot = b.reduce((s, x) => s + x.cost, 0); return `<div class="b" data-i="${i}" style="height:${Math.max(1.5, tot / max * 100)}%">${b.filter(x => x.cost > 0).map(x => `<i style="height:${x.cost / tot * 100}%;background:${cv(x.id)}"></i>`).join('')}</div>`; }).join('')}</div>`;
}
function quotaBlock(id, compactMode) {
  const q = QUOTAS[id];
  if (q.setup) return `<div class="q-line"><p class="dim" style="margin:10px 0 0">尚未连接 · 在终端运行 grok login</p><span class="a-chip" style="margin-top:10px">在终端登录</span></div>`;
  if (q.balance) return `<div class="q-line"><div class="row"><span class="dim">可用余额</span><b class="num">${q.balance}</b></div></div>`;
  if (q.local) return `<div class="q-line"><p class="dim" style="margin:10px 0 0">没有账户额度 · 只统计本机日志</p></div>`;
  const list = compactMode ? q.windows.slice(0, 2) : q.windows;
  return list.map(w => `<div class="q-line"><div class="row"><span>${w.label} <b class="num">${w.rem}%</b> 剩余</span><span class="faint">${w.reset}后重置</span></div>${meter(w.rem, id)}<div class="pace">${paceText(w)}</div></div>`).join('')
    + (q.credits ? `<div class="row" style="margin-top:12px"><span>限额重置额度</span><b>${q.credits} 次可用</b></div>` : '');
}
function providerCard(id) {
  const q = QUOTAS[id], s = SERIES[id];
  const foot = s ? `<div class="row faint card-foot"><span class="num">今日 ${usd(s.days[29].cost)} · 30 天 ${usd(TOOLS[id].cost)}</span><span class="num">${compact(TOOLS[id].tokens)} Token · ${tps(speedOf(s.days, id))}</span></div>` : '';
  return `<div class="app-card"><div class="card-head">${icon(id)}<b>${P[id].name}</b>${q.plan ? `<span class="plan">${q.plan}</span>` : ''}<span class="faint" style="margin-left:auto" data-fresh>刚刚更新</span></div>${quotaBlock(id, true)}${foot}</div>`;
}
function renderPage(id) {
  let html;
  if (id === 'overview') {
    const tot = USAGE_IDS.reduce((s, k) => s + TOOLS[k].cost, 0), tok = USAGE_IDS.reduce((s, k) => s + TOOLS[k].tokens, 0), today = USAGE_IDS.reduce((s, k) => s + TOOLS[k].today, 0);
    html = `<div class="app-card"><div class="row"><span class="dim">全部平台 · API 等价费用</span><span class="a-chip">近 30 天</span></div>
      <div class="row" style="margin-top:6px"><span class="big">${usd(tot)}</span><span class="num">${compact(tok)} Token</span></div>
      <div class="faint">今日 ${usd(today)} · ${USAGE_IDS.length} 个平台有记录</div>
      ${overviewChart()}${miniAxis(SERIES.codex.days)}
      <div class="legend">${USAGE_IDS.map(k => `<span><i style="background:${cv(k)}"></i>${P[k].name} ${usd(TOOLS[k].cost)}</span>`).join('')}</div></div>
      ${['codex', 'claude', 'cursor', 'kimi', 'zcode', 'antigravity', 'copilot', 'opencode', 'deepseek', 'grok', 'pi'].map(providerCard).join('')}`;
  } else {
    const q = QUOTAS[id], s = SERIES[id];
    let usage = '';
    if (s) {
      const max = Math.max(...s.days.map(d => d.cost));
      usage = `<div class="app-card"><div class="row"><b style="font-size:13px">本机用量</b><span class="a-chip">近 30 天</span></div>
        <div class="row" style="margin-top:10px;align-items:flex-start">${[['费用', usd(TOOLS[id].cost)], ['Token', compact(TOOLS[id].tokens)], ['请求', sum(s.days, 'req').toLocaleString('en-US')], ['速度', tps(speedOf(s.days, id))]].map(([k, v]) => `<div><div class="faint">${k}</div><div class="num" style="font-size:15px;font-weight:600;margin-top:2px">${v}</div></div>`).join('')}</div>
        <div class="mini-chart" data-chart="${id}">${s.days.map((d, i) => `<div class="b" data-i="${i}" style="height:${Math.max(1.5, d.cost / max * 100)}%"><i style="height:100%;background:${cv(id)}"></i></div>`).join('')}</div>${miniAxis(s.days)}</div>`;
    }
    html = `<div class="app-card"><div class="card-head">${icon(id)}<b>${P[id].name}</b>${q.plan ? `<span class="plan">${q.plan}</span>` : ''}<span class="faint" style="margin-left:auto" data-fresh>刚刚更新</span></div>${quotaBlock(id, false)}</div>${usage}`;
  }
  const page = document.createElement('div'); page.className = 'app-page'; page.innerHTML = html; appTip = null;
  body.replaceChildren(page); body.scrollTop = 0;
  animateMeters(page);
}
function selectPage(id, user) { appPage = id; renderTabs(); renderPage(id); if (user) stopTour(); }
tabs.addEventListener('click', e => { const b = e.target.closest('[data-page]'); if (b) selectPage(b.dataset.page, true); });
// Chart hover tooltip inside the replica (positioned in the scrolling body's content box).
body.addEventListener('pointerover', e => {
  const b = e.target.closest('.mini-chart .b'); if (!b) return;
  const chart = b.parentElement.dataset.chart, i = +b.dataset.i;
  let text;
  if (chart === 'overview') { const parts = USAGE_IDS.map(k => [k, SERIES[k].days[i].cost]).filter(x => x[1] > 0); text = `<b>${dayLabel(SERIES.codex.days[i].date)}</b> · ${usd(parts.reduce((s, x) => s + x[1], 0))}` + parts.map(([k, v]) => `<br>${P[k].name}  ${usd(v)}`).join(''); }
  else { const d = SERIES[chart].days[i]; text = `<b>${dayLabel(d.date)}</b><br>${usd(d.cost)} · ${compact(d.tokens)} Token${d.speed ? '<br>输出速度 ' + tps(d.speed) : ''}`; }
  if (!appTip) { appTip = document.createElement('div'); appTip.className = 'app-tip'; body.appendChild(appTip); }
  appTip.innerHTML = text;
  const br = b.getBoundingClientRect(), pr = body.getBoundingClientRect();
  const x = br.left - pr.left + br.width / 2, y = br.top - pr.top + body.scrollTop - 8;
  appTip.style.left = Math.min(body.clientWidth - appTip.offsetWidth - 6, Math.max(6, x - appTip.offsetWidth / 2)) + 'px';
  appTip.style.top = Math.max(body.scrollTop + 4, y - appTip.offsetHeight) + 'px';
});
body.addEventListener('pointerout', e => { if (e.target.closest('.mini-chart .b') && appTip && !e.relatedTarget?.closest?.('.mini-chart .b')) { appTip.remove(); appTip = null; } });
body.addEventListener('click', e => { const b = e.target.closest('.mini-chart .b'); if (!b) return; stopTour(); const was = b.classList.contains('on'); b.parentElement.querySelectorAll('.b.on').forEach(x => x.classList.remove('on')); if (!was) b.classList.add('on'); });
['wheel', 'touchstart', 'keydown'].forEach(type => body.addEventListener(type, () => stopTour(), { passive: true }));
$('app-refresh').addEventListener('click', e => {
  const btn = e.currentTarget; stopTour(); btn.classList.remove('spin'); void btn.offsetWidth; btn.classList.add('spin');
  body.querySelectorAll('[data-fresh]').forEach(el => { el.textContent = '正在刷新…'; });
  setTimeout(() => { renderPage(appPage); }, 700);
});
// Auto tour until the visitor touches the demo.
const TOUR = ['overview', 'codex', 'claude', 'cursor', 'zcode'];
let tourTimer = null, tourStep = 0;
function startTour() { if (reduced || tourTimer) return; tourTimer = setInterval(() => { if (document.hidden) return; tourStep = (tourStep + 1) % TOUR.length; selectPage(TOUR[tourStep], false); }, 4800); }
function stopTour() { clearInterval(tourTimer); tourTimer = null; stage.classList.add('touched'); }
new IntersectionObserver(([entry]) => { if (entry.isIntersecting && !stage.classList.contains('touched')) startTour(); else if (!entry.isIntersecting) { clearInterval(tourTimer); tourTimer = null; } }).observe(stage);

// ── Copy helpers ──────────────────────────────────────────────────────
const COPY_ICON = '<svg viewBox="0 0 16 16" aria-hidden="true"><rect x="5" y="5" width="8.5" height="8.5" rx="2" fill="none" stroke="currentColor" stroke-width="1.4"/><path d="M11 3.2V3a1.5 1.5 0 0 0-1.5-1.5H4A1.5 1.5 0 0 0 2.5 3v5.5A1.5 1.5 0 0 0 4 10h.2" fill="none" stroke="currentColor" stroke-width="1.4"/></svg>';
const OK_ICON = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="m3.5 8.5 3 3 6-7" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/></svg>';
async function writeClip(text) { try { await navigator.clipboard.writeText(text); return true; } catch { return false; } }
async function copyText(text, label, restore) { label.textContent = (await writeClip(text)) ? t('copied') : t('copyFailed'); setTimeout(() => { label.textContent = restore(); }, 1600); }
async function copyIcon(btn, text) { const ok = await writeClip(text); btn.innerHTML = ok ? OK_ICON : COPY_ICON; btn.classList.toggle('ok', ok); btn.title = ok ? t('copied') : t('copyFailed'); setTimeout(() => { btn.innerHTML = COPY_ICON; btn.classList.remove('ok'); btn.title = t('copyLabel'); }, 1600); }
const cmdRow = text => `<div class="cmd"><code><span class="pr">&gt; </span>${esc(text)}</code><button type="button" class="copy-btn" data-copy="${esc(text)}" aria-label="${t('copyLabel')}" title="${t('copyLabel')}">${COPY_ICON}</button></div>`;
document.addEventListener('click', e => { const b = e.target.closest('[data-copy]'); if (b) copyIcon(b, b.dataset.copy); });

// ── Install (hero one-liner + the install panel) ──────────────────────
const REPO = 'https://github.com/fanchengliu/codeusagemonit';
const INSTALL = {
  scoop: ['scoop bucket add codeusagemonit ' + REPO, 'scoop install codeusagemonit'],
  ps: ['irm https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/install.ps1 | iex']
};
let quickKind = 'scoop';
function renderQuick() {
  $('quick-code').innerHTML = INSTALL[quickKind].map(l => `<span class="pr">&gt; </span>${esc(l)}`).join('\n');
  document.querySelectorAll('[data-quick]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.quick === quickKind)));
  const btn = $('quick-copy'); btn.innerHTML = COPY_ICON; btn.dataset.copy = INSTALL[quickKind].join('\n'); btn.title = btn.ariaLabel = t('copyLabel');
}
$('quick').addEventListener('click', e => { const b = e.target.closest('[data-quick]'); if (b) { quickKind = b.dataset.quick; renderQuick(); } });
let instKind = 'scoop';
function renderInstall() {
  const steps = keys => `<ol class="inst-steps">${keys.map(k => `<li>${esc(t(k))}</li>`).join('')}</ol>`;
  const html = instKind === 'scoop'
    ? INSTALL.scoop.map(cmdRow).join('') + `<p class="inst-note">${esc(t('instScoopNote'))}</p><p class="cmd-label">${esc(t('instUpdate'))}</p>${cmdRow('scoop update codeusagemonit')}<p class="cmd-label">${esc(t('instNoScoop'))}</p>${cmdRow('irm get.scoop.sh | iex')}`
    : instKind === 'ps'
      ? cmdRow(INSTALL.ps[0]) + steps(['instPs1', 'instPs2', 'instPs3', 'instPs4'])
      : `<a class="btn btn-primary" href="${REPO}/releases/download/v1.2.0/codeusagemonit-1.2.0-win-x64.zip"><svg viewBox="0 0 20 20" width="17" height="17" aria-hidden="true"><path d="M10 3v9m0 0-3.5-3.5M10 12l3.5-3.5M4 15.5h12" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>${esc(t('instZipBtn'))}</a>` + steps(['instZip1', 'instZip2', 'instZip3']);
  const el = $('inst-body'); el.innerHTML = html; el.style.animation = 'none'; void el.offsetWidth; el.style.animation = '';
  document.querySelectorAll('[data-inst]').forEach(b => b.setAttribute('aria-selected', String(b.dataset.inst === instKind)));
}
$('inst-tabs').addEventListener('click', e => { const b = e.target.closest('[data-inst]'); if (b) { instKind = b.dataset.inst; renderInstall(); } });

// ── Providers ─────────────────────────────────────────────────────────
const pGrid = $('p-grid'), pDetail = $('p-detail');
let pSelected = 'codex';
const CUSTOM_ICON = '<span class="pi" style="--c:var(--p-custom);background:none;-webkit-mask:none;mask:none;display:grid;place-items:center;color:var(--p-custom)"><svg viewBox="0 0 24 24" width="100%" height="100%"><path d="M12 5v14M5 12h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg></span>';
function renderProviders() {
  pGrid.innerHTML = [...IDS, 'custom'].map(id => `<button type="button" class="p-tile" role="tab" data-p="${id}" aria-selected="${id === pSelected}" style="--c:${cv(id)}">${id === 'custom' ? CUSTOM_ICON : icon(id)}<span class="name">${id === 'custom' ? t('custom') : P[id].name}</span><span class="kind">${esc(PROV_INFO[id].kind[LI()])}</span></button>`).join('');
  renderProviderDetail();
}
function renderProviderDetail() {
  const id = pSelected, info = PROV_INFO[id], li = LI();
  const name = id === 'custom' ? t('customLong') : P[id].name;
  const cap = (on, label) => `<span class="cap ${on === 1 ? 'yes' : on === 2 ? 'yes beta' : 'no'}">${label}${on === 2 ? t('capBeta') : ''}</span>`;
  pDetail.style.setProperty('--c', cv(id));
  pDetail.innerHTML = `<div class="p-anim"><div class="card-head">${id === 'custom' ? CUSTOM_ICON.replace('--c:var(--p-custom)', '--c:var(--p-custom);width:40px;height:40px') : icon(id)}<h3>${name}</h3></div>
    <div class="kv"><div><span>${t('pConnect')}</span><p>${esc(info.connect[li])}</p></div><div><span>${t('pQuota')}</span><p>${esc(info.quota[li])}</p></div></div>
    <div class="caps">${cap(info.caps[0], t('capQuota'))}${cap(info.caps[1], t('capLocal'))}${cap(info.caps[2], t('capSpeed'))}</div>
    <a class="doc" href="${REPO}/blob/main/docs/providers/${id}.md">${t('docs')}</a></div>`;
}
function pickProvider(b) { pSelected = b.dataset.p; pGrid.querySelectorAll('.p-tile').forEach(x => x.setAttribute('aria-selected', String(x === b))); renderProviderDetail(); }
pGrid.addEventListener('click', e => { const b = e.target.closest('[data-p]'); if (b) pickProvider(b); });
pGrid.addEventListener('pointerover', e => { const b = e.target.closest('[data-p]'); if (b && b.dataset.p !== pSelected && matchMedia('(pointer: fine)').matches) pickProvider(b); });

// ── Board: one tool's quotas and usage ────────────────────────────────
const BOARD_IDS = ['codex', 'claude', 'kimi', 'zcode', 'deepseek', 'pi'];
const dash = { tool: 'codex', period: '30d', metric: 'cost' };
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
function periodBars() { const s = SERIES[dash.tool]; return dash.period === 'today' ? s.hours : dash.period === '7d' ? s.days.slice(-7) : s.days; }
const valueOf = (x, m) => m === 'cost' ? x.cost : m === 'tokens' ? x.tokens : (x.speed || 0);
function countTo(el, to, fmt) {
  if (reduced || !isFinite(to)) { el.textContent = fmt(to); el._v = to; return; }
  const from = el._v ?? 0, start = performance.now(), dur = 650; el._v = to;
  const step = now => { const k = Math.min(1, (now - start) / dur), eased = 1 - Math.pow(1 - k, 3); el.textContent = fmt(from + (to - from) * eased); if (k < 1) requestAnimationFrame(step); };
  requestAnimationFrame(step);
  // Background tabs pause animation frames; make sure the final value lands.
  clearTimeout(el._t); el._t = setTimeout(() => { if (el._v === to) el.textContent = fmt(to); }, dur + 120);
}
function renderDash() {
  const bars = periodBars(), m = dash.metric;
  $('board-panel').style.setProperty('--c', cv(dash.tool));
  toolPick.querySelectorAll('button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.tool === dash.tool)));
  document.querySelectorAll('#period-seg button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.period === dash.period)));
  document.querySelectorAll('.dash-figs button.fig').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.metric === m)));
  countTo($('f-cost'), sum(bars, 'cost'), usd);
  countTo($('f-tokens'), sum(bars, 'tokens'), compact);
  countTo($('f-req'), sum(bars, 'req'), v => Math.round(v).toLocaleString('en-US'));
  countTo($('f-speed'), speedOf(bars, dash.tool), tps);
  const max = Math.max(1e-9, ...bars.map(x => valueOf(x, m)));
  chart.innerHTML = bars.map((x, i) => `<div class="bar${valueOf(x, m) <= 0 ? ' empty' : ''}${i === bars.length - 1 ? ' now' : ''}" data-i="${i}"><i style="--h:0%"></i></div>`).join('');
  requestAnimationFrame(() => requestAnimationFrame(() => chart.querySelectorAll('.bar').forEach((el, i) => { el.firstChild.style.setProperty('--h', (valueOf(bars[i], m) / max * 100).toFixed(2) + '%'); })));
  const ticks = bars.length <= 8 ? bars.map((_, i) => i) : [0, Math.round(bars.length * .25), Math.round(bars.length * .5), Math.round(bars.length * .75), bars.length - 1];
  axis.innerHTML = [...new Set(ticks)].map(i => `<span>${bars[i].hourly ? pad2(bars[i].date.getHours()) + ':00' : dayLabel(bars[i].date)}</span>`).join('');
  $('dash-note').textContent = (dash.period === 'today' ? t('noteHourly') : t('noteDaily')) + ' · ' + (dash.period === 'today' ? t('pToday') : dash.period === '7d' ? t('p7') : t('p30')) + (m === 'speed' ? ' · ' + t('noteSpeed') : '');
  tip.hidden = true;
}
toolPick.addEventListener('click', e => { const b = e.target.closest('[data-tool]'); if (b && b.dataset.tool !== dash.tool) { dash.tool = b.dataset.tool; renderBoardQuota(true); renderDash(); } });
$('period-seg').addEventListener('click', e => { const b = e.target.closest('[data-period]'); if (b) { dash.period = b.dataset.period; renderDash(); } });
document.querySelector('.dash-figs').addEventListener('click', e => { const b = e.target.closest('button.fig'); if (b) { dash.metric = b.dataset.metric; renderDash(); } });
function showTip(barEl) {
  const bars = periodBars(), x = bars[+barEl.dataset.i]; if (!x) return;
  chart.querySelectorAll('.hot').forEach(el => el.classList.remove('hot')); barEl.classList.add('hot');
  const label = x.hourly ? `${dayLabel(x.date)} ${pad2(x.date.getHours())}:00` : x.date.toLocaleDateString(lang === 'zh' ? 'zh-CN' : lang === 'ja' ? 'ja-JP' : 'en-US', { month: 'short', day: 'numeric', weekday: 'short' });
  tip.innerHTML = `<b>${label}</b><div><span>${t('tipCost')}</span><span>${usd(x.cost)}</span></div><div><span>${t('tipTokens')}</span><span>${compact(x.tokens)}</span></div><div><span>${t('tipReq')}</span><span>${x.req.toLocaleString('en-US')}</span></div><div><span>${t('tipSpeed')}</span><span>${x.speed ? tps(x.speed) : '—'}</span></div>`;
  const cr = chart.getBoundingClientRect(), br = barEl.getBoundingClientRect(), wrap = chart.parentElement.getBoundingClientRect();
  tip.hidden = false;
  const left = Math.min(wrap.width - tip.offsetWidth / 2 - 4, Math.max(tip.offsetWidth / 2 + 4, br.left - wrap.left + br.width / 2));
  const barTop = barEl.firstChild.getBoundingClientRect().top - wrap.top, chartTop = cr.top - wrap.top;
  tip.style.left = left + 'px'; tip.style.top = Math.max(chartTop + 6, barTop - tip.offsetHeight - 12) + 'px';
}
chart.addEventListener('pointerover', e => { const b = e.target.closest('.bar'); if (b) showTip(b); });
chart.addEventListener('pointerleave', () => { tip.hidden = true; chart.querySelectorAll('.hot').forEach(el => el.classList.remove('hot')); });
chart.addEventListener('click', e => { const b = e.target.closest('.bar'); if (b) showTip(b); });

// ── Pricing: the real pricing.json, searchable ────────────────────────
const VENDORS = ['all', 'anthropic', 'openai', 'google', 'xai', 'deepseek', 'moonshot', 'zhipu', 'qwen'];
const VENDOR_NAME = { anthropic: 'Anthropic', openai: 'OpenAI', google: 'Google', xai: 'xAI', deepseek: 'DeepSeek', moonshot: 'Kimi', zhipu: 'GLM', qwen: 'Qwen' };
const vendorOf = key => { const n = key.split('/').pop(); return /^claude/.test(n) ? 'anthropic' : /^(gpt|o\d|codex|chatgpt)/.test(n) ? 'openai' : /^gemini/.test(n) ? 'google' : /^grok/.test(n) ? 'xai' : /^deepseek/.test(n) ? 'deepseek' : /^(kimi|moonshot)/.test(n) ? 'moonshot' : /^glm/.test(n) ? 'zhipu' : /^(qwen|qwq)/.test(n) ? 'qwen' : 'other'; };
const FEATURED = ['claude-opus-5-5', 'claude-fable-5-1', 'claude-sonnet-5-5', 'gpt-5.5', 'gpt-5.3-codex', 'gemini-3.1-pro-preview', 'deepseek-v4-pro', 'kimi-k2.7-code', 'glm-5.2', 'claude-haiku-4-5', 'gpt-5.4-mini', 'gemini-3.5-flash', 'qwen3-coder-plus'];
// Shown if pricing.json cannot be fetched (e.g. the page opened from disk).
const PRICE_FALLBACK = { updated: '2026-09-30', models: { 'claude-opus-5-5': { in: 4, out: 20, cr: .2, cw: 5, fast: 2 }, 'claude-fable-5-1': { in: 10, out: 50, cr: .25, cw: 12.5 }, 'claude-sonnet-5-5': { in: 2, out: 10, cr: .2, cw: 2.5 }, 'gpt-5.5': { in: 5, out: 30, cr: .5, long: { at: 272000 }, fast: 2.5 }, 'gpt-5.3-codex': { in: 1.75, out: 14, cr: .175, fast: 2 }, 'gemini-3.1-pro-preview': { in: 2, out: 12, cr: .2, long: { at: 200000 }, fast: 1.8 }, 'deepseek-v4-pro': { in: 1.32, out: 3.96, cr: .044 }, 'kimi-k2.7-code': { in: .95, out: 4, cr: .19 }, 'glm-5.2': { in: 1.4, out: 4.4, cr: .28 }, 'claude-haiku-4-5': { in: 1, out: 5, cr: .1, cw: 1.25 }, 'gpt-5.4-mini': { in: .75, out: 4.5, cr: .075, fast: 2 }, 'gemini-3.5-flash': { in: 1.5, out: 9, cr: .15, fast: 1.8 }, 'qwen3-coder-plus': { in: 1, out: 5 } } };
let prices = [], priceVendor = 'all', priceQ = '';
function loadPrices(json) {
  const rank = k => { const i = FEATURED.indexOf(k); return i < 0 ? 999 : i; };
  prices = Object.entries(json.models).map(([key, v]) => ({ key, v, vendor: vendorOf(key), rank: rank(key) }))
    .sort((a, b) => a.rank - b.rank || VENDORS.indexOf(a.vendor) - VENDORS.indexOf(b.vendor) || a.key.localeCompare(b.key, 'en', { numeric: true }));
  $('price-count').textContent = prices.length; $('price-date').textContent = (json.updated || '—').slice(0, 10);
  renderVendors(); renderPrices();
}
function renderVendors() {
  $('vendors').innerHTML = VENDORS.filter(v => v === 'all' || prices.some(p => p.vendor === v)).map(v => `<button type="button" data-vendor="${v}" aria-pressed="${v === priceVendor}">${v === 'all' ? t('vendorAll') : VENDOR_NAME[v]}</button>`).join('');
}
const money = v => '$' + String(+v.toFixed(3));
function renderPrices() {
  const words = priceQ.toLowerCase().split(/\s+/).filter(Boolean);
  const rows = prices.filter(p => (priceVendor === 'all' || p.vendor === priceVendor) && words.every(w => p.key.includes(w)));
  $('price-rows').innerHTML = rows.length ? rows.map(({ key, v }) => {
    const tags = (v.long && v.long.at ? `<span class="tag">${t('tagLong').replace('{k}', Math.round(v.long.at / 1000))}</span>` : '') + (v.fast && v.fast > 1 ? `<span class="tag fast">Fast ×${trim(v.fast, 1)}</span>` : '');
    const cr = v.cr != null ? money(v.cr) : `<span class="none" title="${esc(t('defaultCache'))}">${money(v.in * .1)}*</span>`;
    return `<tr><td title="${esc(key)}">${esc(key)}${tags}</td><td>${money(v.in)}</td><td>${money(v.out)}</td><td>${cr}</td></tr>`;
  }).join('') : `<tr><td colspan="4" style="text-align:center;color:var(--ink-3);font-family:var(--sans)">${t('priceNone')}</td></tr>`;
  $('price-shown').textContent = t('priceShown').replace('{n}', rows.length).replace('{total}', prices.length);
}
$('vendors').addEventListener('click', e => { const b = e.target.closest('[data-vendor]'); if (!b) return; priceVendor = b.dataset.vendor; renderVendors(); renderPrices(); });
$('price-q').addEventListener('input', e => { priceQ = e.target.value.trim(); renderPrices(); });
// The same file the app syncs from (rebuilt daily in the repository), then the copy
// deployed with the site, then the short list above.
const getJson = (url, ms) => { const ac = new AbortController(), timer = setTimeout(() => ac.abort(), ms); return fetch(url, { cache: 'no-cache', signal: ac.signal }).then(r => { clearTimeout(timer); if (!r.ok) throw new Error(r.status); return r.json(); }); };
getJson('https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/source/Windows/pricing.json', 5000)
  .then(j => { if (!j || !j.models || Object.keys(j.models).length < 100) throw new Error('short'); return j; })
  .catch(() => getJson('pricing.json', 5000)).then(loadPrices).catch(() => loadPrices(PRICE_FALLBACK));

// Speed bars: sorted fastest first, filled when the card scrolls into view.
const speedList = $('speed-list');
const SPEED_IDS = USAGE_IDS.slice().sort((a, b) => TOOLS[b].speed - TOOLS[a].speed);
speedList.innerHTML = SPEED_IDS.map(id => `<div class="sp-row" style="--c:${cv(id)}"><span class="sp-name">${icon(id)}${P[id].name}</span><span class="sp-track"><i></i></span><span class="sp-val" data-sp="${id}">0 t/s</span></div>`).join('');
let speedLive = null;
function fillSpeeds(jitter) {
  speedList.querySelectorAll('.sp-row').forEach((row, i) => {
    const id = SPEED_IDS[i], v = TOOLS[id].speed * (jitter ? 1 + (Math.random() - .5) * .08 : 1);
    row.querySelector('i').style.setProperty('--w', Math.min(100, v / 100 * 100).toFixed(1) + '%');
    const el = row.querySelector('[data-sp]'); jitter ? (el.textContent = tps(v), el._v = v) : countTo(el, v, tps);
  });
}
new IntersectionObserver(([entry]) => {
  if (entry.isIntersecting) { if (!speedLive) { fillSpeeds(false); if (!reduced) speedLive = setInterval(() => { if (!document.hidden) fillSpeeds(true); }, 1800); } }
  else { clearInterval(speedLive); speedLive = null; }
}, { threshold: .3 }).observe(speedList);

// Relay routes: the active one carries the flow, like CC Switch changing provider.
const flows = [...document.querySelectorAll('#relay .flow')], relayNodes = [...document.querySelectorAll('#relay .node:not(.src)')];
let relayOn = 1;
function paintRelay() { flows.forEach((f, i) => f.classList.toggle('on', i === relayOn)); relayNodes.forEach((n, i) => n.classList.toggle('on', i === relayOn)); }
paintRelay();
if (!reduced) setInterval(() => { if (document.hidden) return; relayOn = [1, 0, 1, 2][(Date.now() / 2600 | 0) % 4]; paintRelay(); }, 2600);

// ── Sizes: stacked deck + pop-out viewer (FLIP) ───────────────────────
const SIZES = [
  { key: 'small', src: 'assets/small.png', w: 300, dim: '172 × 172', title: 'sSmall', text: 'vSmall' },
  { key: 'medium', src: 'assets/medium.png', w: 620, dim: '360 × 180', title: 'sMedium', text: 'vMedium' },
  { key: 'large', src: 'assets/large.png', w: 440, dim: '360 × 430', title: 'sLarge', text: 'vLarge' },
  { key: 'full', src: 'assets/overview.png', w: 0, dim: 'vFullDim', title: 'sFull', text: 'vFull' }
];
const viewer = $('viewer'), vImg = $('viewer-img'), deck = $('deck');
let vIndex = -1, lastCard = null;
$('viewer-dots').innerHTML = SIZES.map(() => '<i></i>').join('');
function sizeWidth(s) {
  const narrow = innerWidth < 760, maxH = innerHeight * (narrow ? .55 : .8);
  if (s.key === 'full') return Math.round(Math.min(maxH, 780) * 480 / 840);
  if (s.key === 'large') return Math.round(Math.min(s.w, maxH * 360 / 430, innerWidth - 48));
  return Math.min(s.w, innerWidth - 48);
}
function fillViewer(i) {
  const s = SIZES[i]; vIndex = i;
  vImg.src = s.src; vImg.style.setProperty('--vw', sizeWidth(s) + 'px');
  $('viewer-dim').textContent = s.dim.startsWith('v') ? t(s.dim) : s.dim;
  $('viewer-title').textContent = t(s.title);
  $('viewer-text').textContent = t(s.text);
  document.querySelectorAll('#viewer-dots i').forEach((d, k) => d.classList.toggle('on', k === i));
  deck.querySelectorAll('.deck-card').forEach(c => c.classList.toggle('away', c.dataset.size === s.key));
}
function flipFrom(card) {
  const from = card.querySelector('img').getBoundingClientRect(), to = vImg.getBoundingClientRect();
  if (reduced || !to.width) return;
  vImg.style.transition = 'none';
  vImg.style.transform = `translate(${from.left - to.left}px, ${from.top - to.top}px) scale(${from.width / to.width})`;
  void vImg.offsetWidth;
  vImg.style.transition = 'transform .55s cubic-bezier(.2,.75,.2,1)'; vImg.style.transform = 'none';
}
function openViewer(card) {
  lastCard = card; const i = SIZES.findIndex(s => s.key === card.dataset.size);
  viewer.hidden = false; fillViewer(i);
  const go = () => { flipFrom(card); requestAnimationFrame(() => viewer.classList.add('open')); viewer.querySelector('.viewer-close').focus({ preventScroll: true }); };
  vImg.complete ? go() : vImg.addEventListener('load', go, { once: true });
  document.body.style.overflow = 'hidden';
}
function closeViewer() {
  if (viewer.hidden) return;
  const card = deck.querySelector(`[data-size="${SIZES[vIndex].key}"]`), done = () => { viewer.hidden = true; viewer.classList.remove('open'); vImg.style.transform = ''; vImg.style.transition = ''; deck.querySelectorAll('.away').forEach(c => c.classList.remove('away')); document.body.style.overflow = ''; (lastCard || card)?.focus({ preventScroll: true }); };
  viewer.classList.remove('open');
  if (reduced || !card) return done();
  const from = vImg.getBoundingClientRect(), to = card.querySelector('img').getBoundingClientRect();
  vImg.style.transition = 'transform .45s cubic-bezier(.4,0,.2,1)';
  vImg.style.transform = `translate(${to.left - from.left}px, ${to.top - from.top}px) scale(${to.width / from.width})`;
  setTimeout(done, 430);
}
function stepViewer(d) {
  const i = (vIndex + d + SIZES.length) % SIZES.length;
  vImg.style.transition = 'opacity .18s'; vImg.style.opacity = '0';
  setTimeout(() => { fillViewer(i); vImg.style.transform = 'none'; vImg.onload = () => { vImg.style.opacity = '1'; }; if (vImg.complete) vImg.style.opacity = '1'; }, 180);
}
deck.addEventListener('click', e => { const c = e.target.closest('.deck-card'); if (c) openViewer(c); });
viewer.addEventListener('click', e => { if (e.target.closest('[data-close]')) closeViewer(); });
$('viewer-prev').addEventListener('click', () => stepViewer(-1));
$('viewer-next').addEventListener('click', () => stepViewer(1));
document.addEventListener('keydown', e => { if (viewer.hidden) return; if (e.key === 'Escape') closeViewer(); else if (e.key === 'ArrowLeft') stepViewer(-1); else if (e.key === 'ArrowRight') stepViewer(1); });
addEventListener('resize', () => { if (!viewer.hidden) vImg.style.setProperty('--vw', sizeWidth(SIZES[vIndex]) + 'px'); });
// Deep links: #size-small / -medium / -large / -full open that view.
function openFromHash() { const m = /^#size-(small|medium|large|full)$/.exec(location.hash); if (!m) return; const card = deck.querySelector('[data-size="' + m[1] + '"]'); card.scrollIntoView({ block: 'center', behavior: 'instant' }); openViewer(card); }
addEventListener('hashchange', openFromHash);

// ── Terminal (mirrors codeusage output) ───────────────────────────────
const width = s => [...s].reduce((w, ch) => w + (/[\u1100-\u115F\u2E80-\uA4CF\uAC00-\uD7A3\uF900-\uFAFF\uFE30-\uFE4F\uFF00-\uFF60\uFFE0-\uFFE6]/.test(ch) ? 2 : 1), 0);
const padR = (s, w) => s + ' '.repeat(Math.max(1, w - width(s)));
const padL = (s, w) => ' '.repeat(Math.max(1, w - width(s))) + s;
const col = (s, c) => `<span style="color:${c}">${esc(s)}</span>`, dim = s => `<span class="d">${esc(s)}</span>`, bold = s => `<span class="b">${esc(s)}</span>`;
const bar = (rem, c) => { const on = Math.round(rem / 100 * 24); return col('▮'.repeat(on), rem < 10 ? '#E6A083' : c) + dim('▯'.repeat(24 - on)); };
function stamp() { const d = new Date(); return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`; }
function quotaLines(id, plan) {
  const q = QUOTAS[id], lw = Math.max(8, Math.max(...q.windows.map(w => width(w.label))) + 2), lines = [];
  lines.push(`<span style="color:${P[id].c};font-weight:600">${P[id].name}</span>  ${dim(plan)}  ${dim('de•••@example.com')}`);
  q.windows.forEach(w => {
    lines.push('  ' + esc(padR(w.label, lw)) + bold(padL(w.rem + '%', 6)) + ' 剩余  ' + bar(w.rem, P[id].c) + '  ' + dim(w.reset.replace(' ', '') + '后重置'));
    lines.push('  ' + ' '.repeat(lw) + (w.pace >= 0 ? col('余量 ' + w.pace + '%', '#7AD3A8') + dim(' · 按当前速度可持续到重置') : col('超前消耗 ' + -w.pace + '%', '#F2B36B') + dim(' · ' + (w.empty || ''))));
  });
  if (q.credits) lines.push('  ' + esc(padR('限额重置', lw)) + q.credits + ' 次可用');
  lines.push('  ' + dim(`今日 ${usd(SERIES[id].days[29].cost)} · 30 天 ${usd(TOOLS[id].cost)} · ${compact(TOOLS[id].tokens)} Token · ${tps(speedOf(SERIES[id].days, id))}（API 等价）`));
  return lines;
}
function costLines() {
  const ids = ['codex', 'claude', 'zcode'], days = [23, 24, 25, 26, 27, 28, 29];
  const md = d => `${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
  const lines = [bold('近 7 天 · 本机用量') + dim('（本机日志 × 官方 API 价目估算，不是订阅账单）')];
  lines.push(dim(padR('日期', 8) + ids.map(id => padL(P[id].name, 12)).join('') + padL('合计', 12) + padL('Token', 10)));
  days.forEach(i => {
    const cells = ids.map(id => SERIES[id].days[i].cost), toks = ids.reduce((s, id) => s + SERIES[id].days[i].tokens, 0);
    lines.push(esc(padR(md(SERIES.codex.days[i].date), 8) + ids.map(id => padL(SERIES[id].days[i].tokens > 0 ? usd(SERIES[id].days[i].cost) : '—', 12)).join('')) + bold(padL(usd(cells.reduce((a, b) => a + b, 0)), 12)) + dim(padL(compact(toks), 10)));
  });
  const tot = ids.map(id => days.reduce((s, i) => s + SERIES[id].days[i].cost, 0));
  lines.push(bold(padR('合计', 8) + tot.map(v => padL(usd(v), 12)).join('') + padL(usd(tot.reduce((a, b) => a + b, 0)), 12) + padL(compact(days.reduce((s, i) => s + ids.reduce((x, id) => x + SERIES[id].days[i].tokens, 0), 0)), 10)));
  lines.push(dim('输出速度  ') + ids.map(id => P[id].name + ' ' + bold(tps(speedOf(days.map(i => SERIES[id].days[i]), id)))).join(dim(' · ')) + dim('（发出请求 → 最后一段输出，含首字延迟）'));
  return lines;
}
const COMMANDS = {
  status: { cmd: 'codeusage status', out: () => [bold('codeusagemonit') + dim(` V1.2 · ${stamp()} · 缓存 · 2 分钟前 · 价目 ${$('price-date').textContent}`), '', ...quotaLines('codex', 'Pro 20x'), '', ...quotaLines('claude', '演示账户')] },
  cost: { cmd: 'codeusage cost --days 7', out: costLines },
  thirdparty: { cmd: 'codeusage thirdparty', out: () => [bold('第三方 API 用量') + dim('（本机日志；服务商的周/月限额无法得知）'), '',
    `<span style="color:${P.claude.c};font-weight:600">示例中转 A</span>` + col('  使用中', '#5CC8E0') + '  ' + dim('Claude Code · relay-a.example.com'),
    '  今日 ' + bold('3.36M') + '  近 7 天 ' + bold('19.44M') + '  近 30 天 ' + bold('41.04M') + ' Token' + dim('  · 228 次请求 · 71 t/s · 官方价参考 ≈$49.25'), '',
    `<span style="color:${P.codex.c};font-weight:600">示例中转 B</span>` + '  ' + dim('Codex · api.relay-b.example.org'),
    '  今日 ' + bold('0') + '  近 7 天 ' + bold('11.44M') + '  近 30 天 ' + bold('84.24M') + ' Token' + dim('  · 468 次请求 · 29 t/s · 官方价参考 ≈$168.48')] }
};
const termBody = $('term-body');
let termCmd = 'status', termRun = 0, termSeen = false;
async function runTerm() {
  const run = ++termRun, c = COMMANDS[termCmd], prompt = '<span class="p">PS C:\\Users\\you&gt;</span> ';
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  if (reduced) { termBody.innerHTML = prompt + esc(c.cmd) + '\n' + c.out().join('\n'); return; }
  termBody.innerHTML = prompt + '<span class="caret"></span>';
  for (let i = 1; i <= c.cmd.length; i++) { if (run !== termRun) return; termBody.innerHTML = prompt + esc(c.cmd.slice(0, i)) + '<span class="caret"></span>'; await sleep(22 + Math.random() * 30); }
  await sleep(260);
  let html = prompt + esc(c.cmd) + '\n';
  for (const line of c.out()) { if (run !== termRun) return; html += line + '\n'; termBody.innerHTML = html + '<span class="caret"></span>'; await sleep(55); }
  termBody.innerHTML = html + prompt + '<span class="caret"></span>';
}
$('cmd-list').addEventListener('click', e => { const b = e.target.closest('[data-cmd]'); if (!b) return; termCmd = b.dataset.cmd; document.querySelectorAll('#cmd-list [data-cmd]').forEach(x => x.setAttribute('aria-selected', String(x === b))); runTerm(); });
new IntersectionObserver(([entry], obs) => { if (entry.isIntersecting && !termSeen) { termSeen = true; runTerm(); obs.disconnect(); } }, { threshold: .35 }).observe(termBody);
$('copy-cmd').addEventListener('click', e => copyText(COMMANDS[termCmd].cmd, e.currentTarget, () => t('copy')));
$('copy-sha').addEventListener('click', e => copyText(e.currentTarget.querySelector('code').textContent, e.currentTarget.querySelector('em'), () => t('copyShort')));

// ── Section pager (right edge) and nav state ──────────────────────────
const sections = [...document.querySelectorAll('[data-pager]')], pager = $('pager'), navLinks = [...document.querySelectorAll('.nav-links a')];
pager.innerHTML = sections.map((s, i) => `<li><a href="#${s.id}"><span class="pg-label"><b>${pad2(i)}</b><span data-pg="${s.dataset.pager}"></span></span><i></i></a></li>`).join('');
const pagerLinks = [...pager.querySelectorAll('a')];
let activeSection = -1, flashTimer = 0, scrollFrame = 0;
function paintPagerLabels() { pager.querySelectorAll('[data-pg]').forEach(el => { el.textContent = t(el.dataset.pg); }); pagerLinks.forEach((a, i) => a.setAttribute('aria-label', t(sections[i].dataset.pager))); }
function onScroll() {
  scrollFrame = 0;
  const line = innerHeight * .38;
  let idx = 0; sections.forEach((s, i) => { if (s.getBoundingClientRect().top <= line) idx = i; });
  if (innerHeight + scrollY >= document.documentElement.scrollHeight - 4) idx = sections.length - 1;
  if (idx !== activeSection) {
    const first = activeSection < 0; activeSection = idx;
    pagerLinks.forEach((a, i) => a.setAttribute('aria-current', String(i === idx)));
    navLinks.forEach(a => a.setAttribute('aria-current', String(a.getAttribute('href') === '#' + sections[idx].id)));
    if (!first) { pagerLinks.forEach(a => a.classList.remove('flash')); pagerLinks[idx].classList.add('flash'); clearTimeout(flashTimer); flashTimer = setTimeout(() => pagerLinks[idx].classList.remove('flash'), 1300); }
  }
  const max = document.documentElement.scrollHeight - innerHeight;
  $('pager-fill').style.setProperty('--p', (max > 0 ? scrollY / max * 100 : 0).toFixed(2) + '%');
  nav.classList.toggle('scrolled', scrollY > 8);
}
const nav = document.querySelector('.nav');
addEventListener('scroll', () => { if (!scrollFrame) scrollFrame = requestAnimationFrame(onScroll); }, { passive: true });
addEventListener('resize', () => { if (!scrollFrame) scrollFrame = requestAnimationFrame(onScroll); });

// ── Language ──────────────────────────────────────────────────────────
const META = {
  zh: ['codeusagemonit — Windows 上的 AI 编程额度与用量监控', document.querySelector('meta[name="description"]').content],
  en: ['codeusagemonit — AI coding quotas and usage on Windows', 'A Windows tray app for the remaining quota, reset times, local token usage and output speed of Codex, Claude, Cursor and 8 more AI coding tools. Priced at official API rates, synced daily. Open source; data stays on your PC.'],
  ja: ['codeusagemonit — Windows で AI コーディングの利用枠と使用量を確認', 'Codex、Claude、Cursor など 11 の AI コーディングツールの残り利用枠、リセット時刻、トークン使用量、出力速度を Windows のトレイで。公式 API 単価で計算し、料金表は毎日同期。オープンソースで、データは PC 内で集計します。']
};
let jaFont = false;
function setLang(value) {
  lang = DICT[value] ? value : 'zh';
  if (lang === 'ja' && !jaFont) { jaFont = true; const l = document.createElement('link'); l.rel = 'stylesheet'; l.href = 'https://fonts.googleapis.cn/css2?family=Noto+Sans+JP:wght@400;500;700&display=swap'; document.head.appendChild(l); }
  root.lang = { zh: 'zh-CN', en: 'en', ja: 'ja' }[lang];
  document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
  document.querySelectorAll('[data-i18n-ph]').forEach(el => { el.placeholder = t(el.dataset.i18nPh); });
  document.querySelectorAll('[data-theme-set]').forEach(b => { const k = { light: 'themeLight', dark: 'themeDark', system: 'themeSystem' }[b.dataset.themeSet]; b.title = b.ariaLabel = t(k); });
  document.querySelectorAll('.lang [data-lang]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.lang === lang)));
  document.title = META[lang][0]; document.querySelector('meta[name="description"]').content = META[lang][1];
  paintPagerLabels(); renderProviders(); renderDash(); renderQuick(); renderInstall();
  if (prices.length) { renderVendors(); renderPrices(); }
  if (!viewer.hidden) fillViewer(vIndex);
  try { localStorage.setItem('codeusagemonit-language', lang); } catch { }
}
document.querySelector('.lang').addEventListener('click', e => { const b = e.target.closest('[data-lang]'); if (b) setLang(b.dataset.lang); });

// ── Reveal on scroll ──────────────────────────────────────────────────
const revealer = new IntersectionObserver(entries => entries.forEach(entry => {
  if (!entry.isIntersecting) return;
  entry.target.classList.add('in'); revealer.unobserve(entry.target);
  if (entry.target.id === 'board-panel') renderBoardQuota(true);
}), { threshold: .12, rootMargin: '0px 0px -40px 0px' });
document.querySelectorAll('.reveal').forEach(el => revealer.observe(el));

// ── Start ─────────────────────────────────────────────────────────────
renderTabs(); renderPage('overview'); renderBoardQuota(false);
setTimeout(openFromHash, 300);
let saved; try { saved = localStorage.getItem('codeusagemonit-language'); } catch { }
setLang(saved || (/^ja/i.test(navigator.language) ? 'ja' : /^zh/i.test(navigator.language) ? 'zh' : 'en'));
onScroll();
