# P3.5 Search And Actions Summary

## 范围结论

- 本轮只实现逻辑、数据层、Service、Projection、ViewModel 非视觉接入。
- 没有修改任何 `.xaml`。
- 没有修改 `MainWindow.xaml`。
- UI、搜索框、布局、样式、125% 缩放和交互体验全部留到最后统一处理。

## 已完成

- 新增全局搜索模型与服务：
  - `src/Orderly.Core/Models/SearchRequest.cs`
  - `src/Orderly.Core/Models/SearchResultItem.cs`
  - `src/Orderly.Core/Models/SearchResultSet.cs`
  - `src/Orderly.Core/Models/SearchResultType.cs`
  - `src/Orderly.Core/Models/SearchResultPriority.cs`
  - `src/Orderly.Core/Services/IGlobalSearchService.cs`
  - `src/Orderly.Data/Services/LocalGlobalSearchService.cs`
- 新增今日行动筛选模型与 overload：
  - `src/Orderly.Core/Models/WorkbenchTaskFilter.cs`
  - `src/Orderly.Core/Models/WorkbenchTaskQuery.cs`
  - `src/Orderly.Core/Services/IWorkbenchTaskService.cs`
- 新增只读快捷动作 projection：
  - `src/Orderly.Core/Models/QuickAction.cs`
  - `src/Orderly.Core/Models/QuickActionType.cs`
  - `src/Orderly.Data/Services/QuickActionProjectionBuilder.cs`
- `MainViewModel` 只接入非视觉属性/命令，不写 UI。

## 搜索范围

- `Customers`：名称、联系方式、备注、外部 Id、原始 payload
- `Orders / MerchantOrders`：标题、状态、金额、需求、外部 Id、原始 payload
- `ConversationMessages`：内容、渠道、方向、发送人、metadata
- `AiSuggestions`：建议文本、原因、状态、metadata
- `OcrResults`：文件名、OCR 文本、状态、错误信息、metadata
- `FollowUps`：标题、内容、状态、计划时间
- `ActivityLogs`：标题、描述、类型、操作人、metadata

## 搜索排序规则

- 标题/客户名等主字段匹配权重大于内容字段，内容字段大于 metadata。
- 同分结果按 `OccurredAt` 最近优先。
- 再按实体级别排序：`Customer / Order` 主对象优先于日志类对象。
- 最后按实体 Id 和结果 Id 做稳定排序。

## SearchResult 字段

- `Id`
- `Type`
- `Title`
- `Summary`
- `CustomerId`
- `CustomerName`
- `OrderId`
- `RelatedEntityType`
- `RelatedEntityId`
- `OccurredAt`
- `MatchedField`
- `Score`
- `Priority`
- `PipelineStage`
- `TargetSection`
- `ActionHint`

## WorkbenchTask 筛选规则

- 支持 `TaskType / Priority / PipelineStage / CustomerId / OrderId / TargetSection`
- 支持 `OnlyActionable / IncludeRecentlyActive / OccurredFrom / OccurredTo`
- 筛选在默认 projection、去重和排序之后执行
- 不传筛选时，默认行为与 P3.4 一致

## QuickAction 生成规则

- 只生成动作列表，不触发真实副作用
- `DraftNotSent`：`ReviewDraft / CopyDraft / MarkSent`
- `AiSuggestionPending`：`ReviewSuggestion`
- `OcrNotConverted`：`ConvertOcrToMessage`
- `FollowUpToday / FollowUpOverdue`：`CompleteFollowUp / SnoozeFollowUp`
- `ReplyNeeded`：`ReplyToCustomer`
- 如果有客户/订单上下文，补 `OpenCustomer / OpenOrder`
- 动作支持 `IsEnabled / DisabledReason`

## ViewModel 非视觉接入

- `SearchQuery`
- `SearchResults`
- `SelectedSearchResult`
- `RunSearchCommand`
- `RefreshSearchCommand`
- `ClearSearchCommand`
- `OpenSearchResultCommand`
- `WorkbenchTaskFilter`

## QA

2026-04-29 已执行并通过：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
```

`run-p3-5-search-smoke.ps1` 覆盖：

- 空 query / 短 query 返回空结果
- 七类实体统一搜索
- `TargetSection / ActionHint` 字段存在
- 搜索排序稳定
- `WorkbenchTaskFilter` 的 `TaskType` 和 `IncludeRecentlyActive`
- `WorkbenchTaskQuery` 的 `Limit`
- QuickAction 只投影、不产生副作用
- `reset-qa-data` 后基线恢复稳定

## 已知限制

- 没有做 UI 接入，因此搜索框、结果列表和筛选控件还未可视化。
- 当前搜索是本地 Repository 聚合扫描，没有全文索引。
- `OpenSearchResultCommand` 只做定位，不执行发送、复制、OCR 转消息、完成跟进、延期跟进。

## 下一步建议

- 继续做 P3 逻辑闭环：把搜索结果与工作台动作接到最终 UI 前，先补更多命令路由与深链一致性验证。
- 等 P3 逻辑稳定后，再统一处理 `MainWindow.xaml`、搜索框、筛选栏和视觉层。
