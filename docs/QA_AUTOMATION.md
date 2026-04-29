# QA 自动化

## 目录

- `tools/qa/`

## 2026-04-29 已执行结果

- `dotnet build Orderly.sln -c Debug`：PASS
- `run-p1-smoke.ps1`：PASS
- `run-p2-full-regression.ps1`：PASS
- `run-p3-1-workbench-smoke.ps1`：PASS
- `run-p3-2-pipeline-smoke.ps1`：PASS
- `run-p3-4-workbench-logic-smoke.ps1`：PASS
- `run-p3-5-search-smoke.ps1`：PASS
- `run-p3-6-navigation-smoke.ps1`：PASS
- `run-p3-full-regression.ps1`：PASS

P3.6.1 QA stability 修复结论：

- 既有 P1 UIA 主链路存在多处裸 `SendWait`，对窗口 ready、控件 enable、焦点稳定和输入回读没有显式等待。
- P3.6 新增 `INavigationRouteService` 注入和 `MainViewModel` 导航状态字段，启动服务图比之前更重，但没有修改 UI/XAML 或业务流程；这类启动时序波动会放大旧 UIA race。
- 已将 `run-uia-smoke.ps1` 改为 `ValuePattern / InvokePattern / SelectionItemPattern / ExpandCollapsePattern` 优先，`SendWait` 仅保留为异常可见的键盘兜底，并增加重试与日志。

## 常用命令

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-1-workbench-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-4-workbench-logic-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 脚本说明

- `run-p1-smoke.ps1`
  - `reset -> qa-data-status -> UIA smoke -> qa-data-status`
  - `run-uia-smoke.ps1` 现在会显式等待主窗口 ready、控件 enabled/visible、焦点稳定、文本回读一致
- `run-p2-full-regression.ps1`
  - 顺序执行 `P2.1 -> P2.9`，失败即停
- `run-p3-1-workbench-smoke.ps1`
  - 验证 `WorkbenchTask` 生成
  - 验证 `DraftNotSent / AiSuggestionPending / OcrNotConverted / FollowUpToday / FollowUpOverdue`
  - 验证排序稳定
  - 验证 `reset-qa-data` 后基线恢复
  - 不联网、不调真实 AI API
- `run-p3-2-pipeline-smoke.ps1`
  - 构造 `New / Contacted / Interested / Quoted / DraftPrepared / WaitingPayment / Paid / Fulfilled / Lost`
  - 验证 fallback
  - 验证 `PipelineStage` 不落库
  - 验证不改 `OrderStatus / DealStage`
- `run-p3-5-search-smoke.ps1`
  - 验证统一搜索覆盖 `Customer / Order / Message / AiSuggestion / OcrResult / FollowUp / ActivityLog`
  - 验证空 query / 短 query 返回空结果
  - 验证搜索排序稳定
  - 验证 `WorkbenchTaskFilter / WorkbenchTaskQuery`
  - 验证 QuickAction 只投影不触发副作用
- `run-p3-6-navigation-smoke.ps1`
  - 验证统一路由模型和 route service
  - 验证 `TargetSection / ActionHint` 收口
  - 验证 disabled QuickAction、未知 ActionHint、缺失实体 fallback
  - 验证高风险动作只返回 `RequiresUserAction`
  - 验证 `OpenSearchResultCommand / OpenWorkbenchTaskCommand` 只定位不副作用
- `run-p3-full-regression.ps1`
  - `build -> P1 -> P2 full -> P3.1 -> P3.2 -> P3.4 -> P3.5 -> P3.6`
  - 失败即停，输出 `PASS / FAILED`

## 边界

- 默认回归全部是本地 smoke
- 不依赖公网
- 不调用真实 AI API
- 不验证平台发送
- 不覆盖最终 UI / XAML 视觉表现
- 不覆盖最终 UI 路由焦点视觉表现
- `artifacts/` 是运行产物，不提交

## 已知未覆盖

- 任务卡片的最终视觉 polish
- 搜索框与筛选栏的最终 UI 接入
- 真实外部平台回执
