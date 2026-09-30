<div align="center">

<img src="./docs/favicon.svg" width="88" height="88" alt="codeusagemonit" />

# codeusagemonit

**AI coding quotas and usage, in your Windows tray**

Remaining quota and reset times for Codex, Claude, Cursor and 8 more AI coding tools, along with local token usage and output speed, in one small window.

[![Release](https://img.shields.io/github/v/release/fanchengliu/codeusagemonit?style=flat-square&color=38bdf8)](https://github.com/fanchengliu/codeusagemonit/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/fanchengliu/codeusagemonit/total?style=flat-square&color=818cf8)](https://github.com/fanchengliu/codeusagemonit/releases)
[![License](https://img.shields.io/badge/license-MIT-22c55e?style=flat-square)](./LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d4?style=flat-square)](#install)

[Website](https://codeusagemonit.vercel.app) · [Download](https://github.com/fanchengliu/codeusagemonit/releases/latest) · [User guide (Chinese)](./使用说明.md) · [Releases](https://github.com/fanchengliu/codeusagemonit/releases)

[简体中文](./README.md) · **English**

<img src="./docs/readme-hero.png" width="820" alt="codeusagemonit: full, large, small and medium" />

</div>

> [!NOTE]
> The app's interface is in Simplified Chinese. This page and the [website](https://codeusagemonit.vercel.app) are available in English.

## What it is

If you use several AI coding tools, you juggle several quota windows: 5-hour, weekly, monthly, each resetting at a different time. Local session logs sit in different folders, and usage that goes through a third-party relay is hard to track at all.

codeusagemonit puts all of it in the Windows tray. Click the icon to see what's left on each provider, when it resets, and whether the current pace lasts until then. Tools with local logs also get token totals and an API-equivalent cost; some of them show output speed. It is a native C# / WPF app with a companion CLI, `codeusage`. Free and open source under the MIT license.

## Features

- **Quotas at a glance.** Each quota window is a 24-cell meter; lit cells are what's left. The line below says whether you have headroom or are spending ahead of pace, and estimates when you'll run out. Expand a row for the exact reset time.
- **Local usage and cost.** Reads session logs for tools that keep them on your PC, and counts tokens and requests by hour and by model. Cost is estimated per request from official API prices. Pick any period; click a bar to pin that day's or hour's breakdown.
- **Prices synced daily.** Ships with official prices for 361 models (compiled from LiteLLM and models.dev), including cache reads and writes, long-context tiers and Codex fast-mode multipliers. The app checks for a newer table every 24 hours; you can turn this off, and it falls back to the built-in table offline.
- **Real output speed.** Output tokens ÷ the whole request time, including time to first token and relay latency. Only tools whose logs record that duration get a speed, and each is measured on its own, never averaged with the others. It shows how fast things feel in use, not a benchmark.
- **Relay usage.** When Claude Code or Codex talks to a third-party endpoint, that usage appears on a separate "第三方" (third-party) page, attributed to the exact endpoint, even if you switch providers with CC Switch.
- **Custom providers.** Any GET endpoint that returns JSON (a relay dashboard, your own gateway) can be added with a short JSON definition and gets the same quota meters as built-in providers.
- **Four sizes.** A full panel plus large, medium and small compact views that sit on the desktop. Right-click to switch; every size can be resized.
- **Adjustable look.** Background colour (colour picker with 10 presets), transparency, a background image, and UI zoom (Ctrl + mouse wheel).
- **CLI.** `codeusage` shares the app's settings, keys and caches, and supports `--json` for scripts and status bars.
- **Your data stays on your PC.** No browser cookies, no conversation content, no telemetry.

## Screenshots

One window, four sizes. Switch with the size button at the top, a right-click anywhere in the window, or the tray menu.

<img src="./docs/readme-hero.png" width="820" alt="Four sizes: full, large, small and medium" />

Light theme: full panel on the left, large in the center, small at the top right, medium at the bottom right.

## Supported providers

| Provider | Connects with | Quotas shown | Local usage | Output speed |
| --- | --- | --- | :---: | :---: |
| [Codex](./docs/providers/codex.md) | Your existing Codex CLI or app sign-in | Windows the account reports (e.g. 5-hour, weekly), reset credits | ✓ | ✓ |
| [Claude](./docs/providers/claude.md) | Your existing Claude Code sign-in | 5-hour, weekly and per-model quotas | ✓ | ✓ |
| [Cursor](./docs/providers/cursor.md) | The session saved by the Cursor editor | Plan total, Auto, API; plus the Grok Bot weekly quota when the plan includes one | — | — |
| [Antigravity](./docs/providers/antigravity.md) | The running desktop app, with the saved sign-in as a fallback | Period and per-model quotas | ✓ | — |
| [DeepSeek](./docs/providers/deepseek.md) | API key (or `DEEPSEEK_API_KEY`) | API account balance per currency | — | — |
| [Grok](./docs/providers/grok.md) | Your existing Grok Build CLI sign-in (`grok login`) | Subscription allowance for the billing period | ✓ | ✓ |
| [GitHub Copilot](./docs/providers/copilot.md) | GitHub device login, or an official client's saved authorisation | Monthly premium requests and chat | ✓ | — |
| [Kimi Code](./docs/providers/kimi.md) | API key, China or international | 5-hour, weekly, monthly | ✓ | — |
| [OpenCode Go](./docs/providers/opencode.md) | API key | 5-hour, weekly, monthly | ✓ | ✓ |
| [ZCode (Zhipu / Z.ai)](./docs/providers/zcode.md) | API key (GLM Coding Plan), China or international | 5-hour, weekly, MCP tool calls | ✓ | ✓ |
| [Pi](./docs/providers/pi.md) | No sign-in needed | No account quota; local usage only | ✓ | — |
| [Custom](./docs/providers/custom.md) | Any GET endpoint that returns JSON; the key is stored encrypted | Up to 6 quota windows, or a balance | — | — |

- The first six providers are enabled by default; turn on the rest in Settings → 显示的平台 (providers).
- Data sources, connection steps and limits for each provider are in [`docs/providers/`](./docs/providers/) (Chinese).

## Install

Requires Windows 10 or 11 (x64). The .NET Framework 4.8 that ships with Windows is enough; no Node.js. None of the default install methods need admin rights.

### Installer (recommended)

Download and run [codeusagemonit-setup-1.2.0.exe](https://github.com/fanchengliu/codeusagemonit/releases/download/v1.2.0/codeusagemonit-setup-1.2.0.exe) from [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest).

- Installs to `%LOCALAPPDATA%\Programs\codeusagemonit` by default; you can choose another folder or drive.
- Adds a Start menu shortcut. A desktop shortcut, start with Windows, and adding `codeusage` to your user PATH are optional.
- To update, run the new installer; settings and keys are kept. To uninstall, use Windows Settings → Apps, which asks before deleting your settings.

### Scoop

```powershell
scoop bucket add codeusagemonit https://github.com/fanchengliu/codeusagemonit
scoop install codeusagemonit
```

You get a Start menu shortcut and `codeusage` on your PATH. Update with `scoop update codeusagemonit`; the `data` folder lives in Scoop's persist directory, so updates keep it. No Scoop yet? Run `irm get.scoop.sh | iex` first.

### PowerShell one-liner

```powershell
irm https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/install.ps1 | iex
```

The script downloads the latest release from GitHub, verifies file integrity automatically, installs to `%LOCALAPPDATA%\Programs\codeusagemonit`, adds `codeusage` to your user PATH and creates a Start menu shortcut. Run it again to update. To uninstall, delete that folder and remove it from your user PATH.

### Portable zip

Download `codeusagemonit-<version>-win-x64.zip` from [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest), extract it to any writable folder and run `codeusagemonit.exe`. To update, overwrite the old files and keep the `data` folder beside them.

> [!NOTE]
> The app is not code-signed, so Windows may show "Windows protected your PC" on first run. Click **More info → Run anyway**.

## Getting started

1. The app lives in the tray. **Left-click** the icon to open or hide the window; **right-click** for the menu (refresh, settings, size, quit). Closing the window does not quit the app.
2. Providers that aren't connected show a connect button on their card. Codex, Claude and Grok sign in from a terminal; Cursor and Antigravity sign in inside their own apps; for API-key providers, paste the key into the card; Copilot uses GitHub device login.
3. Data refreshes every 5 minutes. Press F5 to refresh everything, or use the refresh button on a card to refresh just that provider.

To look around first, run `codeusagemonit.exe --demo`. It uses demo data, reads no accounts, makes no network requests and leaves your real settings alone.

For everything else (period picker, compact-size controls, settings, the custom-provider JSON format, how relay usage is attributed), see the [user guide](./使用说明.md) (Chinese).

## CLI

`codeusage.exe` sits next to the desktop app and shares its settings, keys and caches.

```powershell
codeusage                        # quotas and local usage from the app's cache (offline)
codeusage usage -p codex,claude  # query quotas live for the given providers
codeusage cost --days 30         # daily cost and tokens; the last line lists output speed for tools that record timing
codeusage thirdparty             # usage through third-party endpoints (relays)
codeusage providers              # providers, whether they're enabled, and where credentials come from
codeusage status --json          # JSON output for scripts or a status bar
```

| Option | Meaning |
| --- | --- |
| `-p, --provider <id>` | Only these providers; comma-separated |
| `--all` | Include providers that aren't enabled |
| `--days <n>` | Days for `cost`, 1–30, default 7 |
| `--refresh` | Rescan local logs before `cost` |
| `--json` | Output JSON |
| `--no-color` | No colours (`NO_COLOR` is honoured too) |

Exit codes: `0` success, `1` every live query failed, `2` bad arguments. Run `codeusage help` for the full usage. CLI output is in Chinese; `--json` keys are in English.

## Data and privacy

**Where data is stored**

| How you run it | Settings, keys and caches |
| --- | --- |
| Installer (setup.exe) | `%LOCALAPPDATA%\codeusagemonit` |
| Portable zip, PowerShell script | the `data\` folder next to the exe |
| Scoop | `data\` next to the exe (kept in Scoop's persist directory) |

A `portable.txt` file next to the exe forces the `data\` folder beside it. The `CODEUSAGEMONIT_DATA` environment variable points to another folder.

**What is stored**

- `settings.json` (settings), `quota-cache.json` (last quota readings, including account emails), `history.json` (daily usage), `<provider>-logs.json` (token counts by hour and model), and a few others.
- API keys you enter are encrypted with Windows DPAPI into `*.key` files. Only your Windows user can decrypt them; they don't work on another PC or user account. An environment variable with the same name takes precedence over a saved key.
- Only token counts, request counts, durations and model names are recorded. **No conversation content or code is saved.**

**What it connects to**

- Each provider's own quota endpoint (for a custom provider, the URL you configured).
- Once a day, the public `pricing.json` from this repository. No account or usage data is sent, and you can turn it off in settings.
- No server of our own, no telemetry.

**What it does not do**

- It does not read browser cookies.
- It does not copy other tools' sign-in tokens into its own config or logs; they stay in each tool's own folder.

The `data` folder contains account emails and encrypted keys. Don't share it publicly.

## FAQ

<details>
<summary><b>Is the cost a real bill?</b></summary>

No. Cost = tokens of each request in your local logs × that model's official API price: what the same usage would cost on API billing. Subscriptions such as Pro are billed monthly and don't produce this bill.

</details>

<details>
<summary><b>Do I have to sign in to every account again?</b></summary>

No. Codex, Claude Code, Cursor, Antigravity and Grok reuse the sign-in already on your PC; DeepSeek, Kimi, OpenCode and ZCode take an API key; Copilot uses GitHub device login.

</details>

<details>
<summary><b>Why does the cost differ from CC Switch or ccusage?</b></summary>

Token counts match; the pricing rules differ. For example, requests with more than 272K input tokens are billed at the long-context rate, and some new models are missing from other price tables and counted as $0. The line-by-line comparison is in the [user guide](./使用说明.md#为什么和-cc-switch-的数字不一样应该信哪个) (Chinese).

</details>

<details>
<summary><b>How often is the price table updated? Can I edit prices?</b></summary>

GitHub Actions rebuilds `pricing.json` in this repository every day from LiteLLM and models.dev. The app checks every 24 hours, validates a newer table, saves it to `data/pricing.json` and recalculates. To set your own prices, turn off daily price sync in settings, delete `data/pricing.json`, edit `pricing.json` in the app folder (USD per million tokens), and restart.

</details>

<details>
<summary><b>How is output speed measured? Why do some providers have none?</b></summary>

Output tokens (including thinking) ÷ the time from sending the request to its last output being written to the log, token-weighted across requests. Only requests with at least 50 output tokens and 0.2 s to 10 min duration count. Antigravity, Kimi, Pi and Copilot don't record request durations locally, so they show no speed.

</details>

<details>
<summary><b>I need a proxy.</b></summary>

Settings → 数据与网络 (data & network) → proxy. The default, automatic, uses the Windows system proxy and then the `HTTPS_PROXY` / `HTTP_PROXY` environment variables. You can also choose direct or enter your own. The proxy applies to this app only and doesn't change system settings.

</details>

<details>
<summary><b>Is there an English UI or a macOS version?</b></summary>

Not yet: the UI is Simplified Chinese and the app is Windows-only. On macOS, try [CodexBar](https://github.com/steipete/CodexBar), which inspired this project's interface.

</details>

## Build from source

The build uses only the .NET Framework compiler (csc) and WPF that ship with Windows. No Visual Studio, Node.js, Rust or Python. Quit the running app first.

```powershell
git clone https://github.com/fanchengliu/codeusagemonit.git
cd codeusagemonit
.\source\Windows\build.ps1               # builds codeusagemonit.exe and codeusage.exe in the repo root
.\codeusagemonit.exe --self-test         # runs the built-in tests
.\source\Windows\build.ps1 -Installer    # also builds the installer and the portable zip
```

`-Installer` needs [Inno Setup 6.3 or newer](https://jrsoftware.org/isdl.php); the Simplified Chinese language file is included in the repository. See [`source/Windows/installer/README.md`](./source/Windows/installer/README.md).

All source is in [`source/Windows/`](./source/Windows/): `LocalLogs.cs`, `AgentLogs.cs` and `MoreAgentLogs.cs` read local logs, `Pricing.cs` does the pricing, `Presentation.cs` and `Compact.cs` are the UI, and `Cli.cs` is the CLI.

## Acknowledgements

- [CodexBar](https://github.com/steipete/CodexBar): the macOS counterpart that inspired this project's interface; the provider icons come from its resources (MIT).
- [ccusage](https://github.com/ccusage/ccusage): a reference for how each tool's local logs are parsed.
- [Claude Code Usage Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor): a reference for where Windows clients keep their sign-in state.
- [LiteLLM](https://github.com/BerriAI/litellm) and [models.dev](https://github.com/anomalyco/models.dev): sources of the price data (MIT).
- [Inno Setup](https://jrsoftware.org/isinfo.php): the installer; the Simplified Chinese translation is maintained by [kira-96](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation).

This project contains no code from these projects and does not call their programs. See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).

## License

[MIT](./LICENSE)

codeusagemonit is an independent open-source project, not affiliated with OpenAI, Anthropic, Cursor or any other monitored service. Product names and logos belong to their owners.
