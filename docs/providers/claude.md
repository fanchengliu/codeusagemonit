# Claude

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**OAuth**。

## 数据来源

读取 Claude Code 的 `%USERPROFILE%/.claude/.credentials.json`，或 `CLAUDE_CONFIG_DIR` 指定目录中的同名文件。使用 OAuth access token 向 `api.anthropic.com/api/oauth/usage` 查询。

## 连接步骤

1. 先在 Claude Code 中登录：运行 `claude`，按需要输入 `/login`。
2. 在 Claude 卡片点击刷新；也可从卡片打开 Claude Code 终端，登录完成后点击「我已登录，刷新」。
3. 程序从 `~/.claude/projects`、`~/.config/claude/projects` 或 `CLAUDE_CONFIG_DIR` 对应位置读取本机会话。

## 可以查看什么

- 接口实际返回的 5 小时、每周，以及 Sonnet / Opus 等模型额度。
- 套餐、重置时间、剩余比例和使用节奏。
- 本机 Token 构成、API 等价费用、请求数、模型统计和输出速度。

## 限制

这里查询的是 Claude Code 订阅的 OAuth 用量，不是 Anthropic Console API 账单。API Key 或第三方接口用量不一定消耗该订阅额度；第三方记录可在「第三方」页查看。Windows 1.1.0 没有浏览器 Cookie 导入或 CodexBar 的网页 / PTY 回退。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

OAuth 凭据保留在原应用；监控只读使用已有会话。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

先运行一次 Claude Code 刷新会话，再刷新卡片。403 也可能与网络代理、账户访问或服务端限制有关；旧读数会保留时间标记，不代表当前成功。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（ProviderService.Fetch / Parsers.Claude）、`Connect.cs`、`LocalLogs.cs`、`Timing.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
