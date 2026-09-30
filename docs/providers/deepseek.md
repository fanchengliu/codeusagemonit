# DeepSeek

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**API Key**。

## 数据来源

使用用户填写的 API Key，或 `DEEPSEEK_API_KEY` 环境变量，向 `api.deepseek.com/user/balance` 查询。环境变量优先于应用内保存的密钥。

## 连接步骤

1. 在 DeepSeek API 平台创建自己的 API Key。
2. 在 DeepSeek 卡片或「设置 → 账号与密钥」粘贴密钥，点击「保存并连接」。
3. 也可以设置 `DEEPSEEK_API_KEY` 后重新启动监控。

## 可以查看什么

- API 账户总余额，按接口返回的币种分别显示。

## 限制

这是 DeepSeek API 余额，不是网页聊天的额度。1.1.0 不从 DeepSeek 读取独立的本机 Token 历史，也不以余额变化推算费用。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

手动填写的密钥或设备流令牌使用 Windows DPAPI 加密保存在本机 data 目录；只供当前 Windows 用户解密。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

确认密钥来自 DeepSeek API 平台且仍有效；如果应用中换了密钥仍无变化，检查是否有优先级更高的 DEEPSEEK_API_KEY。网络失败时检查设置里的代理模式。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（Store.ProviderKey / ProviderService.Fetch / Parsers.DeepSeek）、`Connect.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
