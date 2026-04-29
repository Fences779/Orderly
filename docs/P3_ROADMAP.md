# P3 Roadmap

## 当前范围

已完成：

- P3.1 `今日行动 / 待处理工作台`
- P3.2 `Pipeline 只读阶段推导`
- P3.4 `Workbench 非 UI 逻辑加固`
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
  - `PipelineStageResolver`
  - `PipelineStageRuleEngine`
- App 接入
  - `WorkbenchTaskListItem`
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

## 后续边界

- UI 留待最终阶段统一处理。
- 后续如果做界面接入，只消费本轮新增的深链字段和 ViewModel 状态，不反向推动 schema 变更。
