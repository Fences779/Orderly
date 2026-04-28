# P2.2 AI Suggestion Summary

## 1. 本轮做了什么

- 在主工作台右侧详情区新增最小 `AI 建议` 入口，位于 `消息 / 沟通记录` 下方、`ActivityLog` 上方。
- 支持在当前客户 / 订单上下文下手动点击“生成建议”。
- 复用现有 `LocalAiAssistantService` 生成明确标记为 `【Local Stub】` 的本地建议。
- 生成后立即写入 `AiSuggestions`，并刷新当前上下文最近建议列表。
- 支持对 `Draft` 状态建议执行“接受 / 拒绝”。
- “接受 / 拒绝”只更新 `AiSuggestion.Status`，不发送任何外部消息。
- 生成、接受、拒绝都会写入 `ActivityLog`。
- 新增 `tools/qa/run-p2-2-ai-suggestion-smoke.ps1`，覆盖 Service / Repository 级闭环验证。

## 2. 本轮没做什么

- 没有接真实 AI API。
- 没有接 OCR。
- 没有做自动发送。
- 没有做平台同步。
- 没有做复杂 Prompt 配置。
- 没有做多轮聊天。
- 没有大改 UI。
- 没有重构订单主链路。

## 3. 入口在哪里

- 入口位置：`工作台` 页签，右侧详情栏，`消息 / 沟通记录` 下方的 `AI 建议` 区块。
- UI 文件：`src/Orderly.App/Views/MainWindow.xaml`
- ViewModel 命令入口：
  - `src/Orderly.App/ViewModels/MainViewModel.AiSuggestionCommands.cs`
  - `src/Orderly.App/ViewModels/MainViewModel.Loading.cs`

## 4. 数据写入路径

- UI / ViewModel：
  - `MainViewModel -> IAiAssistantService`
- Service：
  - `src/Orderly.Data/Services/LocalAiAssistantService.cs`
- Repository：
  - `src/Orderly.Data/Repositories/AiSuggestionRepository.cs`
  - `src/Orderly.Data/Repositories/ActivityLogRepository.cs`
- SQLite 表：
  - `AiSuggestions`
  - `ActivityLogs`

上下文规则：
- `CustomerId`：始终来自 `SelectedCustomer`
- `OrderId`：优先当前且属于该客户的 `SelectedOrder`
- `DealId`：优先当前订单 `DealId`，否则回退 `SelectedDeal`
- `MessageId`：生成建议时优先绑定当前上下文最近消息或显式传入消息

说明：
- `AiSuggestions` 当前模型本身不存 `DealId`。
- `DealId` 会用于生成 / 接受 / 拒绝时的 `ActivityLog` 留痕，保持与 P2.1 上下文策略一致。

## 5. 状态流转

- 初始状态：`Draft`
- 用户点击“接受”后：`Draft -> Accepted`
- 用户点击“拒绝”后：`Draft -> Rejected`
- 本轮不做：
  - `Accepted -> Sent`
  - 自动发送
  - 外部平台回执同步

留痕规则：
- 生成建议：`ActivityType.AiSuggestionGenerated`
- 接受建议：`ActivityType.AiSuggestionAccepted`
- 拒绝建议：`ActivityType.AiSuggestionRejected`

## 6. QA 命令和结果

- Build：
  - `dotnet build Orderly.sln -c Debug`
  - 结果：通过
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
  - 结果：通过
- P2.2 AI suggestion smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-2-ai-suggestion-smoke.ps1`
  - 结果：通过

P2.2 smoke 覆盖点：
- 基于已有 `ConversationMessage` 生成本地建议
- 通过 `ListSuggestionsAsync` 回读建议
- 将一条建议更新为 `Accepted`
- 将另一条建议更新为 `Rejected`
- 校验 `ActivityLog` 的生成 / 接受 / 拒绝记录
- 校验 `reset-qa-data` 后 `AiSuggestions` 与 `ActivityLogs` 基线恢复稳定

UIA 覆盖说明：
- 本轮没有新增单独的 P2.2 UIA 自动化。
- 已通过 P1 UIA smoke，说明 P1 主链路未被破坏。
- P2.2 本轮至少完成了 Service / Repository 级专项 QA。

## 7. 下一步 P2.3 建议

- 接入真实 AI Provider，但继续保留 `Local Stub` 作为离线降级路径。
- 在生成建议前补最小上下文裁剪，避免直接把全部消息历史无上限送入 provider。
- 为真实 AI 接入补错误态、超时、用户可见的降级提示和 provider 调用留痕。
