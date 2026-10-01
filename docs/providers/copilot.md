# Copilot

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.0。连接方式：**OAuth 设备流**。

## 数据来源

应用内使用 GitHub OAuth 设备码登录（申请 `read:user`），或只读复用官方 Copilot 客户端保存的 oauth_token。查询 `api.github.com/copilot_internal/user` 及 GitHub 账户标识。

## 连接步骤

1. 在「设置 → 显示的平台」启用 Copilot。
2. 在卡片点击「使用 GitHub 登录」，复制一次性设备码，在 GitHub 授权页面输入并确认。
3. 授权完成后自动连接。已存在 copilot.vim / JetBrains 等客户端会话时，程序也会尝试复用。

## 可以查看什么

- 接口返回的每月高级请求、对话、代码补全额度和重置日期。
- 无限额度显示为不限量，不伪造百分比。
- 可读取 Copilot CLI 的本机会话 / OTel 使用记录。

## 限制

GitHub 企业策略或账户权限可能影响内部额度接口。Copilot CLI 本机日志读取已实现，但开发机没有真实样本验证；普通 IDE 使用也不一定产生 CLI 日志。没有可用请求耗时，不显示速度。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

手动填写的密钥或设备流令牌使用 Windows DPAPI 加密保存在本机 data 目录；只供当前 Windows 用户解密。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

设备码过期时重新开始；授权失效时重新使用 GitHub 登录。能登录 GitHub 不等于账户已拥有 Copilot 订阅或接口访问权。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`Core.cs`（CopilotToken / StartCopilotLogin / PollCopilotLogin / Parsers.Copilot）、`Connect.cs`、`MoreAgentLogs.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
