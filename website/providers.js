window.PROVIDERS = [
  {
    "id": "codex",
    "name": "Codex",
    "authLabel": {
      "zh": "OAuth",
      "en": "OAuth",
      "ja": "OAuth"
    },
    "capabilities": {
      "zh": "周期额度、重置时间与本机用量",
      "en": "Quota windows, resets and local usage",
      "ja": "利用枠・リセット時刻・ローカル使用量"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/codex.md"
  },
  {
    "id": "claude",
    "name": "Claude",
    "authLabel": {
      "zh": "OAuth",
      "en": "OAuth",
      "ja": "OAuth"
    },
    "capabilities": {
      "zh": "5 小时 / 每周额度与本机用量",
      "en": "5-hour / weekly quotas and local usage",
      "ja": "5時間・週間利用枠とローカル使用量"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/claude.md"
  },
  {
    "id": "cursor",
    "name": "Cursor",
    "authLabel": {
      "zh": "本地会话 · Cookie",
      "en": "Local session · Cookie",
      "ja": "ローカルセッション · Cookie"
    },
    "capabilities": {
      "zh": "套餐、Auto 与 API 额度",
      "en": "Plan, Auto and API quotas",
      "ja": "プラン・Auto・API の利用枠"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/cursor.md"
  },
  {
    "id": "antigravity",
    "name": "Antigravity",
    "authLabel": {
      "zh": "本地服务 · OAuth",
      "en": "Local service · OAuth",
      "ja": "ローカルサービス · OAuth"
    },
    "capabilities": {
      "zh": "模型额度与本机会话统计",
      "en": "Model quotas and local session usage",
      "ja": "モデル別利用枠とローカル使用量"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/antigravity.md"
  },
  {
    "id": "deepseek",
    "name": "DeepSeek",
    "authLabel": {
      "zh": "API Key",
      "en": "API key",
      "ja": "API キー"
    },
    "capabilities": {
      "zh": "按币种展示 API 账户余额",
      "en": "API account balances by currency",
      "ja": "通貨別 API アカウント残高"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/deepseek.md"
  },
  {
    "id": "grok",
    "name": "Grok",
    "authLabel": {
      "zh": "CLI 会话",
      "en": "CLI session",
      "ja": "CLI セッション"
    },
    "capabilities": {
      "zh": "订阅账期额度与本机用量",
      "en": "Billing-period quota and local usage",
      "ja": "請求期間の利用枠とローカル使用量"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/grok.md"
  },
  {
    "id": "copilot",
    "name": "Copilot",
    "authLabel": {
      "zh": "OAuth 设备流",
      "en": "OAuth device flow",
      "ja": "OAuth デバイスフロー"
    },
    "capabilities": {
      "zh": "高级请求、对话等每月额度",
      "en": "Monthly premium requests and chat quota",
      "ja": "プレミアムリクエストなどの月間利用枠"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/copilot.md"
  },
  {
    "id": "kimi",
    "name": "Kimi",
    "authLabel": {
      "zh": "API Key",
      "en": "API key",
      "ja": "API キー"
    },
    "capabilities": {
      "zh": "Kimi Code 周期额度与本机用量",
      "en": "Kimi Code quotas and local usage",
      "ja": "Kimi Code の利用枠とローカル使用量"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/kimi.md"
  },
  {
    "id": "opencode",
    "name": "OpenCode",
    "authLabel": {
      "zh": "API Key",
      "en": "API key",
      "ja": "API キー"
    },
    "capabilities": {
      "zh": "OpenCode Go 额度与本机历史",
      "en": "OpenCode Go quotas and local history",
      "ja": "OpenCode Go の利用枠とローカル履歴"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/opencode.md"
  },
  {
    "id": "zcode",
    "name": "ZCode",
    "authLabel": {
      "zh": "API Key",
      "en": "API key",
      "ja": "API キー"
    },
    "capabilities": {
      "zh": "GLM 编码套餐额度与本机用量",
      "en": "GLM coding-plan quotas and local usage",
      "ja": "GLM Coding Plan の利用枠とローカル使用量"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/zcode.md"
  },
  {
    "id": "pi",
    "name": "Pi",
    "authLabel": {
      "zh": "本地文件",
      "en": "Local files",
      "ja": "ローカルファイル"
    },
    "capabilities": {
      "zh": "本机 Token、请求与费用估算",
      "en": "Local tokens, requests and cost estimates",
      "ja": "ローカルのトークン数・リクエスト数・推定費用"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/pi.md"
  },
  {
    "id": "custom",
    "name": "自定义接口",
    "authLabel": {
      "zh": "API Key · 自定义",
      "en": "API key · Custom",
      "ja": "API キー · カスタム"
    },
    "capabilities": {
      "zh": "将 JSON 接口映射为额度或余额",
      "en": "Map a JSON endpoint to quotas or balance",
      "ja": "JSON API を利用枠・残高にマッピング"
    },
    "docsUrl": "https://github.com/fanchengliu/codeusagemonit/blob/main/docs/providers/custom.md"
  }
]
;