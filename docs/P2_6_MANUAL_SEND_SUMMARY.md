# P2.6 Manual Send Summary

## 1. 做了什么

- 在现有 `AI 建议` 区块上补齐“人工确认发送闭环 / 复制发送辅助”。
- 新增最小操作：
  - `准备回复草稿`
  - `复制草稿`
  - `标记已发送`
  - `拒绝草稿`（复用现有入口）
- 复制动作只写系统剪贴板，不触发任何外部发送。
- 复制后写 `ActivityLog`，并在 `AiSuggestion.MetadataJson.autoReply` 写入复制元数据。
- 标记已发送仍然只是本地状态，继续复用 `AiSuggestionStatus.Sent`。
- 自动 QA 改为使用 `InMemoryClipboardService`，不依赖真实系统剪贴板。

## 2. 没做什么

- 没有自动发送。
- 没有控制微信 / 闲鱼窗口。
- 没有监听平台。
- 没有接平台 API。
- 没有做同步。
- 没有做自动截屏。
- 没有做复杂发送历史页。
- 没有做全局快捷键监听。
- 没有新增平台选择器。
- 没有重构订单主链路。

## 3. UI 入口

- 位置：`工作台 -> 右侧详情栏 -> AI 建议`
- 按钮：
  - `准备回复草稿`
  - `复制草稿`
  - `标记已发送`
  - `拒绝草稿`
- 关键文件：
  - `src/Orderly.App/Views/MainWindow.xaml`
  - `src/Orderly.App/ViewModels/MainViewModel.AutoReplyCommands.cs`
  - `src/Orderly.App/ViewModels/AiSuggestionListItem.cs`

## 4. 复制发送流程

- `Draft / Accepted -> 准备回复草稿 -> DraftPrepared`
- `DraftPrepared -> 复制草稿 -> 仍保持 DraftPrepared，但 metadata.state = copied`
- 用户手动去微信 / 闲鱼等平台粘贴发送
- `DraftPrepared(copied) -> 标记已发送 -> Sent`

约束：
- `标记已发送` 现在要求草稿已先执行过复制动作。
- 本软件始终不执行外部发送。

## 5. 数据路径

- UI / ViewModel：
  - `MainViewModel -> IAutoReplyService`
- Service：
  - `src/Orderly.Data/Services/LocalAutoReplyService.cs`
- Repository：
  - `src/Orderly.Data/Repositories/AiSuggestionRepository.cs`
  - `src/Orderly.Data/Repositories/ActivityLogRepository.cs`
- 剪贴板抽象：
  - `src/Orderly.Core/Services/IClipboardService.cs`
  - `src/Orderly.Infrastructure/Services/DesktopClipboardService.cs`
  - `src/Orderly.Infrastructure/Services/InMemoryClipboardService.cs`

## 6. 状态流转

- 继续复用 `AiSuggestionStatus`：
  - `Draft`
  - `Accepted`
  - `DraftPrepared`
  - `Sent`
  - `Rejected`

本轮决策：
- 不新增 `Copied / ReadyToSend` 枚举状态。
- 用 `MetadataJson.autoReply.state = prepared / copied / sent / rejected` 表达草稿子状态。
- 这样不影响现有表结构和主状态枚举读取逻辑，改动最小。

## 7. metadata

- 位置：`AiSuggestions.MetadataJson.autoReply`
- 关键字段：
  - `mode = "local-draft"`
  - `state = "prepared" | "copied" | "sent" | "rejected"`
  - `localOnly = true`
  - `externalSendExecuted = false`
  - `preparedAt`
  - `copiedAt`
  - `copiedBy = "p2.6"`
  - `deliveryMode = "manual-copy"`
  - `externalPlatform = "manual"`
  - `sentAt`
  - `sentBy = "manual-confirm"`

说明：
- 复制时写 `copiedAt / copiedBy / deliveryMode`。
- 标记已发送时写 `sentAt / sentBy`。
- 不新增独立发送记录表。

## 8. ActivityLog

- 继续保留：
  - `AutoReplyDraftPrepared`
  - `AutoReplySent`
  - `AutoReplyDraftRejected`
- 本轮新增：
  - `AutoReplyDraftCopied`

含义：
- `AutoReplyDraftCopied`：草稿已复制到系统剪贴板，等待用户手动粘贴发送。
- `AutoReplySent`：只是本地人工确认已发送，不代表软件调用了外部平台。

## 9. QA

- Build：
  - `dotnet build Orderly.sln -c Debug`
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
- P2.6 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-6-manual-send-smoke.ps1`

P2.6 smoke 覆盖点：
- 基于 `AiSuggestion` 准备回复草稿
- 复制草稿到 fake clipboard
- 校验 `copiedAt / deliveryMode / copiedBy`
- 校验 `AutoReplyDraftCopied` 日志
- 标记已发送
- 校验 `Sent` 状态与 `sentAt / sentBy`
- 校验 `AutoReplySent` 日志
- 校验 `reset-qa-data` 后基线恢复

UIA 覆盖说明：
- 本轮未新增按钮级 UIA 自动化。
- 已补 Service / Repository 级专项 smoke。

## 10. 下一步 P2.7 建议

- 先明确云备份边界：只备份本地数据，还是同步 `Conversation / AiSuggestion / OcrResult / ActivityLog` 全量对象。
- 为 `AiSuggestions / OcrResults / ConversationMessages` 设计最小远端标识和冲突策略。
- 在不破坏本地优先体验的前提下补“手动触发备份 / 最近备份状态”。
