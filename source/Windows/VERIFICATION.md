# Verification — Windows 0.8

Validated on Windows 11 Pro x64 (build 26200), 2026-09-30.

## 0.8 current release

- **59 self-tests pass.** New range fixtures cover local midnight versus a rolling day, a fixed start with follow-now, invalid/reversed/future/old input, weighted hour boundaries, scanned current-hour prefixes, exclusive end boundaries, missing histories, conservative daily-only selection, fractional-time-zone midnight pricing, wider historical scan coverage and index-cache invalidation.
- **22 offline WPF integration checks pass.** New checks exercise the calendar in all layouts, five preset buttons, actual day-cell selection, HH:mm edits, confirm/cancel validation, independent per-provider persistence and follow-now. They also measure equal progress-track widths with mixed refresh/connect states and verify distinct small/medium/large summary structures with icon navigation. Existing resize, geometry, connection, settings, refresh and privacy regressions remain covered. A synthetic history executable also verifies one scan per confirmation, coalescing of obsolete queued requests and acceptance of the latest range; no real user logs are read by this check.
- Inspected screenshots include calendar selection, small/medium/large summaries, aligned overviews, settings and connection pages. Synthetic account data is used; no real login or credential submission is part of these UI checks. WPF screenshots validate layout, not composited backdrop appearance.
- Period totals explicitly retain their source granularity. These checks do not claim minute-accurate source records, actual billing amounts, or recovery of logs missing from the computer.

## 0.7 previous release

- **48 self-tests pass**, including Acrylic migration from Mica while retaining alpha/geometry and local-only provider refresh that preserves unrelated providers' history.
- **15 offline WPF integration checks pass**: full-panel context menu switches layouts; all four native HWNDs/layout states return left/right/bottom resize hit codes; per-layout custom geometry survives switching; small/medium/large overview and detail navigation retain geometry; platform dropdowns and 7/30-day filters work; styled connection buttons open forms at the current size; region/key drafts survive refresh; every layout opens one separate Settings window without changing the monitor; Acrylic transparency previews revert on cancel and persist on save; unrelated settings saves do not undo a layout changed while Settings was open; single refresh stays independent; ready accounts can reconnect; summaries remain provider-specific; old refresh results are rejected; mock CLI quoting and cold saved-geometry startup work.
- Native geometry comparisons allow less than one logical pixel of DPI rounding (host at 125%). Layout screenshots use WPF rendering, so they do not validate desktop compositing or actual remote-account authorization.
- Screenshots inspected: all compact overviews, provider detail pages, connection forms, small API-key forms, full overview and the standalone Settings window. Tests do not move the user's mouse, submit real keys, or start real login flows.

## 0.6 previous release

- Both x64 executables compile with the installed .NET Framework C# compiler. **46 self-tests passed**, including migration of old opaque/multi-widget settings, valid display modes, invalid geometry, and the complete transparency range.
- **11 offline WPF integration checks passed** (`verify-regression.ps1`, synthetic accounts): chart filter clicks preserve every provider-card and meter instance; one-provider busy state is independent; overlapping fetch generations reject older results; all four sizes use the same HWND at 80/100/140% scale; large overview exposes every enabled provider in a scrollable list; compact placement survives opening/cancelling Settings; cached expired accounts keep their connect link in every compact size; API-key/region drafts survive renders; Settings offer only Acrylic/Mica and preserve cancel/save semantics at both transparency endpoints; a mock `.cmd` launcher in a path with spaces receives `login` correctly; cold compact startup restores saved zoom.
- The test harness invokes actual WPF routed controls, without moving the mouse or authorizing accounts. Screenshots of full, small, medium, large overview/detail, Settings and card connections were rendered and inspected. RenderTargetBitmap captures layout, not the composited DWM backdrop.
- Login/HTTP success against every remote service was **not** tested. Existing provider/parser fixtures remain in the self-tests; live CLI/browser authorization requires the user's own session. No real login was initiated by the UI checks. The CLI launcher was tested with a harmless local fixture.
- Windows-specific checks replace upstream Swift checks on this machine: neither `make` nor `swift` is installed. Swift/macOS sources were not changed.
- Release packaging uses an explicit source/asset allowlist and includes Windows source, rebuild/package scripts, MIT notices and ccusage. Runtime settings, keys, account caches, test output and logs are excluded. A SHA-256 inventory is included; the ZIP has a companion `.sha256` file.

The records below describe earlier versions; multi-widget and opaque-option observations do not describe the 0.6 interface.

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
- Claude hourly index vs a fresh ccusage run: **identical on all 15 days with usage** (2026-08-31 … 2026-09-29), after adopting ccusage's timestamp fallback (one day had 433 records without request id).
- Real data: both clients currently official; the app recorded that in `endpoints.json`, read 20 host → name entries from CC Switch, and grouped 518.3M Claude tokens (2026-08-31 … 09-20) without request id as a suspected relay. Evidence that request id absence marks a relay: the same client version logged request ids against the official API on 09-22 but not on 09-02; some records carry `entrypoint: claude-desktop-3p` or non-Anthropic message ids.
- Live switch, isolated: with `CLAUDE_CONFIG_DIR` pointed at a scratch folder, rewriting its `settings.json` to a relay produced a new mark within 4 s (host normalised, `Since` equal to the file write time, key stored only as a 6-hex fingerprint). The user's real configuration was not modified.

## Automated

- Compiled with the installed .NET Framework C# compiler (`/langversion:5 /codepage:65001`).
- **33 self-tests passed** (`--self-test`), 27 carried over from 0.2 plus: ccusage token-composition fields, history merge (past days never shrink, today replaced, time-zone change resets), Codex log reader (duplicate `token_count` skipped, `info: null` ignored, >256 KB lines skipped, unfinished tail left for the next scan), exact-window clipping and per-day pricing, epoch-millisecond timestamps, quota-cache round trip.
- **7 UI Automation checks passed** on the isolated `--demo` preview (no mouse movement): every tab switches pages; Codex detail fields present; settings open inside the panel (no second window); material preview applies live and is reverted by back/cancel (DWM backdrop 1 → 3); Save persists scale, always-on-top and provider selection; a disabled provider leaves the tab strip; production `data/settings.json` hash unchanged.
- `verify-interaction.ps1` was updated for the in-panel settings page and saved with a BOM (the 0.2 file failed to parse in Windows PowerShell 5.1). Its physical mouse drag/resize steps were **not run** in this session because the desktop was in use.

## Data accuracy

- Codex hourly index vs ccusage daily totals: **identical on all 22 days** from 2026-09-08 to 2026-09-29 (difference 0 tokens each day).
- Incremental rescan: 9 ms with no changed files; cold full scan of ~1.1 GB of session logs: 570 ms (OS cache warm).
- CC Switch comparison for 2026-09-29: token components identical; cost difference fully explained by the GPT-6 Astra > 272K long-context tier (recomputed $294.5226 vs ccusage $294.522551; flat pricing reproduces CC Switch's $188.9134). Official rule confirmed on the OpenAI model page.

## Visual

- Demo and real-data screenshots inspected: overview hero with stacked 30-day chart, provider cards, detail page (period table, composition, chart, quota windows), settings page, 32-bit tray icon at 16–48 px.

Personal caches and screenshots containing live usage are not included in distributable archives.
