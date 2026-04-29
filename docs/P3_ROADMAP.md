# P3 Roadmap

## 本轮范围

本轮完成：

- P3.1 `今日行动 / 待处理工作台`
- P3.2 `Pipeline 只读阶段推导`
- P3 QA / 回归总控

本轮明确不做：

- 不改数据库 schema
- 不改 `src/Orderly.Data/Sqlite/DatabaseInitializer.cs`
- 不接云同步、微信、闲鱼或其他平台
- 不自动发送
- 不改 AI Provider / OCR / 备份恢复核心逻辑
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
  - `MainViewModel` 仅增加绑定、选择、刷新命令
  - `MainWindow.xaml` 工作台左列新增“今日行动”卡片

## UI 入口

- 文件：`src/Orderly.App/Views/MainWindow.xaml`
- 位置：工作台页左列顶部，在现有客户/订单列表上方
- 行为：
  - 点击任务只定位 `Customer / Order`
  - 加载现有详情区
  - 不新建任何业务流

## FollowUp 接入情况

- 已接入 `FollowUps` 表的 `ScheduledAt / Status / CompletedAt`
- 已支持：
  - `FollowUpToday`
  - `FollowUpOverdue`
- 当前未把 `Order.NextFollowUpAt` 单独投影成新任务类型，避免和 `FollowUps` 形成双状态源

## QA 状态

2026-04-29 已执行并通过：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-1-workbench-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 已知限制

- `今日行动` 点击后只保证稳定定位到客户/订单，不保证精确滚动到具体 AI 建议或 OCR 项。
- `RecentlyActiveCustomer` 依赖现有 `ActivityLog / ConversationMessage / Customer.LastContactAt / UpdatedAt / Order.UpdatedAt`，属于弱信号。
- `PipelineStage` 是启发式只读推导，不替代 `DealStage / OrderStatus`。

## 下一步建议

- P3 后续如果继续做交互闭环，优先补任务深链定位。
- 如果近期要压噪音，可继续细化 `RecentlyActiveCustomer` 的去重和阈值。
- 如果后续要把阶段展示扩大到详情区，继续保持只读 projection，不落库。
