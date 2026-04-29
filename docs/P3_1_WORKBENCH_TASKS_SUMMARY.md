# P3.1 Workbench Tasks Summary

## 做了什么

- 新增 `WorkbenchTask` 只读投影模型。
- 新增 `LocalWorkbenchTaskService`，从现有客户、订单、消息、AI 建议、OCR、FollowUp、ActivityLog 只读推导工作台任务。
- 在工作台左列顶部新增“今日行动”卡片。
- 新增 `MainViewModel` 最小接入：
  - `ObservableCollection<WorkbenchTaskListItem> WorkbenchTasks`
  - `WorkbenchTaskListItem? SelectedWorkbenchTask`
  - `RefreshWorkbenchTasksCommand`
  - `OpenWorkbenchTaskCommand`
  - `SelectWorkbenchTaskCommand`

## 没做什么

- 没改表。
- 没给任务单独落库。
- 没给任务新增编辑流。
- 没做 AI 建议 / OCR 子项级别深链。

## 任务类型与数据来源

- `ReplyNeeded`
  - 来源：`ConversationMessages` + `AiSuggestions` + `ActivityLogs`
  - 规则：最新 `Incoming` 后没有后续 `Outgoing / Sent / AutoReplySent`
- `DraftNotSent`
  - 来源：`AiSuggestions.Status` + `MetadataJson.autoReply.state`
  - 规则：`DraftPrepared` 或 `prepared/copied`，且未 `Sent`
- `AiSuggestionPending`
  - 来源：`AiSuggestions.Status`
  - 规则：`Draft`
- `OcrNotConverted`
  - 来源：`OcrResults.Status` + `MetadataJson.convertedToMessageId`
  - 规则：`Completed` 且未写入 `convertedToMessageId`
- `FollowUpToday`
  - 来源：`FollowUps`
  - 规则：`ScheduledAt.Date == today` 且未完成
- `FollowUpOverdue`
  - 来源：`FollowUps`
  - 规则：`ScheduledAt.Date < today` 且未完成
- `RecentlyActiveCustomer`
  - 来源：`ActivityLogs / ConversationMessages / Customer.LastContactAt / UpdatedAt / Order.UpdatedAt`
  - 规则：近 7 天存在活跃信号

## 排序规则

固定优先级：

1. `FollowUpOverdue`
2. `DraftNotSent` 且 `state = copied`
3. `ReplyNeeded`
4. `DraftNotSent` 且 `state = prepared`
5. `AiSuggestionPending`
6. `OcrNotConverted`
7. `FollowUpToday`
8. `RecentlyActiveCustomer`

同类任务内按时间倒序，再按稳定键排序。

## 点击任务行为

- 优先定位 `Order`
- 再同步定位 `Customer`
- 加载现有详情区
- 不创建新业务状态
- 无法稳定定位到具体 AI/OCR 子项时，只定位客户/订单

## FollowUp 接入情况

- 已接入 `FollowUps.ScheduledAt / Status / CompletedAt`
- 当前不额外把 `Order.NextFollowUpAt` 映射成任务，避免双来源冲突

## 已知限制

- `RecentlyActiveCustomer` 仍可能较宽，会把非阻塞型活跃客户列出来。
- `ReplyNeeded` 依赖现有本地发送留痕；如果未来出现新的发送留痕格式，需要补规则。
- 当前只在工作台页显示，不在客户/订单页重复展示。
