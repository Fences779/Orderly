# P2.4 AI Provider Summary

## 1. 本轮做了什么

- 保留 `IAiAssistantService` 作为业务入口，继续由 `LocalAiAssistantService` 负责：
  - 读取客户 / 订单 / 最近消息
  - 最小上下文裁剪
  - 调用 Provider
  - 写入 `AiSuggestions`
  - 写入 `ActivityLog`
  - Provider 失败时自动 fallback
- 新增 Provider 层抽象：
  - `src/Orderly.Core/Services/IAiSuggestionProvider.cs`
  - `src/Orderly.Core/Models/AiSuggestionRequest.cs`
  - `src/Orderly.Core/Models/AiSuggestionProviderResult.cs`
  - `src/Orderly.Core/Models/AiSuggestionContextMessage.cs`
- 新增默认本地 Provider：
  - `src/Orderly.Data/Services/LocalAiSuggestionProvider.cs`
- 新增通用 `OpenAI-compatible` HTTP Provider：
  - `src/Orderly.Data/Services/OpenAiCompatibleSuggestionProvider.cs`
- 新增环境变量配置与 Provider 选择：
  - `src/Orderly.Data/Services/AiProviderOptions.cs`
  - `src/Orderly.Data/Services/AiSuggestionProviderFactory.cs`
- 在 `AI 建议` 卡片增加极简 Provider 标记：
  - `Local Stub`
  - `OpenAI-compatible`
  - `Fallback`
- 新增离线专项 QA：
  - `tools/qa/run-p2-4-ai-provider-smoke.ps1`

## 2. 本轮没做什么

- 没有做自动发送。
- 没有接微信 / 闲鱼 / 任何外部消息平台发送。
- 没有做 OCR。
- 没有做同步。
- 没有做设置页。
- 没有做密钥输入 UI。
- 没有做复杂 Prompt 管理后台。
- 没有重构订单主链路。
- 没有把真实 API Key 写入代码、脚本或文档。

## 3. Provider 架构

- App 组合层：
  - `src/Orderly.App/App.xaml.cs`
  - 从环境变量读取 `AiProviderOptions`
  - 创建默认 `LocalAiSuggestionProvider`
  - 根据 `ORDERLY_AI_PROVIDER` 选择 primary provider
- 业务层：
  - `src/Orderly.Data/Services/LocalAiAssistantService.cs`
  - 负责上下文装配、上下文裁剪、调用 Provider、fallback、持久化和 `ActivityLog`
- Provider 层：
  - `LocalAiSuggestionProvider`
  - `OpenAiCompatibleSuggestionProvider`
- 数据层：
  - 继续复用 `AiSuggestions` / `ActivityLogs`
  - 没有新增表

调用路径：

- `MainViewModel -> IAiAssistantService`
- `LocalAiAssistantService -> IAiSuggestionProvider`
- `IAiSuggestionProvider -> AiSuggestionProviderResult`
- `LocalAiAssistantService -> AiSuggestionRepository / ActivityLogRepository`

## 4. Local Stub fallback 策略

- `ORDERLY_AI_PROVIDER` 为空或 `local`：
  - 直接使用 `Local Stub`
  - `usedFallback = false`
- `ORDERLY_AI_PROVIDER=openai-compatible`：
  - 尝试走 `OpenAiCompatibleSuggestionProvider`
  - 若缺少 `BASE_URL / API_KEY / MODEL`，自动 fallback 到 `Local Stub`
  - 若超时、HTTP 错误、JSON 解析失败、返回空文本，自动 fallback 到 `Local Stub`
  - fallback 时 `usedFallback = true`
  - `errorSummary` 记录失败摘要

关键前提：

- 没有 API Key 时，程序仍可完整运行。
- 默认自动 QA 不调用真实外网 API。
- fallback 后仍写 `AiSuggestions` 与 `ActivityLog`。

## 5. 最小上下文裁剪

