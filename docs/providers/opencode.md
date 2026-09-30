# OpenCode

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**API Key**。

## 数据来源

使用 OpenCode Go 订阅 API Key 或 `OPENCODE_API_KEY`，查询 `opencode.ai/zen/go/v1/usage`。

## 连接步骤

1. 在设置里启用 OpenCode。
2. 在卡片或设置中填写 OpenCode Go 的 API Key，点击「保存并连接」。
3. 本机历史默认从 `~/.local/share/opencode` 读取；支持 XDG_DATA_HOME 或 OPENCODE_DATA_DIR 指定的位置。

## 可以查看什么

- Go 订阅接口返回的 5 小时、每周、每月额度。
- 本机 opencode*.db 或旧版 storage/message JSON 中的 Token、请求、模型、费用及可用计时。

## 限制

使用其他模型提供商的 OpenCode 会话不等于拥有 OpenCode Go 订阅。不同于 CodexBar 的部分实现，此版本不使用 OpenCode 浏览器 Cookie。1.1.0 的本机历史与速度解析没有真实开发机样本验证。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

手动填写的密钥或设备流令牌使用 Windows DPAPI 加密保存在本机 data 目录；只供当前 Windows 用户解密。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

缺少 rolling 用量字段时会提示接口不兼容；确认 Key 属于 Go 订阅。只有账户额度但无本机统计时，检查日志位置与客户端版本。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（OpenCode 分支 / Parsers.OpenCode）、`Connect.cs`、`MoreAgentLogs.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
