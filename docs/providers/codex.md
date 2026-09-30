# Codex

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**OAuth**。

## 数据来源

读取 `%USERPROFILE%/.codex/auth.json` 中 Codex 自己保存的 OAuth 会话；设置了 `CODEX_HOME` 时使用该目录。向 `chatgpt.com` 查询额度及限额重置额度。

## 连接步骤

1. 在 Codex 应用中登录，或运行 `codex login`。
2. 打开 codeusagemonit 的 Codex 卡片，点击刷新。未连接时可点击「在终端登录」，完成后点击「我已登录，刷新」。
3. 需要本机用量时，在 Codex 中产生会话记录；程序读取 `sessions` 和 `archived_sessions`。

## 可以查看什么

- 账户实际返回的 5 小时、每周及其他额度窗口与重置时间。
- 接口提供时展示限额重置额度与到期信息。
- 本机日志的 Token、API 等价费用、请求数、模型构成及可计时请求的输出速度。

## 限制

纯 API Key 登录不提供这里使用的 ChatGPT 订阅 OAuth 会话。接口未返回的周期不会凭空补全；重置额度接口失败不影响已取得的主额度。删除或缺失的本机会话无法从账户额度接口还原。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

OAuth 凭据保留在原应用；监控只读使用已有会话。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

令牌过期时，先打开 Codex 应用或运行一次 Codex，让原客户端续期，再刷新。若使用自定义 CODEX_HOME，请确认启动监控的进程也能看到该环境变量。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（ProviderService.Fetch / Parsers.Codex）、`UsageDetails.cs`、`LocalLogs.cs`、`Timing.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
