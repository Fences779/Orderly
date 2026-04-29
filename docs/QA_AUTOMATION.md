# QA 自动化

日期：2026-04-29

## 当前结论

- 当前 main 的 P3 closeout 基线已验证通过。
- 默认 closeout 命令就是 `build + P1 smoke + P2 full regression + P3 full regression`。
- QA 自动化只覆盖本地逻辑与最小 UIA 主链路，不覆盖最终 UI/XAML 视觉验收。

## 本轮已执行结果

- `dotnet build Orderly.sln -c Debug`：PASS
- `run-p1-smoke.ps1`：PASS
- `run-p2-full-regression.ps1`：PASS
- `run-p3-1-workbench-smoke.ps1`：PASS
- `run-p3-2-pipeline-smoke.ps1`：PASS
- `run-p3-4-workbench-logic-smoke.ps1`：PASS
- `run-p3-5-search-smoke.ps1`：PASS
- `run-p3-6-navigation-smoke.ps1`：PASS
- `run-p3-full-regression.ps1`：PASS

## Closeout 命令

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 脚本说明

- `run-p1-smoke.ps1`
  - `reset -> qa-data-status -> UIA smoke -> qa-data-status`
  - 依赖 `run-uia-smoke.ps1`
- `run-uia-smoke.ps1`
  - P1 UIA 主链路 smoke
  - 优先使用 `ValuePattern / InvokePattern / SelectionItemPattern / ExpandCollapsePattern`
  - 对窗口 ready、控件可用、焦点稳定、文本回读增加显式等待和重试
- `run-p2-full-regression.ps1`
  - 顺序执行 `P2.1 -> P2.9`
  - 失败即停
- `run-p3-1-workbench-smoke.ps1`
  - 验证 `WorkbenchTask` 生成、任务类型、排序稳定、QA baseline 恢复
- `run-p3-2-pipeline-smoke.ps1`
  - 验证 `PipelineStage` 推导、fallback、不落库、不写回旧状态
- `run-p3-4-workbench-logic-smoke.ps1`
  - 验证深链字段、`RecentlyActiveCustomer` 降噪、去重、稳定排序、pipeline fallback
- `run-p3-5-search-smoke.ps1`
  - 验证统一搜索、`WorkbenchTaskFilter`、`WorkbenchTaskQuery`、`QuickAction` 只投影不副作用
- `run-p3-6-navigation-smoke.ps1`
  - 验证 `INavigationRouteService`
  - 验证 `TargetSection / ActionHint` 语义收口
  - 验证高风险动作 `RequiresUserAction`
  - 验证 `OpenSearchResultCommand / OpenWorkbenchTaskCommand` 只定位不副作用
- `run-p3-full-regression.ps1`
  - `build -> P1 -> P2 full -> P3.1 -> P3.2 -> P3.4 -> P3.5 -> P3.6`
  - 失败即停，输出 `PASS / FAILED`

## 边界

- 默认回归不联网
- 默认回归不调用真实 AI API
- 不验证平台发送
- 不验证云同步
- 不验证生产库覆盖恢复
- 不覆盖最终 UI / XAML 视觉表现
- 不覆盖 125% 缩放收尾
- `artifacts/` 只存运行产物，不提交

## P4 隔离说明

- `p4/customer-import-export-wip` 上的 customer import/export、database migration/migrator、tests project 不属于当前 main closeout 基线。
- 当前 main 的 QA 结论不包含该分支的未合入能力。
