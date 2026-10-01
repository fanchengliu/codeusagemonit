# ZCode

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.0。连接方式：**API Key**。

## 数据来源

使用智谱 / Z.ai API Key，国内查询 open.bigmodel.cn，国际查询 api.z.ai，路径均为 `/api/monitor/usage/quota/limit`。

## 连接步骤

1. 在设置里启用 ZCode。
2. 选国内 / 国际，填入对应的 GLM 编码套餐 API Key，点击「保存并连接」。
3. 也可使用 Z_AI_API_KEY；国内还接受 BIGMODEL_API_KEY、ZHIPU_API_KEY、ZHIPUAI_API_KEY、GLM_API_KEY。

## 可以查看什么

- 接口实际提供的 5 小时、每周与 MCP 工具调用等额度。
- 本机 `~/.zcode/cli/db/db.sqlite` 里的 Token、费用估算、请求、模型和输出速度。

## 限制

有 API Key 不代表账户包含 GLM 编码套餐额度。地区与密钥必须一致；模型价格未收录时费用会标记为部分估算。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

手动填写的密钥或设备流令牌使用 Windows DPAPI 加密保存在本机 data 目录；只供当前 Windows 用户解密。 不存储密码。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

检查地区、密钥所属套餐与环境变量覆盖。额度正常但本机数据为空时，确认 ZCode 在当前 Windows 用户下确实产生了会话数据库。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`Core.cs`（ZCode 分支 / Parsers.Zai / ProviderCatalog.KeyEnv）、`Connect.cs`、`AgentLogs.cs`。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
