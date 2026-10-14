# Antigravity

[← 返回项目](../../README.md) · [下载](https://github.com/fanchengliu/codeusagemonit/releases)

> 适用于 codeusagemonit 1.0.1。连接方式：**本地服务 · agy CLI · OAuth**。

## 数据来源

按顺序尝试：

1. **本地服务**：探测当前 Windows 用户正在运行的 Antigravity / agy 本地语言服务，通过回环地址读取额度（`RetrieveUserQuotaSummary`），再用 `GetUserStatus` 读取订阅等级和模型名称。
2. **agy CLI**：桌面应用没有运行、PATH 上有 agy 1.1.11 或更新版本时，在后台运行 `agy -p /usage --output-format json`，只接受成功的结构化 `usage` 报告。仅对这次子进程设置 `AGY_CLI_DISABLE_AUTO_UPDATE=true`，避免 agy 的自动更新弹出终端窗口。可在「设置 → 高级 → 登录续期」关闭。
3. **OAuth 回退**：读取 Windows Credential Manager 的 `gemini:antigravity` 凭据，向 Google Cloud Code 接口查询。

## 连接步骤

1. 打开 Antigravity 并登录，保持应用运行。
2. 在 Antigravity 卡片点击「我已登录，刷新」；若应用未打开，可以点击「打开 Antigravity」。
3. 本机用量来自 `~/.gemini/antigravity*/conversations/*.db` 中可识别的会话记录。

## 可以查看什么

- 本地服务、agy 或 OAuth 接口返回的分组额度（如 Gemini Models、Claude and GPT models 的每周 / 5 小时窗口）与重置时间。
- 订阅等级：取 `userTier.name`（如 Google AI Pro / Google AI Ultra）；旧字段 `planInfo.planName` 无论订阅都常显示 Pro，只作后备。
- 本机日志中有记录的 Token、费用估算、请求数和模型构成。

## 模型名称

Antigravity 的会话记录只保存数字模型 id。比本程序更新的模型以前会显示为 `model_placeholder_m417` 一类的占位名，也查不到价目。现在本地服务的模型列表（如 `MODEL_PLACEHOLDER_M417 → Gemini 4 Argon (High)`）会保存到数据目录的 `antigravity-models.json`，本机统计按它命名：

| 记录中的 id | 显示为 | 价目 |
|---|---|---|
| m401 / m402 | `claude-opus-5-5-medium` / `-high` | 按 `claude-opus-5-5` 精确匹配 |
| m403–m405 | `claude-sonnet-5-5-low` … `-high` | 按 `claude-sonnet-5-5` 精确匹配 |
| m417–m419 | `gemini-4-argon-high` … `-low` | 价目表没有公开价格时显示为未定价 |

推理强度（Low / Medium / High）保留在名称里，计价时去掉后缀、只做精确匹配，不借用相近模型的价格。名称在 Antigravity 运行并被读取过一次后才会更新；重新扫描本机用量后生效。

## 限制

只返回模型可用性时不会当作真实的 100% 剩余额度。不同 Antigravity 版本的本地协议可能不同。本地记录没有可用的请求耗时，因此不显示输出速度。agy 未登录时会停在登录方式选择处，本程序等到超时（45 秒）后结束它，10 分钟内不再重试。

费用为本机记录的 API 等价估算，不是订阅账单。数据范围受服务端返回字段、本机日志与所选时间段限制。

## 隐私

仅探测当前 Windows 用户拥有的 Antigravity 本地服务；agy 由它自己读取和维护登录；OAuth 回退读取系统凭据管理器。不存储密码，也不读写刷新令牌。网络查询会把相应的鉴权信息发送到该提供商（自定义平台则发送到你配置的地址）。本机统计索引保存数值，不保存对话正文。

## 常见问题

先保持已登录的应用运行，再刷新。若提示只有模型可用性而无真实额度，请不要按显示为零的使用量推断剩余；应用更新后接口可能需要适配。

查询失败时，界面可能保留上次成功读数；请结合更新时间与错误提示判断。

## 实现位置

`source/Windows/`：`LocalAntigravity.cs`（本地服务、`AntigravityModels`）、`SessionRecovery.cs`（agy）、`BackgroundCli.cs`、`Core.cs`（Antigravity 分支）、`AgentLogs.cs`。agy 后备与会话恢复参考了 [issue #17](https://github.com/fanchengliu/codeusagemonit/issues/17)（@xing-skyline）的分析。本文依据该版本代码核对；服务商接口或客户端格式变化时可能需要更新。
