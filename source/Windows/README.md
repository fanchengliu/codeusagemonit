# codeusagemonit for Windows — 1.1

A single-tray Windows monitor for the quotas and local usage of AI coding assistants (Codex, Claude, Cursor, Antigravity, DeepSeek, Grok, GitHub Copilot, Kimi Code, OpenCode, ZCode, Pi and user-defined HTTP providers). It is an independent C# / WPF implementation. CodexBar and ccusage were studied as references (interaction ideas, provider endpoints, log formats); none of their code or binaries is included. The provider logos in `icons/` are the products' own logos, taken from CodexBar's resources (MIT) — see `THIRD-PARTY-NOTICES.md`.

## Window and appearance

- One visual system in `Panel.xaml`: a smoked surface over DWM Acrylic, neutral ink, one cool accent (`#5CC8E0`), provider colours only in data. Cards, switches, segmented controls, text fields, slider, scrollbar and tooltips are all styled there.
- **Surface colour and interface transparency** (`ColorPicker.cs`): a palette with 10 dark presets, a saturation/brightness square, hue and transparency strips and a hex field (`SurfaceColor`, `#RRGGBB`); transparency 0–100 % (0 = opaque) is shared with the slider. The colour tints the layer over the system blur, the picture dim layer and the popups. Text stays light, so the palette shows the WCAG contrast ratio and warns below 4.5 : 1. The old Mica / "solid" materials are migrated (solid → 0 %).
- **Background picture** (`Appearance.cs`): Settings → 外观 → 选择图片… copies the picture to `data/background.<ext>` (no database; one local file, ≤ 40 MB, decoded at ≤ 2560 px), with fill / fit / tile and a 0–85 % dim scrim. It is shown on every display size, under the transparency layer. 移除 deletes the copy.
- The provider strip is a single row and only lists enabled providers; providers needing attention get an amber dot.
- Quota rows keep the user's segmented meter (24 cells, 2 px gaps, remaining fraction in the provider colour, warm colour below 10 %). In the full panel a quota row is a button: click to expand the exact reset time, window length and used share.
- Settings open in their own window built from the same styles (the panel keeps its display size). Changes apply on Save; Cancel, close or Esc discards them. Transparency and the background picture preview live.
- The window has no native caption: the header is dragged with `DragMove`, so right-clicking anywhere opens the display-size menu instead of the system menu. Drag any edge/corner to resize; Ctrl+wheel scales the interface (80–140 %).
- Card headers: provider name, plan and refresh button share the first line; freshness and the (optionally masked) account share the second, right-aligned under the refresh button.
- Every provider card and detail page has its own refresh button (`RefreshOne`); a per-provider fetch serial makes sure an older result never overwrites a newer one.
- Providers that are not connected (or whose login expired) are connected from their own card (`Connect.cs`).

## Local usage engine (`LocalLogs.cs`, `AgentLogs.cs`, `MoreAgentLogs.cs`, `Pricing.cs`, `UsageScan.cs`)

codeusagemonit reads each assistant's own local history and prices every request itself; nothing external is executed.

| Agent | Source read | Notes |
| --- | --- | --- |
| Codex | `CODEX_HOME` / `~/.codex/sessions`, `archived_sessions` (`*.jsonl`) | `last_token_usage`, or the difference of `total_token_usage`; model from `turn_context`; `service_tier` fast / priority multiplier (also the `config.toml` default); replayed prefixes of forked threads skipped; cross-file de-duplication |
| Claude Code | `CLAUDE_CONFIG_DIR` / `~/.claude/projects`, `~/.config/claude/projects` | `message.id` + `requestId` de-duplication (else + timestamp); the larger copy of a streamed message wins; `costUSD` used when present; sidechain / advisor records |
| Antigravity | `~/.gemini/antigravity*/conversations/*.db` (SQLite, protobuf metadata) | per-step usage, identity de-duplication |
| ZCode | `ZCODE_HOME` / `~/.zcode/cli/db/db.sqlite` | completed `model_usage` rows |
| Grok | `GROK_HOME` / `~/.grok/sessions/**/updates.jsonl` | `turn_completed`; recorded cost preferred |
| Kimi | `KIMI_DATA_DIR` / `~/.kimi`, `~/.kimi-code` → `sessions/**/wire.jsonl` | `StatusUpdate` and `usage.record` |
| Pi, GitHub Copilot CLI, OpenCode | session JSONL / OTEL spans / SQLite + JSON | ported from the documented formats; **no local data on the test machine** |

