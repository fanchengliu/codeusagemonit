# DeepSeek

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.3.0。连接方式：**API Key**；本机用量来自 **DeepSeek Harness**。

## 数据来源

使用用户填写的 API Key，或 `DEEPSEEK_API_KEY` 环境变量，向 `api.deepseek.com/user/balance` 查询。环境变量优先于应用内保存的密钥。

本机用量：读取 DeepSeek Harness（`dsh`，`@deepseek-ai/dsh-*`）保存在 `~/.dsh/sessions/<工作区>/<会话>/session.v4.jsonl.zstd` 的会话记录。文件由多个追加的 zstd 帧组成，本软件自带解压（.NET Framework 没有 zstd），增量读取：只解压上次之后新写入的完整帧。每次模型调用由 `request/header`（发出时间、模型）和 `assistant/message`（`usage` 与回复写入时间）组成。

## 连接步骤

1. 在 DeepSeek API 平台创建自己的 API Key。
2. 在 DeepSeek 卡片或「设置 → 账号与密钥」粘贴密钥，点击「保存并连接」。
3. 也可以设置 `DEEPSEEK_API_KEY` 后重新启动监控。

## 可以查看什么

- API 账户总余额，按接口返回的币种分别显示。
- 详情页的**本机用量**（使用 DeepSeek Harness 时）：费用、Token、请求数、Token 构成、柱状图、各模型用量和**输出速度**。

## 限制

这是 DeepSeek API 余额，不是网页聊天的额度。余额变化不用来推算费用。

- 本机用量只包括 DeepSeek Harness 的调用；直接调用 API 的其他程序不计入。
- 会话标题的生成调用在记录里没有用量，不计入。
- `inputTokens` 按“不含缓存命中”的输入处理；若某条记录的 `totalTokens` 表明缓存命中已含在输入里，会自动扣除。目前只用无缓存命中的会话核对过。
- 数据目录固定为 `~/.dsh`（未发现可改目录的环境变量）。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

手动填写的密钥或设备流令牌使用 Windows DPAPI 加密保存在本机 data 目录；只供当前 Windows 用户解密。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

确认密钥来自 DeepSeek API 平台且仍有效；如果应用中换了密钥仍无变化，检查是否有优先级更高的 DEEPSEEK_API_KEY。网络失败时检查设置里的代理模式。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`Core.cs`（Store.ProviderKey / ProviderService.Fetch / Parsers.DeepSeek）、`Connect.cs`、`DeepSeekHarness.cs`（会话记录）、`Zstd.cs`（解压）。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
