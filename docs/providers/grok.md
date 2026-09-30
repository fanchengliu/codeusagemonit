# Grok

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**CLI 会话**。

## 数据来源

读取 Grok Build CLI 的 `~/.grok/auth.json`；`GROK_HOME` 可覆盖目录。使用其中已保存的会话向 `cli-chat-proxy.grok.com/v1/billing?format=credits` 查询。

## 连接步骤

1. 安装 Grok Build CLI 并运行 `grok login`。
2. 在 Grok 卡片点击「在终端登录」也能打开这个流程；完成后点击「我已登录，刷新」。
3. 本机统计读取 `~/.grok/sessions/**/updates.jsonl`，或 GROK_HOME 对应目录。

## 可以查看什么

- 当前账期订阅使用比例、重置时间，以及接口提供的 Build 使用占比。
- 本机 Token、费用估算、请求数和基于客户端计时的输出速度。

## 限制

此连接使用 Grok Build CLI 会话，不读取 grok.com 的浏览器 Cookie，也不是 xAI 控制台管理 API。只有日志含 apiDurationMs 的有效请求可纳入速度统计。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

OAuth 凭据保留在原应用；监控只读使用已有会话。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

无法找到 grok 命令时先安装 CLI 或手动登录。已登录但没有数据时检查 GROK_HOME 与原 CLI 是否一致，失效时重新执行 grok login。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（Grok 分支 / Parsers.Grok）、`Connect.cs`、`AgentLogs.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