- Each agent has an hourly index `data/<agent>-logs.json` with buckets `yyyyMMddHH|provider|model` → fresh input, cache read, cache write, output, other tokens, requests, USD, unpriced tokens. Only counts are stored, never content. Scans are incremental (file size / offset); about 33 days of hours are retained. The index is rebuilt automatically when its version or the price table changes.
- `pricing.json` (USD per million tokens, 361 models) is compiled from the public LiteLLM and models.dev catalogs. Lookup: exact → without provider prefix → without date suffix → `.`/`@` normalised → longest contained key (a version number is never continued: `gpt-5` does not match `gpt-5-4`). Rules: cache read / 5-minute and 1-hour cache write rates, whole-request long-context tiers (e.g. > 272K input), LiteLLM `above_200k` marginal tiers, Codex cached input at the input rate when no cached rate exists, fast / priority multipliers. Tokens of models without a price are counted and flagged ("部分模型没有价目").
- **Output speed** (`Timing.cs`): every bucket also keeps TN / TO / TS — requests with a measured duration, their output tokens (reasoning included) and seconds, so speed = TO / TS is additive over any period, model or endpoint and is always shown **per agent**, never averaged across agents. Duration is request sent → last output written (time to first token, network and relay latency included). Claude Code: the entry the response answers (`parentUuid`) → the response's last block; Codex: the last tool output / user message → the response's last item (`response_item` timestamps, read from the line prefix without decoding tool output); ZCode: `started_at` → `completed_at`; Grok: `apiDurationMs` per turn; OpenCode: `time.created` → `time.completed` (unverified). Requests with < 50 output tokens or outside 0.2 s – 10 min are not timed.
- `HistoryService.FromIndexes` derives the 30-day daily history (`data/history.json`, engine `native-1`). Past days keep the largest total ever observed, so pruned session files do not shrink history; days are grouped by the Windows time zone.

## Periods and charts (`RangePicker.cs`, `Charts.cs`)

- Every usage view has a period button: 当天 / 近 24 小时 / 近 7 天 / 近 14 天 / 近 30 天, or a custom start and end (calendar + hour grid, "end follows now"). Presets apply at once; custom ranges apply on 确定. Hourly detail reaches back ~32 days.
- Figures for the period: cost, tokens, requests, token composition, per-model list, and a chart — hourly bars when the period is ≤ 48 h, daily bars otherwise. Charts are stacked by model (provider pages) or by provider (overview).
- Charts are interactive: date axis, hover tooltip, click a bar to pin its detail (date/hour, cost, tokens, speed, per-model or per-provider split), 费用 / Token toggle, plus 速度 on one agent's charts. Speed also appears as a fourth figure on provider pages, per model, in each overview card (last 7 days), on third-party endpoint cards and in `codeusage cost`. In the overview, the provider chips filter the chart and totals without rebuilding the quota cards.
- The current quota window's usage is hour-accurate for every agent with an index; providers without local logs fall back to whole local days (a lower bound, shown as ≥).

## Display sizes

One window, four sizes, one chosen at a time (`DisplaySize`): small, medium and large render into `CompactRoot` (`Compact.cs`); full is the panel (`ScaleRoot`). Defaults are 172×172, 360×180 and 360×430 at 100 % scale.

- Every size is resizable within its own limits (`WindowFrame.MinSize` / `MaxSize`) and remembers its own geometry (`Layouts` in `settings.json`).
- Every size has an overview page plus one page per enabled provider; switch with the icon strip, dots, mouse wheel or ←/→.
- Interactions: per-provider refresh; click the headline to cycle quota windows (small / medium); click a window row to promote it (medium) or expand it (large); the medium size shows quota windows only (a single window adds pace, exact reset and reset credits; providers without quotas show 7-day / 30-day facts), charts are in the large and full sizes; click chart bars to read a day or hour; the large size has its own period button, cost / token / request figures and chart; connect a provider inline in the same size; ⋯ / right-click menu.
- Compact sizes sit at the bottom of the z-order (a `WM_WINDOWPOSCHANGING` hook) unless "always on top" is on.

