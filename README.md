<div align="center">

<img src="./docs/favicon.svg" width="96" height="96" alt="codeusagemonit logo" />

# codeusagemonit

**专为 Windows 打造的轻量级 AI 编程助手用量监控与 CLI 工具**

[![Release](https://img.shields.io/github/v/release/fanchengliu/codeusagemonit?style=flat-square&color=38bdf8)](https://github.com/fanchengliu/codeusagemonit/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-blue?style=flat-square)](https://github.com/fanchengliu/codeusagemonit)
[![Binary Size](https://img.shields.io/badge/size-551%20KB-success?style=flat-square)](https://github.com/fanchengliu/codeusagemonit)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](./LICENSE)
[![Website](https://img.shields.io/badge/website-fanchengliu.github.io-818cf8?style=flat-square)](https://fanchengliu.github.io/codeusagemonit/)

[🌐 访问官方展示网站](https://fanchengliu.github.io/codeusagemonit/) · [📖 详细使用说明 (中文)](./使用说明.md) · [⬇ 下载最新版 (v1.1.0)](https://github.com/fanchengliu/codeusagemonit/releases/latest)

</div>

---

## 💡 为什么选择 codeusagemonit？

在 Windows 上进行高强度 AI 编程时，经常面临多个平台的配额窗口（5小时/每周/每月）、重置时刻不同、各家本地日志散落各处、第三方中转站用量无法追踪等痛点。

`codeusagemonit` 是一个独立实现的 Windows 原生桌面托盘程序与配套 CLI 工具：
* **零 Electron 依赖，仅 551 KB**：采用原生 C# 5.0 / WPF 开发，启动冷启时间 < 40ms，内存占用通常低于 45 MB。
* **11+ 主流平台全景覆盖**：官方支持 **Codex、Claude Code、Cursor、Antigravity、GitHub Copilot、DeepSeek、Grok、Kimi Code、OpenCode Go、ZCode、Pi**，并支持自定义 JSON HTTP 网关。
* **四种自适应桌面形态**：同一个程序支持 **完整面板**、**大尺寸小组件**、**中尺寸双栏**、**小号悬浮挂件**，随心所欲放置在桌面或置顶。
* **真实输出速度统计 (t/s)**：端到端计算网络延迟、首字时间及推理耗时的真实输出速度，一眼看清谁在暗中偷降频。
* **361 款模型离线精准计价**：内置来自 LiteLLM 与 models.dev 的完整价目表，精准支持长上下文阶梯分档（>272K 翻倍）与缓存写入折扣，不虚报、不漏算。
* **第三方中转站穿透追踪**：即便在 CC Switch 频繁切换不同供应商，也能精确将每次调用的 Token 和费用归属到具体的服务商或自建站点。
* **极致本地隐私**：不碰浏览器 Cookie，API Key 均使用 Windows 原生 DPAPI 硬件加密，零云端遥测，代码内容永不出网。

---

## 🖥 界面尺寸一览

| 尺寸形态 | 适用场景 | 核心呈现内容 |
| :--- | :--- | :--- |
| **小号挂件 (Small)** | 紧凑贴在桌面角落 | 大号剩余百分比、24 格发光分段进度条、重置倒计时，点击切换额度窗口 (1/2) |
| **中尺寸 (Medium)** | 侧边栏常驻参考 | 左侧展示近 30 天与今日支出；右侧各平台额度条与消耗节奏 |
| **大尺寸 (Large)** | 交互图表与分析 | 时间段选择器 + 该时间段总费用 + 堆叠柱状图 + 详细额度条与测速 |
| **完整面板 (Full)** | 托盘展开全景管理 | 顶部多标签栏、可点开展开的精确重置时间与配额明细、各平台独立刷新按钮 |

---

## ⌨ 命令行工具 (codeusage)

随桌面版附带的 `codeusage.exe` 与桌面端共享同一份 DPAPI 凭证与数据缓存，可直接集成进脚本、终端或状态栏：

```powershell
# 查看各平台实时额度与今日消耗（24格 ASCII 进度条）
codeusage

# 实时查询指定平台额度
codeusage usage -p codex

# 查看近 30 天每日用量汇总与加权输出速度 (t/s)
codeusage cost --days 30

# 查看第三方中转站用量
codeusage thirdparty

# 以 JSON 格式输出，便于脚本或系统状态栏集成
codeusage status --json
```

---

## 🚀 快速上手与运行

### 方式一：下载即用（免安装）
从 [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest) 下载 `codeusagemonit-1.1.0-win-x64.zip`，解压后双击运行 `codeusagemonit.exe` 即可。

### 方式二：PowerShell 一键构建
本项目使用 Windows 自带的 .NET Framework 编译器，**无需安装 Visual Studio、Node.js、Rust 或 Python**：

```powershell
git clone https://github.com/fanchengliu/codeusagemonit.git
cd codeusagemonit
.\source\Windows\build.ps1
```

构建将在几秒内完成，并在根目录生成单文件可执行程序。

---

## 🔒 隐私与安全性设计

1. **零 Cookie 嗅探**：鉴于 Windows 平台上现代 Chrome 已启用 App-Bound Encryption（应用绑定加密），本工具绝不强行非法提取浏览器会话，而是使用官方 OAuth 设备码、已授权本地 CLI 令牌或用户填写的 API Key。
2. **硬件级 DPAPI 加密**：所有填写的密钥均通过 Windows `ProtectedData` (DPAPI) 加密保存在 `data/*.key`，只能由当前登录系统的 Windows 用户解密，跨机器复制无效。
3. **本地离线扫描**：增量只读扫描本地会话文件，仅提取 Token 计数与模型名称，**绝不读取、不上传、不保存任何对话内容或代码上下文**。

---

## 📄 开源许可与致谢

* 本项目采用 [MIT 许可证](./LICENSE) 开源。
* 平台图标取自各大品牌官方 Logo（由 CodexBar 收集整理，MIT 许可，商标归各自公司所有）。
* 详见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
