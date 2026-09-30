<div align="center">

[中文](./README.md) · **English**

<img src="./docs/favicon.svg" width="96" height="96" alt="codeusagemonit logo" />

# codeusagemonit

**A lightweight Windows tray app and CLI for AI coding-assistant usage**

[![Release](https://img.shields.io/github/v/release/fanchengliu/codeusagemonit?style=flat-square&color=38bdf8)](https://github.com/fanchengliu/codeusagemonit/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-blue?style=flat-square)](https://github.com/fanchengliu/codeusagemonit)
[![Binary Size](https://img.shields.io/badge/size-551%20KB-success?style=flat-square)](https://github.com/fanchengliu/codeusagemonit)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](./LICENSE)
[![Website](https://img.shields.io/badge/website-codeusagemonit.vercel.app-818cf8?style=flat-square)](https://codeusagemonit.vercel.app)

[🌐 Website](https://codeusagemonit.vercel.app) · [📖 Detailed guide (Chinese)](./使用说明.md) · [⬇ Installer (v1.2.0)](https://github.com/fanchengliu/codeusagemonit/releases/download/v1.2.0/codeusagemonit-setup-1.2.0.exe)

</div>

---

## 💡 Why codeusagemonit?

Heavy AI coding on Windows means several quota windows (5-hour / weekly / monthly), different reset times, logs scattered across each vendor’s folders, and third-party relays whose usage never shows up.

`codeusagemonit` is an independent Windows tray app and matching CLI:

* **No Electron, 551 KB**: native C# 5.0 / WPF. Cold start is under 40 ms, and memory use is usually under 45 MB.
* **11 tools, plus your own endpoint**: **Codex, Claude Code, Cursor, Antigravity, GitHub Copilot, DeepSeek, Grok, Kimi Code, OpenCode Go, ZCode, and Pi**, and a custom JSON HTTP gateway. Cursor shows the included **Grok Bot weekly quota** under the plan total, Auto, and API / manual-model meters (a failed Grok Bot query does not hide the other three; the plan total stays the main meter).
* **Four sizes, one window**: **full panel**, **large widget**, **medium two-column**, and **small floater**. Park it on the desktop or keep it always on top.
* **Real output speed (t/s)**: end-to-end time, including network delay, time to first token, and generation, so a quiet throttle is visible.
* **361 models, priced with the vendors**: the table is built from the official API prices collected by LiteLLM and models.dev, and GitHub Actions rebuilds it every day. The app checks once every 24 hours and updates itself (turn this off in settings; offline it uses the built-in table). Long-context tiers (above 272K the price doubles) and cache-write discounts are included.
* **Relays stay attributed**: even if CC Switch moves you between providers, each call’s tokens and cost stay with that provider or self-hosted endpoint.
* **Local by default**: no browser cookies. API keys are encrypted with Windows DPAPI. There is no telemetry, and conversation text never leaves the machine. The network is used only to query each provider’s quota and, once a day, to download the public price table (that download can be turned off).

---

## 🖥 Window sizes

| Size | Where it fits | What it shows |
| :--- | :--- | :--- |
| **Small** | A corner of the desktop | Large percent remaining, a 24-cell meter, reset countdown. Click to switch quota windows (1/2). |
| **Medium** | A sidebar you leave open | Last 30 days and today on the left; each provider’s meter and pace on the right. |
| **Large** | Charts you want to poke | Period picker, cost for that period, stacked bars, detailed meters, and speed. |
| **Full** | The tray’s full view | Tab bar, expandable exact reset times and quota detail, and a refresh button on every provider. |

---

## ⌨ Command line (`codeusage`)

`codeusage.exe` ships with the desktop app and shares its DPAPI credentials and cache, so scripts, a terminal, or a status bar can read the same numbers:

```powershell
# Quotas and today’s usage (24-cell ASCII meters)
codeusage

# One provider, queried live
codeusage usage -p codex

# Daily totals for the last 30 days, plus weighted output speed (t/s)
codeusage cost --days 30

# Third-party relay usage
codeusage thirdparty

# JSON for a script or a status bar
codeusage status --json
```

---

## 🚀 Install and run

### 1. Installer (recommended)

Download **[codeusagemonit-setup-1.2.0.exe](https://github.com/fanchengliu/codeusagemonit/releases/download/v1.2.0/codeusagemonit-setup-1.2.0.exe)** from [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest). You can pick the folder and the drive. The default is `%LOCALAPPDATA%\Programs\codeusagemonit`, and it does not need administrator rights. The installer adds a Start menu shortcut, and can add a desktop shortcut, start at sign-in, and put `codeusage` on your user PATH. It registers an uninstaller under Windows Settings → Apps. Uninstall asks before deleting settings. Running a newer installer is the upgrade; settings, keys, and cache in `%LOCALAPPDATA%\codeusagemonit` stay.

### 2. Scoop (same idea as a Homebrew tap)

```powershell
scoop bucket add codeusagemonit https://github.com/fanchengliu/codeusagemonit
scoop install codeusagemonit
```

codeusagemonit shows up in the Start menu, and `codeusage` works in a terminal. `scoop update codeusagemonit` upgrades it. The `data` folder lives in Scoop’s persist directory, so an update keeps it.

### 3. PowerShell one-liner

```powershell
irm https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/install.ps1 | iex
```

This downloads the latest release, checks the SHA-256, installs to `%LOCALAPPDATA%\Programs\codeusagemonit`, adds `codeusage` to your user PATH, and creates a Start menu shortcut. No administrator rights. Run it again to upgrade.

### 4. Portable zip

Download `codeusagemonit-1.2.0-win-x64.zip` from [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest), extract it, and run `codeusagemonit.exe`.

### 5. Build from source

The project uses the .NET Framework compiler that ships with Windows. **Visual Studio, Node.js, Rust, and Python are not required**:

```powershell
git clone https://github.com/fanchengliu/codeusagemonit.git
cd codeusagemonit
.\source\Windows\build.ps1
.\source\Windows\build.ps1 -Installer
```

Without arguments the build finishes in a few seconds and writes the executables in the repo root. `-Installer` also needs [Inno Setup 6.3 or newer](https://jrsoftware.org/isdl.php) (the Simplified Chinese language file is already in the repo; do not copy it into Inno Setup’s Languages folder) and writes `codeusagemonit-setup-<version>.exe` plus the portable zip. See [source/Windows/installer/README.md](./source/Windows/installer/README.md).

---

## 🔒 Privacy

1. **No cookie scraping.** Current Chrome on Windows uses app-bound encryption, and this tool does not try to pull a browser session. It uses official OAuth device codes, a local CLI token you already authorized, or an API key you paste.
2. **DPAPI.** Keys you enter are stored as `*.key` files under the data directory, encrypted with Windows `ProtectedData` (DPAPI). Only the Windows user who saved them can decrypt them; copying the files to another machine does not. The installer keeps data in `%LOCALAPPDATA%\codeusagemonit`. The zip, Scoop, and PowerShell installs keep a `data` folder next to the program.
3. **Local, read-only scans.** Session files are scanned incrementally and read-only. Only token counts and model names are kept. **Conversation text and code context are never read, uploaded, or stored.**

---

## 📄 License and credits

* [MIT License](./LICENSE).
* Provider icons are the vendors’ own logos, collected by CodexBar (MIT). Trademarks belong to their owners.
* See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).
