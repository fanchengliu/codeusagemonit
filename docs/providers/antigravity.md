# Antigravity

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.0。连接方式：**本地服务 · OAuth**。

## 数据来源

优先探测当前 Windows 用户正在运行的 Antigravity / agy 本地语言服务，通过回环地址读取额度。无法读取本地额度时，回退到 Windows Credential Manager 的 `gemini:antigravity` OAuth 凭据，向 Google Cloud Code 接口查询。

## 连接步骤

1. 打开 Antigravity 并登录，保持应用运行。
2. 在 Antigravity 卡片点击「我已登录，刷新」；若应用未打开，可以点击「打开 Antigravity」。
3. 本机用量来自 `~/.gemini/antigravity*/conversations/*.db` 中可识别的会话记录。

## 可以查看什么

- 本地服务或 OAuth 接口返回的周期 / 模型额度与重置时间。
- 本机日志中有记录的 Token、费用估算、请求数和模型构成。

## 限制

只返回模型可用性时不会当作真实的 100% 剩余额度。不同 Antigravity 版本的本地协议可能不同。本地记录没有可用的请求耗时，因此不显示输出速度。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

仅探测当前 Windows 用户拥有的 Antigravity 本地服务；OAuth 回退读取系统凭据管理器。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

先保持已登录的应用运行，再刷新。若提示只有模型可用性而无真实额度，请不要按显示为零的使用量推断剩余；应用更新后接口可能需要适配。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`LocalAntigravity.cs`、`Core.cs`（Antigravity 分支）、`AgentLogs.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
