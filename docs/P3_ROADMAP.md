# P3 Roadmap

## 当前范围

已完成：

- P3.1 `今日行动 / 待处理工作台`
- P3.2 `Pipeline 只读阶段推导`
- P3.4 `Workbench 非 UI 逻辑加固`
- P3.5 `非 UI 搜索 / 筛选 / 快捷动作数据能力`
- P3 QA / 回归总控

本轮明确不做：

- 不改任何 `.xaml`
- 不改数据库 schema
- 不改 `src/Orderly.Data/Sqlite/DatabaseInitializer.cs`
- 不接云同步、微信、闲鱼或其他平台
- 不自动发送
- 不自动截屏
- 不改 AI Provider / OCR / 备份恢复核心链路
- 不做最终 UI polish
- 不做搜索框、布局、样式、视觉和交互体验

## 已落地架构

- Core Models
  - `WorkbenchTask`
  - `WorkbenchTaskType`
  - `WorkbenchTaskPriority`
  - `PipelineStage`
  - `PipelineStageSnapshot`
- Core Services
  - `IWorkbenchTaskService`
  - `IPipelineStageResolver`
- Data Services
  - `LocalWorkbenchTaskService`
  - `LocalGlobalSearchService`
  - `PipelineStageResolver`
  - `PipelineStageRuleEngine`
- App 接入
  - `WorkbenchTaskListItem`
  - `SearchResultListItem`
  - `MainViewModel` 仅增加非视觉字段、选择同步、刷新命令

## P3.4 逻辑加固

- 深链字段：
  - `DealId`
  - `MessageId`
  - `AiSuggestionId`
  - `OcrResultId`
  - `FollowUpId`
  - `TargetSection`
  - `ActionHint`
  - `DedupeKey`
- `RecentlyActiveCustomer` 降噪：
  - 仅最近 7 天
  - 最多 5 条
  - 同客户最多 1 条
  - 如果已有更高优先级任务，则移除该客户的 `RecentlyActiveCustomer`
- 任务去重与排序：
  - 同一实体同一任务类型按 `DedupeKey` 去重
  - 排序规则集中在 service comparer
  - 重复读取结果保持稳定
- Pipeline 推导加固：
  - `WaitingPayment` 仅凭本地 sent 信号时标记 fallback
  - `Fulfilled / Lost` 对 `Closed` 订单的回退条件更明确
  - 缺失客户/订单上下文时安全回退到 `New`

## QA 状态

2026-04-29 已执行并通过：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-1-workbench-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-4-workbench-logic-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 当前限制

- 本轮只准备数据和 ViewModel 状态，不处理 UI/XAML。
- `OpenWorkbenchTaskCommand` 不触发发送、OCR 转换、跟进完成等副作用。
- `PipelineStage` 仍然是只读 projection，不写回 `Orders / Deals`。

## P3.5 搜索 / 筛选 / 快捷动作

- 本轮只做数据层、Service、Projection、ViewModel 非视觉接入。
- 没有修改任何 `.xaml`，`MainWindow.xaml` 保持不变。
- 全局搜索范围：
  - `Customers`
  - `Orders / MerchantOrders`
  - `ConversationMessages`
  - `AiSuggestions`
  - `OcrResults`
  - `FollowUps`
  - `ActivityLogs`
- 搜索规则：
  - `query` 为空返回空结果
  - `trim` 后长度小于 `2` 返回空结果
  - 大小写不敏感
  - 中文按 `contains` 匹配
  - 默认最多返回 `50` 条
- 搜索排序：
  - 字段强匹配优先：标题/客户名 > 内容 > metadata
  - 同分时按 `OccurredAt` 最近优先
  - 再按实体类型优先：`Customer / Order` 主对象优先于日志类
  - 最后按实体 Id / 结果 Id 稳定排序
- `SearchResultItem` 字段：
  - `Id / Type / Title / Summary`
  - `CustomerId / CustomerName / OrderId`
  - `RelatedEntityType / RelatedEntityId`
  - `OccurredAt / MatchedField / Score / Priority`
  - `PipelineStage / TargetSection / ActionHint`
- `WorkbenchTask` 筛选：
  - `TaskType / Priority / PipelineStage / CustomerId / OrderId / TargetSection`
  - `OnlyActionable / IncludeRecentlyActive / OccurredFrom / OccurredTo`
  - 默认调用路径不传筛选，行为保持与 P3.4 一致
- QuickAction 规则：
  - 只生成动作列表，不执行真实发送、复制、转换、完成、延期
  - `DraftNotSent` -> `ReviewDraft / CopyDraft / MarkSent`
  - `AiSuggestionPending` -> `ReviewSuggestion`
  - `OcrNotConverted` -> `ConvertOcrToMessage`
  - `FollowUpToday / FollowUpOverdue` -> `CompleteFollowUp / SnoozeFollowUp`
  - `ReplyNeeded` -> `ReplyToCustomer`
  - 导航动作按上下文补 `OpenCustomer / OpenOrder`
- ViewModel 非视觉接入：
  - `SearchQuery / SearchResults / SelectedSearchResult`
  - `RunSearchCommand / RefreshSearchCommand / ClearSearchCommand / OpenSearchResultCommand`
  - `WorkbenchTaskFilter` 状态接入 `RefreshWorkbenchTasks`

## 后续边界

- UI 留待最终阶段统一处理。
- 后续如果做界面接入，只消费本轮新增的深链字段和 ViewModel 状态，不反向推动 schema 变更。
- 下一步更适合继续做 P3 逻辑闭环，而不是提前处理 UI。