- 客户信息：
  - 姓名
  - 联系昵称 / handle
  - 备注
- 订单信息：
  - 商品标题
  - 预算
  - 状态
  - 需求备注
- 最近消息：
  - 默认最多 5 条
  - 最近一条客户消息优先作为 focus message
- 明确不包含：
  - 系统路径
  - 数据库路径
  - API Key

Prompt 约束：

- 中文
- 短
- 可审计
- 只生成可直接编辑的回复建议
- 不输出 JSON
- 不输出解释
- 不假装已经发送

## 6. 环境变量配置

支持以下环境变量：

- `ORDERLY_AI_PROVIDER`
- `ORDERLY_AI_BASE_URL`
- `ORDERLY_AI_API_KEY`
- `ORDERLY_AI_MODEL`
- `ORDERLY_AI_TIMEOUT_SECONDS`

推荐配置：

- 默认本地：
  - `ORDERLY_AI_PROVIDER` 留空或设为 `local`
- 开启通用兼容接口：
  - `ORDERLY_AI_PROVIDER=openai-compatible`

PowerShell 手动测试示例：

```powershell
$env:ORDERLY_AI_PROVIDER="openai-compatible"
$env:ORDERLY_AI_BASE_URL="https://api.deepseek.com/v1"
$env:ORDERLY_AI_API_KEY="<your-api-key>"
$env:ORDERLY_AI_MODEL="deepseek-chat"
$env:ORDERLY_AI_TIMEOUT_SECONDS="15"
dotnet run --project src/Orderly.App/Orderly.App.csproj
```

说明：

- `BASE_URL` 是否需要包含 `/chat/completions`，按服务商兼容接口文档填写。
- 代码会兼容：
  - `https://example.com/v1`
  - `https://example.com/v1/chat/completions`
- 文档只使用占位符，不包含真实密钥。

## 7. Metadata 字段

`AiSuggestion.MetadataJson` 至少包含：

- `provider`
- `model`
- `usedFallback`
- `createdBy = "p2.4"`
- `contextMessageCount`
- `timeoutSeconds`
- `errorSummary`（fallback 时可选）

当前还补充了：

- `requestedProvider`
- `messageId`
- `orderId`
- `providerResult`

`ActivityLog` 生成记录会额外带：

- `suggestionId`
- `suggestionStatus`
- `messageId`
- `provider`
- `usedFallback`

## 8. 无 API Key 时如何运行

- 默认无需任何环境变量。
- 不配置 `ORDERLY_AI_PROVIDER` 时，应用直接走 `Local Stub`。
- 即使把 `ORDERLY_AI_PROVIDER` 设为 `openai-compatible`，只要缺少必要配置，也会自动 fallback 到 `Local Stub`。
- 因此不会因为没有 API Key 导致：
  - build 失败
  - QA 失败
  - App 启动失败

## 9. QA 命令和结果

- Build：
  - `dotnet build Orderly.sln -c Debug`
  - 结果：通过
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
  - 结果：通过
- P2.4 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-4-ai-provider-smoke.ps1`
  - 结果：通过

P2.4 smoke 覆盖点：

- 无环境变量时使用 `Local Stub`
- `openai-compatible` 缺配置时不崩溃
- 使用本地无效地址模拟 Provider 失败并 fallback
- fallback 后仍能生成 `AiSuggestion`
- `ListSuggestionsAsync` 可读回
- `MetadataJson` 包含 `provider / usedFallback / createdBy / contextMessageCount`
- `ActivityLog` 有 `AiSuggestionGenerated`
- `reset-qa-data` 后基线恢复稳定

## 10. 下一步 P2.5 建议

- 保持当前 Provider 架构，给 OCR 也做相同的 provider/fallback 边界。
- 继续坚持默认离线可运行，不让真实 Provider 成为主链路硬依赖。
- 若进入 P2.5，优先做：
  - OCR Provider 抽象
  - 本地 Stub / mock fallback
  - 错误留痕与最小可审计 metadata
