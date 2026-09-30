# Third-party notices

codeusagemonit is an independent Windows application. It is not an official release of CodexBar or ccusage and is not affiliated with any monitored service. It contains no code or binaries from the projects below. The only files taken from another project are the provider logo SVGs in `icons/` (from CodexBar, see below); the other projects are credited because their public source was studied as a reference, or because data derived from them ships with the application.

## Data that ships with the application

### LiteLLM model price catalog
- Source: https://github.com/BerriAI/litellm — `model_prices_and_context_window.json`
- License: MIT (the catalog lives outside the repository's `enterprise/` directory). Full text: `licenses/LiteLLM-MIT.txt`.
- Use: `pricing.json` (USD per million tokens) is compiled from this catalog for the API-equivalent cost estimates.

### models.dev
- Source: https://github.com/anomalyco/models.dev
- License: MIT. Full text: `licenses/models.dev-MIT.txt`.
- Use: long-context price tiers (e.g. whole-request rates above 272K input tokens) and a few models the LiteLLM catalog does not list yet, compiled into `pricing.json`.

Prices are the vendors' published list prices as recorded by these catalogs. The estimates are not bills.

## Provider logos

The files in `icons/` are the logos of the monitored products, used unmodified only to identify each provider. They are taken from CodexBar (`Sources/CodexBar/Resources/ProviderIcon-<id>.svg`, MIT, Copyright (c) 2026 Peter Steinberger, license text in `licenses/CodexBar-MIT.txt`; `zcode.svg` is `ProviderIcon-zai.svg`). The logos and product names are trademarks of their respective owners; their use does not imply endorsement.

## Projects studied as references (no code included)

### CodexBar
- https://github.com/steipete/CodexBar — MIT, Copyright (c) 2026 Peter Steinberger. License text kept in `licenses/CodexBar-MIT.txt`.
- The Windows application re-implements, in its own C#, the provider-overview idea and the published behaviour of the providers' quota endpoints (Codex windows and reset credits, Claude utilization, Antigravity local endpoints, DeepSeek balances). Apart from the provider logos above, no CodexBar file is included.

### Claude Code Usage Monitor (CodeZeno)
- https://github.com/CodeZeno/Claude-Code-Usage-Monitor — MIT. License text kept in `licenses/CodeZeno-MIT.txt`.
- Studied for where Windows clients keep their sign-in state (Cursor `state.vscdb`, Antigravity Credential Manager target, Grok CLI session headers).

### ccusage
- https://github.com/ccusage/ccusage — studied for the local log formats of Claude Code, Codex, Antigravity, ZCode, Grok, Kimi, Pi, GitHub Copilot CLI and OpenCode, and their de-duplication rules. codeusagemonit reads those logs with its own engine (`LocalLogs.cs`, `AgentLogs.cs`, `MoreAgentLogs.cs`, `Pricing.cs`); ccusage is neither bundled nor called.

Service names and brand names belong to their respective owners. No proprietary API credentials are bundled.
