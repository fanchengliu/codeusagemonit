<div align="center">

**中文** · [English](./README.en.md)

<img src="./docs/favicon.svg" width="96" height="96" alt="codeusagemonit logo" />

# codeusagemonit

**专为 Windows 打造的轻量级 AI 编程助手用量监控与 CLI 工具**

[![Release](https://img.shields.io/github/v/release/fanchengliu/codeusagemonit?style=flat-square&color=38bdf8)](https://github.com/fanchengliu/codeusagemonit/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-blue?style=flat-square)](https://github.com/fanchengliu/codeusagemonit)
[![Binary Size](https://img.shields.io/badge/size-551%20KB-success?style=flat-square)](https://github.com/fanchengliu/codeusagemonit)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](./LICENSE)
[![Website](https://img.shields.io/badge/website-codeusagemonit.vercel.app-818cf8?style=flat-square)](https://codeusagemonit.vercel.app)

[🌐 访问官方展示网站](https://codeusagemonit.vercel.app) · [📖 详细使用说明 (中文)](./使用说明.md) · [⬇ 下载安装程序 (v1.2.0)](https://github.com/fanchengliu/codeusagemonit/releases/download/v1.2.0/codeusagemonit-setup-1.2.0.exe)

</div>

---

## 💡 为什么选择 codeusagemonit？

在 Windows 上进行高强度 AI 编程时，经常面临多个平台的配额窗口（5小时/每周/每月）、重置时刻不同、各家本地日志散落各处、第三方中转站用量无法追踪等痛点。

`codeusagemonit` 是一个独立实现的 Windows 原生桌面托盘程序与配套 CLI 工具：
* **零 Electron 依赖，仅 551 KB**：采用原生 C# 5.0 / WPF 开发，启动冷启时间 < 40ms，内存占用通常低于 45 MB。
* **11+ 主流平台全景覆盖**：官方支持 **Codex、Claude Code、Cursor、Antigravity、GitHub Copilot、DeepSeek、Grok、Kimi Code、OpenCode Go、ZCode、Pi**，并支持自定义 JSON HTTP 网关。Cursor 在套餐总量、Auto、API / 手动模型之外，另显示包含在套餐内的 **Grok Bot 每周额度**（查询失败不影响其余三项，主额度仍是套餐总量）。
* **四种自适应桌面形态**：同一个程序支持 **完整面板**、**大尺寸小组件**、**中尺寸双栏**、**小号悬浮挂件**，随心所欲放置在桌面或置顶。
* **真实输出速度统计 (t/s)**：端到端计算网络延迟、首字时间及推理耗时的真实输出速度，一眼看清谁在暗中偷降频。
* **361 款模型，与官方同步计价**：价目表整理自 LiteLLM 与 models.dev 收录的各厂商官方 API 单价，由 GitHub Actions 每天重建；软件每 24 小时检查一次并自动更新（可在设置中关闭，断网时使用内置价目）。支持长上下文阶梯分档（>272K 翻倍）与缓存写入折扣，不虚报、不漏算。
* **第三方中转站穿透追踪**：即便在 CC Switch 频繁切换不同供应商，也能精确将每次调用的 Token 和费用归属到具体的服务商或自建站点。
* **极致本地隐私**：不碰浏览器 Cookie，API Key 均使用 Windows 原生 DPAPI 硬件加密，零云端遥测，代码内容永不出网（联网只为查询各平台额度，以及每天下载一次公开价目表，可在设置中关闭）。

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

### 方式一：安装程序（推荐）

从 [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest) 下载 **[codeusagemonit-setup-1.2.0.exe](https://github.com/fanchengliu/codeusagemonit/releases/download/v1.2.0/codeusagemonit-setup-1.2.0.exe)**。可以选择安装目录和盘符，默认是 `%LOCALAPPDATA%\Programs\codeusagemonit`，不需要管理员权限。安装程序会创建开始菜单快捷方式，可选桌面快捷方式、登录时启动，以及把 `codeusage` 加入用户 PATH，并在“Windows 设置 → 应用”里注册卸载。卸载前会询问是否删除设置。再运行新的安装程序就是升级，`%LOCALAPPDATA%\codeusagemonit` 里的设置、密钥和缓存会保留。

### 方式二：Scoop（和 Homebrew 的 tap 一样）
```powershell
scoop bucket add codeusagemonit https://github.com/fanchengliu/codeusagemonit
scoop install codeusagemonit
```
开始菜单里会出现 codeusagemonit，终端里可以直接用 `codeusage`；`scoop update codeusagemonit` 升级，`data` 文件夹放在 Scoop 的 persist 目录，升级不丢。

### 方式三：PowerShell 一行安装
```powershell
irm https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/install.ps1 | iex
```
下载最新 Release 并核对 SHA-256，装到 `%LOCALAPPDATA%\Programs\codeusagemonit`，把 `codeusage` 加进用户 PATH，创建开始菜单快捷方式；不需要管理员权限，再运行一次就是升级。

### 方式四：下载即用（免安装）
从 [Releases](https://github.com/fanchengliu/codeusagemonit/releases/latest) 下载 `codeusagemonit-1.2.0-win-x64.zip`，解压后双击运行 `codeusagemonit.exe` 即可。

### 方式五：从源码构建
本项目使用 Windows 自带的 .NET Framework 编译器，**无需安装 Visual Studio、Node.js、Rust 或 Python**：

```powershell
git clone https://github.com/fanchengliu/codeusagemonit.git
cd codeusagemonit
.\source\Windows\build.ps1
.\source\Windows\build.ps1 -Installer
```

不带参数时，构建在几秒内完成，并在根目录生成可执行程序。`-Installer` 还需要 [Inno Setup 6.3 或更新版本](https://jrsoftware.org/isdl.php)（简体中文语言文件已放在仓库里，不用再往 Inno Setup 的 Languages 目录里复制），会额外生成 `codeusagemonit-setup-<版本>.exe` 和免安装 zip。说明见 [source/Windows/installer/README.md](./source/Windows/installer/README.md)。

---

## 🔒 隐私与安全性设计

1. **零 Cookie 嗅探**：鉴于 Windows 平台上现代 Chrome 已启用 App-Bound Encryption（应用绑定加密），本工具绝不强行非法提取浏览器会话，而是使用官方 OAuth 设备码、已授权本地 CLI 令牌或用户填写的 API Key。
2. **硬件级 DPAPI 加密**：所有填写的密钥均通过 Windows `ProtectedData` (DPAPI) 加密保存在数据目录的 `*.key`，只能由当前登录系统的 Windows 用户解密，跨机器复制无效。安装程序把数据放在 `%LOCALAPPDATA%\codeusagemonit`；zip / Scoop / PowerShell 安装仍使用程序旁边的 `data` 文件夹。
3. **本地离线扫描**：增量只读扫描本地会话文件，仅提取 Token 计数与模型名称，**绝不读取、不上传、不保存任何对话内容或代码上下文**。

---

## 📄 开源许可与致谢

* 本项目采用 [MIT 许可证](./LICENSE) 开源。
* 平台图标取自各大品牌官方 Logo（由 CodexBar 收集整理，MIT 许可，商标归各自公司所有）。
* 详见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
