# codeusagemonit for Windows — 0.7

A single-tray Windows monitor for eleven built-in providers and custom JSON APIs. The interface and selected provider logic are adapted from CodexBar under MIT licenses.

## Window and appearance

- One visual system in `Panel.xaml`: smoked glass over DWM Acrylic, neutral ink, one cool accent (`#5CC8E0`), provider colours in data. Cards, switches, segmented controls, text fields, sliders, scrollbars and tooltips share the same styles.
- The provider strip lists enabled providers and wraps to two rows when needed; providers needing attention get an amber dot.
- Quota rows keep the user's segmented meter (24 cells, 2 px gaps, remaining fraction in the provider colour, warm colour below 10 %).
- Settings open in a separate, resizable singleton window. The monitor keeps its layout and geometry and continues updating. Save applies changes; Esc/native close/cancel discard drafts and restore the saved transparency.
- Drag the title area to move the panel; drag any edge/corner to resize. Placement, size and Ctrl+wheel scale (80–140 %) persist.
- Always-on-top is off by default and independent from the keep-open/auto-hide pin.
- The tray icon is a multi-size 32-bit ICO; the tray menu uses a dark renderer.
- Acrylic is the only material. The transparency slider runs from 0 to 100: 0 makes the smoke layer fully opaque, 100 removes the layer while retaining the Windows backdrop. Old `solid` settings migrate to Acrylic at 0 transparency; old Mica preserves its transparency. Unsupported Windows versions use an opaque fallback.
- Full overview chart filters replace only the chart/totals region, preserving account cards and quota meters. Each account has independent refresh; Pi refreshes its own local history. F5 refreshes the compact platform being viewed; Ctrl+F5 refreshes all. A provider's action menu offers details, copy and reconnection.

## Account and history details

Codex displays the upstream plan label (Pro 20x / Pro 5x), quota windows, reset countdowns, a linear pace estimate, and read-only reset-credit inventory. The software never redeems reset credits or activates quota windows.

Each provider with local records shows a period table (today / current quota window / 30 days × API-equivalent cost / tokens), the token composition (fresh input, output, cache read, cache write, cache-hit rate — same definitions as CC Switch), a 30-day chart, recent quota windows and the most-used model.

**Codex quota windows are hour-accurate.** `CodexLogs` incrementally indexes `~/.codex/sessions` and `archived_sessions` into hourly token buckets (only counts, never content; `data/codex-logs.json`). The counting rule matches ccusage 20.0.26 token-for-token on every full day checked. Window cost is apportioned with each local day's ccusage $/token rate, so it is shown as ≈. Other providers fall back to whole local days inside the window (a lower bound, shown as ≥).

History merge: past days keep the largest total ever observed, so pruned or deleted session files do not shrink history. Days are grouped by the Windows time zone.

Pricing comes from ccusage's offline table, which includes cache discounts and long-context tiers (e.g. GPT-6 Astra requests with > 272K input tokens cost 2× input/cache and 1.5× output). That tier explains why CC Switch, which prices flat, reports lower Codex costs for identical token counts.

## Four mutually exclusive display sizes

`Compact.cs` provides three information-density presets in the same monitor window: small (260×320), medium (420×320), large (420×540), plus the full panel (420×790). All four have native resize edges. Each layout's custom geometry persists independently in `AppConfig.Layouts`; legacy full-panel geometry is retained. Defaults are logical pixels at 100% zoom. The full panel's context menu is available throughout its background, not just the title strip.

Compact layouts are normal interactive windows, with optional always-on-top. Every preset has an overview, a platform picker, single-platform refresh, a 7/30-day range, clickable quota/detail navigation, copy feedback and button-based account controls. Connection forms stay inside the current window with scrolling at small widths; API-key/region drafts survive refreshes. Back/Esc returns to the previous compact page. Settings use a separate window without switching the monitor to full. Full and compact backgrounds share one Acrylic transparency preference.

## Command line

`codeusage.exe` is compiled from the same sources (`/main:CodeUsageMonit.CliProgram`, console target) and shares the `data` folder: `status` (cached, offline), `usage` (live), `cost`, `thirdparty`, `providers`; `--provider`, `--all`, `--days`, `--refresh`, `--json`, `--no-color`. Output uses 24-bit ANSI colour when the console supports virtual-terminal sequences, CJK-aware column widths, and a 24-cell text meter.

## Third-party endpoints (relays)

Usage sent through third-party endpoints never moves the subscription meters, so it gets its own page. It shows **usage only** — relays set their own weekly/monthly limits and do not publish them, so there is deliberately no quota meter.

- `LocalLogs.cs` indexes Codex *and* Claude Code session logs into `hour|provider|model` token buckets (`data/codex-logs.json`, `data/claude-logs.json`; counts only). Both match ccusage 20.0.26 token-for-token per day, including ccusage's Claude de-duplication (`message.id` + `requestId`, or + `timestamp` when a relay drops the request id).
- `ThirdParty.cs` attributes buckets to endpoints:
  - Codex: `session_meta.model_provider` separates official (`openai`) from custom exactly; the host comes from a switch timeline.
  - Claude Code: logs carry no endpoint, so the app watches `settings.json` (`ANTHROPIC_BASE_URL`) and records each switch at the file's write time (`data/endpoints.json`). Usage before the first mark is reported as unattributed.
  - Claude records without a request id (the official API always returns one) are grouped as a "suspected relay".
- API keys are never stored; a 6-hex SHA-256 fingerprint distinguishes accounts on one host. If CC Switch is installed, provider names are read (read-only) from its database for display; its keys are not read.
- The Codex subscription window counts only the official provider. "Official-price reference" costs reuse ccusage's per-model daily rates and are not what a relay charges.

## Provider connections

