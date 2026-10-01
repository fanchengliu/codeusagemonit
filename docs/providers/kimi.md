# Kimi

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.0。连接方式：**API Key**。

## 数据来源

使用 Kimi Code API Key 或 `KIMI_CODE_API_KEY`。国内接口 `api.kimi.com/coding/v1/usages`，国际接口 `api.kimi.ai/coding/v1/usages`，由设置的地区决定。

## 连接步骤

1. 在设置里启用 Kimi。
2. 从 Kimi Code 控制台获取 Key；卡片中选国内 / 国际，粘贴 Key，点击「保存并连接」。
3. 本机记录来自 `~/.kimi/sessions`、`~/.kimi-code/sessions`，可由 `KIMI_DATA_DIR` 覆盖。

## 可以查看什么

- 接口返回的 5 小时、每周、每月总量、套餐和重置时间。
- CLI wire.jsonl 中的本机 Token、请求数、费用估算和模型构成。

## 限制

这里连接 Kimi Code，不使用 kimi.com 网页的 kimi-auth Cookie，也不是 Moonshot API 余额查询。地区必须与密钥所属账户匹配；日志没有可用请求耗时，不显示速度。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

手动填写的密钥或设备流令牌使用 Windows DPAPI 加密保存在本机 data 目录；只供当前 Windows 用户解密。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

401 / 无效密钥时检查 Key 类型与地区。保存了新 Key 仍不生效时检查 KIMI_CODE_API_KEY，它优先于卡片保存的 Key。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`Core.cs`（Kimi 分支 / Parsers.Kimi / ProviderCatalog.KeyEnv）、`Connect.cs`、`AgentLogs.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
