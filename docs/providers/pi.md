# Pi

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.1.0。连接方式：**本地文件**。

## 数据来源

只读取 Pi 的本机会话文件，默认 `~/.pi/agent/sessions/**/*.jsonl`；支持 PI_AGENT_DIR 指向的日志根目录。无需账号登录，不查询远端额度接口。

## 连接步骤

1. 在设置里启用 Pi。
2. 使用 Pi 产生包含 assistant usage 字段的会话日志。
3. 刷新本机用量，在 Pi 页面选择时间范围。

## 可以查看什么

- 日志中已记录的输入、输出、缓存 Token、请求数和模型构成。
- 日志自带的 cost.total，或按本地价格表估算的费用。

## 限制

Pi 在本软件中没有账户剩余额度或重置倒计时。1.1.0 读取器已实现，但开发机没有 Pi 真实数据验证。日志没有可用请求耗时，不显示速度。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受本机日志与所选时间段限制。

## 隐私

此平台只做本机日志统计，不需要密码、令牌或网络额度查询。 不存储密码。本机统计索引保存数值，不保存对话正文。

## 常见问题

空页面不代表额度已用尽。确认当前 Windows 用户下存在 Pi 会话，且记录包含 usage；使用自定义路径时检查 PI_AGENT_DIR。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

1.1.0 下载包内 `source/Windows/`：`Core.cs`（ProviderCatalog.LocalOnly）、`MoreAgentLogs.cs`（ScanPi / PiLine）。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
