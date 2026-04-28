# P2.0 Foundation Summary

## 1. P2.0 目标

P2.0 只做 AI / OCR / 自动回复 / 云同步的底座骨架，不接真实外部服务，不做 UI 视觉改版，不改变 P1 主链路。

## 2. 本轮完成内容

- 新增会话消息、AI 建议、OCR 结果、同步记录 4 个轻量模型。
- 新增对应 Repository 接口与 SQLite Repository 实现。
- 重塑 AI / OCR / 自动回复接口合同，并新增 `IConversationService`、`ISyncService`。
- 新增本地 Stub 服务：
  - `LocalAiAssistantService`
  - `LocalOcrService`
  - `LocalAutoReplyService`
  - `LocalSyncService`
- 新增 SQLite 表：
  - `ConversationMessages`
  - `AiSuggestions`
  - `OcrResults`
  - `SyncRecords`
- 最小扩展 `ActivityType` 与 ActivityLog 留痕点。
- 扩展 QA 数据维护逻辑与最小 P2 QA 种子。

## 3. 新增模型

- `ConversationMessage`
  - 保存手工消息 / 导入消息。
- `AiSuggestion`
  - 保存 AI 回复建议，不代表真实发送。
- `OcrResult`
  - 保存 OCR 任务与结果状态。
- `SyncRecord`
  - 保存未来云同步占位状态。

所有新模型均继承 `EntityBase`，继续复用 `CreatedAt / UpdatedAt / DeletedAt / RemoteId / IsSynced / Version`。

## 4. 新增接口

### Repository

- `IConversationMessageRepository`
- `IAiSuggestionRepository`
- `IOcrResultRepository`
- `ISyncRecordRepository`

### Service

- `IConversationService`
- `IAiAssistantService`
- `IOcrService`
- `IAutoReplyService`
- `ISyncService`

## 5. Stub 实现说明

- `LocalAiAssistantService`
  - 只基于本地最近消息生成固定格式建议。
  - 建议文本明确标注 `Local Stub`。
  - 不调用任何真实 AI API。
- `LocalOcrService`
  - 只创建 `Pending` 任务。
  - 只在显式调用 `CompleteOcrTaskAsync / FailOcrTaskAsync` 时变更状态。
- `LocalAutoReplyService`
  - 只处理草稿准备、已发送/已拒绝的本地状态。
  - 不发送外部消息。
- `LocalSyncService`
  - 只维护 `SyncRecord`。
  - 不上传、不下载、不创建真实远端记录。

## 6. 数据库表说明

- `ConversationMessages`
  - 关联 `CustomerId / OrderId / DealId`，承接聊天消息。
- `AiSuggestions`
  - 关联 `CustomerId / OrderId / MessageId`，承接建议文本。
- `OcrResults`
  - 关联 `CustomerId / OrderId`，承接 OCR 状态。
- `SyncRecords`
  - 通过 `EntityType + EntityId` 记录同步占位状态。

时间字段继续使用 `ToString("O")`，枚举继续存 `INTEGER`。

## 7. 本轮没有做什么

- 没有接真实 AI API。
- 没有接真实 OCR API。
- 没有做微信 / 闲鱼自动化。
- 没有做真实云同步。
- 没有把 P2.0 接进 `MainViewModel` 或 `FloatingWindowViewModel`。
- 没有改订单主链路 `IOrderRepository -> MerchantOrder -> OrderListItem`。
- 没有做 Final Visual QA。

## 8. 后续 P2.1 建议

- 增加手工消息录入入口。
- 增加最小消息列表只读展示。
- 为 `LocalAiAssistantService` 增加基于消息上下文的更稳定模板规则。
- 为 `LocalOcrService` 增加手动补全文本入口。
- 为 `LocalSyncService` 增加批量待同步检查视图。
