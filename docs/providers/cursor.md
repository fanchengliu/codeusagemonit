# Cursor

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**本地会话 · Cookie**。

## 数据来源

读取 `%APPDATA%/Cursor/User/globalStorage/state.vscdb` 中的 `cursorAuth/accessToken` 和缓存邮箱。根据这个编辑器会话生成 `WorkosCursorSessionToken` Cookie 请求头，向 `cursor.com/api/usage-summary` 查询。**不读取浏览器 Cookie 数据库。**

## 连接步骤

1. 在 Cursor 编辑器中登录自己的账户。
2. 在 Cursor 卡片点击「打开 Cursor」，或手动打开编辑器完成登录。
3. 回到卡片点击「我已登录，刷新」。

## 可以查看什么

- 套餐总量、Auto、API / 手动模型的剩余比例。
- 账期结束时间、重置倒计时、套餐与账户标识。

## 限制

1.1.0 没有 Cursor 本机会话 Token / 费用扫描器。读到额度不意味着有本机 Token 历史。便携版或改过用户数据目录的 Cursor 可能不在默认读取路径。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

编辑器令牌保留在 Cursor 的数据库中；监控读取并向 Cursor 自己的接口发送会话，不读取浏览器的 Cookie 存储。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

若未连接，先确认默认 Windows 用户下的 Cursor 已登录。无法自动打开时可手动启动。令牌失效时在 Cursor 内重新登录；接口返回变化可能需要后续适配。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（ProviderService.Fetch / Parsers.Cursor / NativeCredentials.SqliteText）、`Connect.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
