# Devin

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.1。连接方式：**本地服务 · 本机缓存**。

Devin Desktop 是 Windsurf 改名后的桌面应用，套餐从积分制改为每日 / 每周额度。本程序只读本机数据，使用 Devin Desktop 自己的登录。

## 数据来源

按顺序尝试：

1. **本地服务**：当前 Windows 用户正在运行的 Devin / Windsurf 语言服务（路径或命令行含 devin 或 windsurf，排除 Antigravity），通过回环地址调用 `GetUserStatus`，读取 `planStatus` 中的每日 / 每周剩余百分比、重置时间和套餐名。
2. **本机缓存**：`%APPDATA%\Devin` 或 `%APPDATA%\Windsurf` 下 `User\globalStorage\state.vscdb` 中的 `windsurf.settings.cachedPlanInfo`（UTF-8 或 UTF-16 均可）。这份缓存只在应用启动时更新，套餐名后会标注「本机缓存」。

不使用 app.devin.ai / windsurf.com 的网页接口：它需要浏览器会话令牌。

## 连接步骤

1. 在 设置 → 显示的平台 里点亮 Devin（新加的平台默认不启用）。
2. 打开 Devin Desktop 并登录，保持应用运行。
3. 在 Devin 卡片点击「我已登录，刷新」；若应用未打开，可以点击「打开 Devin」。

## 可以查看什么

- 每日、每周额度的剩余比例与重置时间。
- 旧的积分制套餐（缓存里没有每日 / 每周额度时）：本账期消息数、Flow 操作数的已用比例。

## 限制

- 字段名依据 CodexBar 的 Windsurf 实现（MIT）和公开说明，尚未在 Windows 上的真实 Devin 账户核对，欢迎在 [issue #16](https://github.com/fanchengliu/codeusagemonit/issues/16) 反馈。
- 缺少剩余百分比时不当作 0% 或 100%，该窗口不显示。
- 缓存里已经过了重置时间的窗口不再显示（它说明不了当前窗口）；全部过期时提示打开 Devin Desktop 后刷新。
- 暂无本机 Token 用量统计，也不显示输出速度。

## 隐私

仅探测当前 Windows 用户拥有的本地服务，只读打开 `state.vscdb`。不存储密码，不读取浏览器 Cookie 或会话令牌，不访问 Devin 的网页接口。

## 实现位置

`source/Windows/`：`LocalDevin.cs`、`LocalAntigravity.cs`（语言服务探测）、`Core.cs`（Devin 分支、`NativeCredentials.SqliteBytes`）。本文依据该版本代码核对；客户端格式变化时可能需要更新。
