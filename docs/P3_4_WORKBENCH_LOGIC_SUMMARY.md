# P3.4 Workbench Logic Summary

## 结论

- 本轮只做逻辑、QA、文档，不改任何 `.xaml`。
- `WorkbenchTask` 的深链定位数据已补齐到 projection / ViewModel item。
- `RecentlyActiveCustomer` 已做降噪、去重、稳定排序增强。
- `PipelineStage` fallback 规则已加固，但仍保持只读推导。

## 深链字段设计

新增或强化字段：

- `CustomerId`
- `OrderId`
- `DealId`
- `RelatedEntityType`
- `RelatedEntityId`
- `MessageId`
- `AiSuggestionId`
- `OcrResultId`
- `FollowUpId`
- `TargetSection`
- `ActionHint`
- `DedupeKey`

字段原则：

- 不改数据库 schema。
- 不落任务表。
- 不新增副作用命令。
- `OpenWorkbenchTaskCommand` 仍只做稳定客户/订单定位。
- `AiSuggestion` / `Ocr` 在安全前提下同步 ViewModel 选中状态。
- `FollowUp` 先只保留定位字段，UI 留待最终阶段统一处理。

## RecentlyActiveCustomer 降噪规则

- 时间窗口固定为最近 7 天。
- 总量上限固定为 5 条。
- 同一客户只保留 1 条最近活跃任务。
- 若同客户存在以下更高优先级任务，则移除其 `RecentlyActiveCustomer`：
  - `FollowUpOverdue`
  - `DraftNotSent`
  - `ReplyNeeded`
  - `AiSuggestionPending`
  - `OcrNotConverted`
  - `FollowUpToday`
- 最近活跃排序依据：
  - `ActivityLog.CreatedAt`
  - `ConversationMessage.MessageTime`
  - `Customer.UpdatedAt`

## 去重与排序规则

- 统一使用 `DedupeKey` 做内部去重。
- 同一实体同一任务类型只保留 1 条。
- `RecentlyActiveCustomer` 在最终集合阶段再次做客户级压制，避免和高优任务并存。
- 排序统一收口到 service comparer：
  1. `FollowUpOverdue`
  2. `DraftNotSent` 且 `ActionHint = ReplyToCustomer`
  3. `ReplyNeeded`
  4. `DraftNotSent` 其他状态
  5. `AiSuggestionPending`
  6. `OcrNotConverted`
  7. `FollowUpToday`
  8. `RecentlyActiveCustomer`
- 同层级内按 `OccurredAt` 倒序，再按稳定比较器保证重复读取顺序一致。

## Pipeline 加固点

- 客户不存在时安全回退到 `New`。
- 指定 `OrderId` 但订单缺失时安全回退到 `New`。
- `WaitingPayment` 在只有本地 `sent` 信号、没有终态确认时标记 fallback。
- `Closed` 订单：
  - `DealStage = Won` 直接推导 `Fulfilled`
  - `DealStage = Lost` 直接推导 `Lost`
  - 有履约完成信号时 fallback 到 `Fulfilled`
  - 否则 fallback 到 `Lost`
- 不修改 `OrderStatus / DealStage`，不写回 schema。

## QA 覆盖

- `run-p3-1-workbench-smoke.ps1`
  - 补充深链字段断言
- `run-p3-2-pipeline-smoke.ps1`
  - 补充 `WaitingPayment / Closed / MissingCustomer` fallback 断言
- `run-p3-4-workbench-logic-smoke.ps1`
  - 覆盖深链字段、降噪、去重、稳定排序、pipeline fallback、baseline reset
- `run-p3-full-regression.ps1`
  - 纳入 P3.4 smoke

## QA 结果

2026-04-29 已执行并通过：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-4-workbench-logic-smoke.ps1
```

## UI 说明

- 本轮未做 UI。
- 本轮未做 XAML。
- 本轮未做布局、样式、视觉、125% 缩放或交互体验处理。
- 后续界面接入只应消费本轮新增字段和 ViewModel 状态，统一留到最终阶段处理。
