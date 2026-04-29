# P3.1 Workbench Tasks Summary

## 当前状态

- P3.1 已完成基础工作台任务投影。
- P3.4 已在原有能力上补强逻辑，但本轮仍然不改 UI/XAML。
- `OpenWorkbenchTaskCommand` 继续保持“只定位、不执行副作用”。

## 已落地能力

- 新增 `WorkbenchTask` 只读投影模型。
- 新增 `LocalWorkbenchTaskService`，从客户、订单、消息、AI 建议、OCR、FollowUp、ActivityLog 只读推导工作台任务。
- `MainViewModel` 保留最小接入：
  - `ObservableCollection<WorkbenchTaskListItem> WorkbenchTasks`
  - `WorkbenchTaskListItem? SelectedWorkbenchTask`
  - `RefreshWorkbenchTasksCommand`
  - `OpenWorkbenchTaskCommand`
  - `SelectWorkbenchTaskCommand`

## P3.4 补强点

- 深链字段只增强 projection / ViewModel item，不改数据库 schema：
  - `RelatedEntityType`
  - `RelatedEntityId`
  - `CustomerId`
  - `OrderId`
  - `DealId`
  - `MessageId`
  - `AiSuggestionId`
  - `OcrResultId`
  - `FollowUpId`
  - `TargetSection`
  - `ActionHint`
  - `DedupeKey`
- `OpenWorkbenchTaskCommand` 继续先选 `Order`，再选 `Customer`。
- 如果任务指向 `AiSuggestion` / `Ocr`，会在 ViewModel 层做无副作用选择同步。
- `FollowUp` 目前只准备深链字段，不新增 UI 级选中状态。

## 任务类型与数据来源

- `ReplyNeeded`
  - 来源：`ConversationMessages` + `AiSuggestions` + `ActivityLogs`
  - 规则：最新 `Incoming` 后没有后续 `Outgoing / Sent / AutoReplySent`
  - 深链：`MessageId`，`TargetSection = Conversation`，`ActionHint = ReplyToCustomer`
- `DraftNotSent`
  - 来源：`AiSuggestions.Status` + `MetadataJson.autoReply.state`
  - 规则：`DraftPrepared` 或 `prepared/copied`，且未 `Sent`
  - 深链：`AiSuggestionId`、可选 `MessageId`，`TargetSection = AiSuggestion`
- `AiSuggestionPending`
  - 来源：`AiSuggestions.Status`
  - 规则：`Draft`
  - 深链：`AiSuggestionId`、可选 `MessageId`，`ActionHint = ReviewSuggestion`
- `OcrNotConverted`
  - 来源：`OcrResults.Status` + `MetadataJson.convertedToMessageId`
  - 规则：`Completed` 且未写入 `convertedToMessageId`
  - 深链：`OcrResultId`，`TargetSection = Ocr`，`ActionHint = ConvertOcr`
- `FollowUpToday`
  - 来源：`FollowUps`
  - 规则：`ScheduledAt.Date == today` 且未完成
  - 深链：`FollowUpId`，`TargetSection = FollowUp`
- `FollowUpOverdue`
  - 来源：`FollowUps`
  - 规则：`ScheduledAt.Date < today` 且未完成
  - 深链：`FollowUpId`，`TargetSection = FollowUp`
- `RecentlyActiveCustomer`
  - 来源：`ActivityLogs / ConversationMessages / Customer.UpdatedAt`
  - 规则：近 7 天活跃、最多 5 条、同客户只保留 1 条

## 降噪、去重、排序

- `RecentlyActiveCustomer` 限定最近 7 天。
- `RecentlyActiveCustomer` 总量上限 5。
- 存在以下更高优先级任务时，不再保留同客户的 `RecentlyActiveCustomer`：
  - `FollowUpOverdue`
  - `DraftNotSent`
  - `ReplyNeeded`
  - `AiSuggestionPending`
  - `OcrNotConverted`
  - `FollowUpToday`
- 去重键统一收口到 `DedupeKey`。
- 同一实体同一任务类型只保留一条。
- 固定排序：
  1. `FollowUpOverdue`
  2. `DraftNotSent` 且 `ActionHint = ReplyToCustomer`
  3. `ReplyNeeded`
  4. `DraftNotSent` 其他状态
  5. `AiSuggestionPending`
  6. `OcrNotConverted`
  7. `FollowUpToday`
  8. `RecentlyActiveCustomer`
- 同类任务内按 `OccurredAt` 倒序，再按稳定比较器落序。

## 没做什么

- 不改任何 `.xaml`。
- 不做布局、样式、视觉、控件交互优化。
- 不改表。
- 不给任务单独落库。
- 不做云同步、平台发送、自动完成。

## 已知限制

- 当前只准备深链数据和 ViewModel 状态，不新增 UI 展示。
- `FollowUp` 还没有独立的 ViewModel 级选中对象。
- `PipelineStage` 仍然是只读启发式推导，不替代 `DealStage / OrderStatus`。