## Command line

`codeusage.exe` is compiled from the same sources (`/main:CodeUsageMonit.CliProgram`, console target) and shares the `data` folder: `status` (cached, offline), `usage` (live), `cost`, `thirdparty`, `providers`; `--provider`, `--all`, `--days`, `--refresh`, `--json`, `--no-color`. `cost --refresh` rescans the local logs with the same engine; `--json` includes requests, unpriced tokens and per-model cost.

## Third-party endpoints (relays)

Usage sent through third-party endpoints never moves the subscription meters, so it gets its own page, with **usage only** (relays do not publish their limits).

- `ThirdParty.cs` attributes Codex / Claude buckets to endpoints: Codex by `session_meta.model_provider` plus a switch timeline; Claude Code by watching `settings.json` (`ANTHROPIC_BASE_URL`) and recording each switch (`data/endpoints.json`). Claude records without a request id are grouped as a "suspected relay".
- API keys are never stored; a 6-hex SHA-256 fingerprint distinguishes accounts on one host. CC Switch's database is read only for provider names.
- "Official-price reference" costs use the same per-request pricing and are not what a relay charges.

## Provider connections

| Provider | Source |
| --- | --- |
| Codex | Existing `CODEX_HOME/auth.json` or `~/.codex/auth.json`; quota and reset-credit endpoints |
| Claude | Existing Claude Code login |
| Cursor | Read-only editor `state.vscdb`; plan total / Auto / API, plus a Grok Bot weekly meter (`get-sand-usage-status`) when the included limit is non-zero. A failed extra call leaves the three plan meters up; 套餐总量 stays the headline |
| Antigravity | An already-running same-user desktop language server with its local CSRF token, then the existing Google credential fallback |
| DeepSeek | API key entered in Settings or `DEEPSEEK_API_KEY`; balances kept separate by currency |
| Grok | Existing xAI Grok Build CLI login |
| GitHub Copilot | In-app GitHub OAuth device flow (`read:user`), or a token already saved by an official Copilot client (read-only) |
| Kimi Code | API key (`KIMI_CODE_API_KEY` or DPAPI-saved) |
| OpenCode | OpenCode Go API key (`OPENCODE_API_KEY` or saved) |
| ZCode | GLM Coding Plan API key (`Z_AI_API_KEY`, China aliases `BIGMODEL_API_KEY` …) |
| Pi | No account quota; local logs only |

The five newer providers are opt-in. Browser cookies are never imported.

## Custom providers

`CustomProviders.cs` implements a declarative HTTP-JSON provider: one `GET` to an HTTPS URL (plain HTTP only for loopback / private hosts), fixed auth header forms, the secret stored with DPAPI as `data/custom-<id>.key`, redirects rejected, 15 s timeout, 1 MiB cap, up to 6 windows plus balance / plan / account mapped by a dot-path subset. Definitions live in `data/custom-providers.json`.

## Privacy and network behavior

Application settings, caches, indexes and the background picture live in `data` beside the executable. Auto proxy mode reads the Windows HTTP proxy, with HTTP/HTTPS environment fallback; global environment variables are not changed. External HTTPS validates certificates and rejects authenticated redirects. Provider login tokens remain in the owning applications. Keys saved through Settings use Windows DPAPI for the current user.

## Build and verification

```powershell
& .\source\Windows\build.ps1
.\codeusagemonit.exe --self-test
.\codeusagemonit.exe --demo
```

Build uses the Windows-supplied .NET Framework C# 5 compiler (`/codepage:65001`) and WPF assemblies, and copies `Panel.xaml`, `pricing.json` and `icons/` next to the executables. Scripts containing Chinese text are saved as UTF-8 with BOM so Windows PowerShell 5.1 parses them.

Demo mode has a separate instance and stores its settings under `verification/demo-data`; it neither reads account credentials nor modifies production preferences. `--background` starts in the tray; `--quit` exits the production instance; `--quit --demo` exits the preview.

See `THIRD-PARTY-NOTICES.md` and `licenses/` for attribution. This is an independent application, not an official release of CodexBar or ccusage.
