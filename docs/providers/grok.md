# Grok

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.1。连接方式：**CLI 会话**。

## 数据来源

读取 Grok Build CLI 的 `~/.grok/auth.json`；`GROK_HOME` 可覆盖目录。使用其中最新的会话向 `cli-chat-proxy.grok.com/v1/billing?format=credits` 查询。

计费接口在新账期可能只给出账期、不给用量百分比。这时用同一会话再查 `grok.com/grok_api_v2.GrokBuildBilling/GetGrokCreditsConfig`：有明确百分比就用；百分比缺省（protobuf 省略零值）时，只有完整返回、账期类型可识别且覆盖当前时刻，才按 0% 处理。空返回、错误、未来或历史账期都不会生成「剩余 100%」。

## 会话恢复

- 会话已到 `expires_at`：先在后台运行一次 `grok models`（列出模型，不发送任务），由 Grok CLI 自己续期并保存，再重新读取 `auth.json`。
- 接口返回 401：先重读 `auth.json`，其他客户端可能已经换了新会话；没有新会话再续期，然后只重试一次。
- 续期 30 秒超时；同一平台的后台运行不会并发；10 分钟内最多续期一次。仍然失败才提示登录过期，上次成功读数会保留并标记为旧数据。
- 可在「设置 → 高级 → 登录续期」关闭。

## 连接步骤

1. 安装 Grok Build CLI 并运行 `grok login`。
2. 在 Grok 卡片点击「在终端登录」也能打开这个流程；完成后点击「我已登录，刷新」。
3. 本机统计读取 `~/.grok/sessions/**/updates.jsonl`，或 GROK_HOME 对应目录。

## 可以查看什么

- 当前账期一条共享额度（已用比例和重置时间）。Build / Chat 作为本期消耗构成列出，计入同一份额度，不另显示成剩余额度。旧缓存里的「Build 占比」在读取时去掉。
- 本机 Token、费用估算、请求数和基于客户端计时的输出速度。

## 限制

此连接使用 Grok Build CLI 会话，不读取 grok.com 的浏览器 Cookie，也不是 xAI 控制台管理 API。只有日志含 apiDurationMs 的有效请求可纳入速度统计。`GetGrokCreditsConfig` 后备按 issue #17 报告的格式实现，尚未在本仓库维护者的账户上实测。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

OAuth 凭据保留在原应用；监控只读使用已有会话，续期由 Grok CLI 自己完成，本程序不读写刷新令牌。不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

无法找到 grok 命令时先安装 CLI 或手动登录。已登录但没有数据时检查 GROK_HOME 与原 CLI 是否一致；自动续期失败时重新执行 grok login。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`Core.cs`（Grok 分支 / Parsers.Grok）、`SessionRecovery.cs`、`BackgroundCli.cs`、`Connect.cs`、`AgentLogs.cs`。会话恢复与新账期解析依据 [issue #17](https://github.com/fanchengliu/codeusagemonit/issues/17)（@xing-skyline）的分析独立实现。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
