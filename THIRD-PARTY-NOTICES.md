# Third-party notices

## Pane

- Upstream: https://github.com/ItsJazii/pane
- Reference commit: `51877a421eb5489b629220a72df387aea048526d`; MIT license. Full text: `licenses/Pane-MIT.txt`.
- Referenced Cursor Grok Bot DashboardService usage fields and independent allowance handling. The adapter is implemented in C#; no Pane executable is bundled.

codeusagemonit is an independent Windows adaptation. It is not an official release of CodexBar and is not affiliated with any monitored service.

## CodexBar

- Upstream: https://github.com/steipete/CodexBar
- Baseline commit: `25bba9b7fd9ce83c33053958f7366e23b2dc8a82`
- Copyright (c) 2026 Peter Steinberger; MIT license.
- Reused material: provider SVG icons; the unified provider-overview interaction; ported Codex quota-window and optional-limit mappings, plan formatting, read-only reset-credit requests, pace calculations, Claude utilization mappings, Antigravity local endpoint contracts, and DeepSeek per-currency balance semantics.
- This repository contains the independent Windows implementation in `source/Windows`, using the Windows-supplied .NET Framework / WPF libraries. The upstream Swift/AppKit source is available at the baseline commit linked above.
- Full license: `licenses/CodexBar-MIT.txt` in the distribution.

## Claude Code Usage Monitor

- Upstream: https://github.com/CodeZeno/Claude-Code-Usage-Monitor
- Reference version: `v2.15.22`; MIT license.
- Adapted Windows provider discovery and response handling: Cursor's read-only `state.vscdb` token lookup and session-cookie construction; the exact Antigravity Credential Manager target and quota endpoints; Grok's allowlisted xAI CLI session issuer and billing headers.
- This application does not launch or depend on the CodeZeno monitor.
- Full license: `licenses/CodeZeno-MIT.txt` in the distribution.

## ccusage

- Upstream: https://github.com/ccusage/ccusage
- Bundled version: `20.0.26`, Windows x64 native executable; MIT license.
- Purpose: read local usage records and return 30-day daily token and API-equivalent cost estimates.
- Invoked with offline pricing and JSON output. This estimate is not a subscription bill or quota balance.
- Full license: `licenses/ccusage-MIT.txt` in the distribution.

Service names and brand marks belong to their respective owners. No proprietary API credentials are bundled.