Disconnected/expired accounts expose connection controls directly in their overview card and detail page; errors offer an expandable reconnect section. Key-based providers accept an API key inline (and a region for Kimi/ZCode), with drafts retained during refreshes. Copilot offers its device-code flow. Codex/Claude/Grok open an installed CLI in a new terminal; Claude requires `/login`. Cursor/Antigravity open the installed app. After external login, use the card's “我已登录，刷新”. Custom cards open their definition editor. Credentials remain in the existing apps or in the same DPAPI store used by Settings. Full and single-provider refreshes keep only the latest-started result for each provider.

| Provider | Source |
| --- | --- |
| Codex | Existing `CODEX_HOME/auth.json` or `~/.codex/auth.json`; quota and reset-credit endpoints |
| Claude | Existing Claude Code login |
| Cursor | Read-only editor `state.vscdb`; separate total / Auto / API meters and reported billing-cycle boundaries |
| Antigravity | First tries an already-running same-user desktop language server with its local CSRF token, then the existing Google credential fallback |
| DeepSeek | API key entered in Settings or `DEEPSEEK_API_KEY`; balances are kept separate by currency |
| Grok | Existing xAI Grok Build CLI login; shared billing-period allowance |
| GitHub Copilot | In-app GitHub OAuth device flow (VS Code Copilot client ID, `read:user`), or a token already saved by an official Copilot client (`github-copilot/apps.json` / `hosts.json`, read-only); `GET api.github.com/copilot_internal/user` |
| Kimi Code | API key (`KIMI_CODE_API_KEY` or DPAPI-saved); `GET api.kimi.com|api.kimi.ai/coding/v1/usages`; ratio pools, legacy counts, rate-limit windows |
| OpenCode | OpenCode Go API key (`OPENCODE_API_KEY` or saved); `GET opencode.ai/zen/go/v1/usage` |
| ZCode | GLM Coding Plan API key (`Z_AI_API_KEY`, China aliases `BIGMODEL_API_KEY` …); `GET open.bigmodel.cn|api.z.ai/api/monitor/usage/quota/limit` |
| Pi | No account quota; local logs only |

The five newer providers are opt-in (`ProviderCatalog.DefaultEnabled` keeps the original six). Local history for all of them comes from ccusage's `--by-agent` rows. Browser cookies are never imported: Chrome's app-bound cookie encryption on Windows makes that impractical and it would widen the credential scope. Mappings follow the upstream CodexBar fetchers (`CopilotUsageFetcher`, `KimiModels`, `OpenCodeGoUsageFetcher`, `zai.js`); each has a fixture self-test.

## Custom providers

`CustomProviders.cs` implements a declarative HTTP-JSON provider, following the boundary of CodexBar's accepted custom-provider design (`docs/custom-provider-design.md`) rather than its JavaScript plugin runtime, which would need an embedded JS engine:

- One `GET` to an HTTPS URL (plain HTTP only for loopback / RFC 1918 / link-local / `.local` hosts); no user info or fragments; redirects are errors; 15 s timeout; 1 MiB response cap.
- Auth forms: `none`, `bearer`, `token`, `x-api-key`, or a named header (forbidden: Host, Cookie, …). The secret is entered separately and stored with DPAPI as `data/custom-<id>.key`; it never appears in the definition, URL or messages.
- Mapping: up to 6 windows (`usedPercent` | `remainingPercent` | `used`/`remaining` + `limit`, optional `ratio`, `resetsAt`, `resetInSeconds`, `windowHours`), a balance and plan/account strings, via a dot-path subset (`a.b[0].c`, ≤ 32 components). Unknown keys fail validation; missing values stay unknown instead of becoming 0.
- Definitions live in `data/custom-providers.json` and are created in Settings (JSON editor with Test / Save / Delete). Instance IDs are `custom-<id>` and cannot collide with built-ins.

## Privacy and network behavior

Application settings/cache live in `data` beside the executable. Auto proxy mode reads the Windows HTTP proxy, with HTTP/HTTPS environment fallback. It does not change global environment variables. External HTTPS validates certificates and rejects authenticated redirects.

Provider login tokens remain in the owning applications and are not copied to this application's settings or logs. DeepSeek keys saved through Settings use Windows DPAPI for the current user. Usage caches contain personal account metadata.

## Build and verification

```powershell
& .\source\Windows\build.ps1
.\codeusagemonit.exe --self-test
.\codeusagemonit.exe --demo
# Offline WPF integration checks; use a disposable output folder:
& .\source\Windows\build.ps1 -OutputDirectory .\verification\preview
& .\source\Windows\verify-regression.ps1 -OutputDirectory .\verification\preview
# Build a portable ZIP, source and licenses included:
& .\source\Windows\package.ps1
```

Build uses the Windows-supplied .NET Framework C# 5 compiler (`/codepage:65001`) and WPF assemblies. Scripts containing Chinese text are saved as UTF-8 with BOM so Windows PowerShell 5.1 parses them. `verify-regression.ps1` compiles a separate harness from `tests/UiRegression.cs`, invokes real WPF control events with synthetic account data, writes screenshots/reports under the isolated output's `verification`, and never starts live login. Screenshots render WPF content and do not prove the DWM backdrop. `verify-interaction.ps1` is the older physical drag/resize harness; run only in an isolated visible demo when mouse automation will not interfere with user work.

Demo mode has a separate instance and stores its settings under `verification/demo-data`; it neither reads account credentials nor modifies production preferences. `--probe` is an explicit real-account diagnostic and writes no tokens or account identifiers.

`--background` starts in the tray; `--quit` exits the production instance; `--quit --demo` exits the preview. The close button hides the panel.

See `THIRD-PARTY-NOTICES.md` and `licenses/` for attribution. This is an independent Windows adaptation, not an official CodexBar Windows release.
