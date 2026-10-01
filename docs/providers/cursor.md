# Cursor

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.3.0。连接方式：**本地会话 · Cookie**。

## 数据来源

读取 `%APPDATA%/Cursor/User/globalStorage/state.vscdb` 中的 `cursorAuth/accessToken` 和缓存邮箱。根据这个编辑器会话生成 `WorkosCursorSessionToken` Cookie 请求头，向 `cursor.com/api/usage-summary` 查询套餐总量、Auto、API / 手动模型。同一会话再 `POST cursor.com/api/dashboard/get-sand-usage-status`（`Origin: https://cursor.com`）读取 **Grok Bot** 的每周额度；该请求失败或没有包含额度时，其余三项照常显示。

用量明细：同一会话分页 `POST cursor.com/api/dashboard/get-filtered-usage-events`（每页 100 条，最近 31 天），每条调用带时间、模型、输入 / 输出 / 缓存读取 / 缓存写入 Token 和 Cursor 给出的 API 等价价格（`tokenUsage.totalCents`）。首次扫描取完整 31 天，之后每次只取上次之后的部分（往前多取 2 小时补迟到的记录）。**不读取浏览器 Cookie 数据库。**

## 连接步骤

1. 在 Cursor 编辑器中登录自己的账户。
2. 在 Cursor 卡片点击「打开 Cursor」，或手动打开编辑器完成登录。
3. 回到卡片点击「我已登录，刷新」。

## 可以查看什么

- 套餐总量、Auto、API / 手动模型的剩余比例。套餐总量仍是托盘和额度窗口的主额度。
- Grok Bot · 每周：已用比例与下次重置时间。仅当接口返回有效数据且 `hasNonZeroIncludedLimit` 为 true 时，显示在 Cursor 卡片的最后一行。
- 账期结束时间、重置倒计时、套餐与账户标识。
- 详情页的**账户用量**：所选时间段的费用、Token、请求数、Token 构成、按小时 / 天的柱状图、各模型的费用和 Token，以及当前账期内的用量。概览和命令行 `codeusage cost` 也包含 Cursor。

## 限制

- 用量来自账户后台，**包含该账户在其他设备上的调用**，和其他平台的“本机用量”口径不同。
- 明细里没有请求耗时，所以 Cursor **没有输出速度**。
- 按次计入套餐的请求（明细里没有 Token）只计请求数，Token 为 0。
- 费用是 Cursor 给出的 API 等价价格，不是订阅账单。
- 便携版或改过用户数据目录的 Cursor 可能不在默认读取路径。

## 隐私

编辑器令牌保留在 Cursor 的数据库中；监控读取并向 Cursor 自己的接口发送会话，不读取浏览器的 Cookie 存储。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引（`data/cursor-logs.json`）只保存每小时、每个模型的 Token、请求数和费用，不保存对话正文或会话 ID。

## 常见问题

若未连接，先确认默认 Windows 用户下的 Cursor 已登录。无法自动打开时可手动启动。令牌失效时在 Cursor 内重新登录；接口返回变化可能需要后续适配。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`Core.cs`（ProviderService.Fetch / CursorDashboard / Parsers.Cursor / Parsers.CursorGrokBot / NativeCredentials.SqliteText）、`CursorUsage.cs`（用量明细）、`Connect.cs`。服务商接口或客户端格式变化时可能需要更新。
