# codeusagemonit __VERSION__

Codex、Claude、Cursor 等 11 个 AI 编程工具的剩余额度、重置时间、Token 用量和输出速度，放在 Windows 托盘的一个小窗口里。

## 主要功能

- **额度一目了然**：每个额度窗口一条 24 格进度条，提示当前节奏和预计用尽时间；完整面板之外还有贴在桌面上的大、中、小三种尺寸。
- **本机用量与费用**：读取各工具的本机会话记录，按小时、按模型统计 Token、请求数和输出速度，用官方 API 单价逐次估算费用；Cursor 用量取自账户明细，DeepSeek 读 DeepSeek Harness。
- **中转站看板**：Claude Code / Codex 经第三方接口的用量归到具体站点，按时间段对比用量、费用、请求和同一模型的速度。
- **正在使用的平台更快刷新**，也可以给每个平台单独设置刷新间隔。
- **多设备与备份**：SQL 导入导出（自动去重）、WebDAV 多设备同步、自动备份与一键恢复。
- **中文 / English 界面**，设置分为通用、认证、数据、高级、关于，可检查更新。
- 数据只在本机统计，不保存对话内容；各工具的登录令牌留在原处只读不复制。

## 下载

- **codeusagemonit-setup-__VERSION__.exe** — Windows 安装程序。可选安装目录（默认 `%LOCALAPPDATA%\Programs\codeusagemonit`，无需管理员），开始菜单快捷方式，可选桌面快捷方式、登录时启动，以及把 `codeusage` 加入用户 PATH。在“Windows 设置 → 应用”里卸载；卸载前会询问是否删除设置。升级会保留 `%LOCALAPPDATA%\codeusagemonit` 里的数据。
- **codeusagemonit-__VERSION__-win-x64.zip** — 免安装。解压到可写目录后运行 `codeusagemonit.exe`，数据在旁边的 `data` 文件夹。

程序未做代码签名，SmartScreen 提示时选“更多信息 → 仍要运行”。
