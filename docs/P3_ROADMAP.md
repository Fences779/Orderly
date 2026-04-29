# P3 Roadmap

日期：2026-04-29
状态：P3 完成，closeout 以 `docs/P3_CLOSEOUT_SUMMARY.md` 为准

## 当前结论

- P3 已完成并完成收口。
- 本阶段主线是工作台、只读阶段推导、搜索/筛选/快捷动作 projection、统一路由语义和 QA 回归。
- 本阶段不包含最终 UI/XAML 接入，不包含视觉 polish，不包含 125% 缩放收尾。
- `p4/customer-import-export-wip` 已隔离，不属于 P3 closeout。

## 阶段提交

- `01ad22d` `feat: p3.1 workbench tasks`
  - 落地 `WorkbenchTask`、`PipelineStage`、`PipelineStageResolver`、`PipelineStageRuleEngine`
  - 建立今日行动 / 待处理工作台基础投影
- `d833459` `chore: p3 qa and docs`
  - 补 `P3.1 / P3.2` 文档
  - 新增 `run-p3-1-workbench-smoke.ps1`
  - 新增 `run-p3-2-pipeline-smoke.ps1`
  - 新增 `run-p3-full-regression.ps1`
- `e6c8398` `feat: p3.4 workbench logic hardening`
  - 补齐 `WorkbenchTask` 深链字段
  - 加固 `RecentlyActiveCustomer` 降噪、去重、稳定排序
  - 加固 `PipelineStage` fallback
- `97b6108` `feat: p3.5 search and action projections`
  - 落地统一全局搜索 projection
  - 落地 `WorkbenchTaskFilter / WorkbenchTaskQuery`
  - 落地 `QuickAction` 只读投影
- `501e984` `feat: p3.6 navigation route hardening`
  - 落地 `INavigationRouteService`
  - 统一 `SearchResult / WorkbenchTask / QuickAction` 的 `TargetSection / ActionHint` 语义
  - 收口 ViewModel 安全定位路径
- `bff98fb` `test: stabilize p1 uia smoke`
  - 加固 `run-uia-smoke.ps1`
  - 让 `run-p1-smoke.ps1` 与 `run-p3-full-regression.ps1` 稳定通过

## 当前已确认能力

- 今日行动 / 待处理工作台
- `PipelineStage` 只读阶段推导
- `FollowUpToday / FollowUpOverdue`
- `WorkbenchTask` 深链字段
- `RecentlyActiveCustomer` 降噪
- `WorkbenchTask` 去重与稳定排序
- 全局搜索 projection
- `WorkbenchTask` 筛选
- `QuickAction` projection
- `NavigationRouteService`
- `SearchResult / WorkbenchTask / QuickAction` 路由语义统一
- `P3 full regression` 已通过

## 当前明确未做

- 不改任何 `.xaml`
- 不改 `src/Orderly.App/Views/MainWindow.xaml`
- 不做最终 UI 接入
- 不做视觉 polish
- 不做 125% 缩放收尾
- 不接平台联动
- 不自动发送
- 不接云同步
- 不开放生产库覆盖恢复
- 不合入 `p4/customer-import-export-wip`

## QA 结论

2026-04-29 已执行：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

- `dotnet build Orderly.sln -c Debug`：PASS
- `run-p1-smoke.ps1`：PASS
- `run-p2-full-regression.ps1`：PASS
- `run-p3-full-regression.ps1`：PASS

## P4 接续状态

- `P4.1` 已完成：工程健康审计，见 `docs/P4_ENGINEERING_AUDIT.md`
- `P4.2A` 已完成：文档口径收口与发布验收入口统一
- `P4.2B` 已完成：低风险工程债清理完成
- `P4.3` 不进入本轮：当前接受 `QA-only baseline`，不恢复 tests project
- `P4.4` 已完成：Release freeze 归档见 `docs/P4_RELEASE_FREEZE.md`
- 当前发布基线：`QA-only baseline`
- `dotnet test` 当前不是必跑项
- 当前剩余非视觉阻断项：无
- UI / 视觉 / XAML / 125% 缩放阶段交由 Antigravity

## 后续边界

- Final UI 阶段只应消费现有 `WorkbenchTask`、`SearchResult`、`QuickAction`、`NavigationRouteResult` 和相关 ViewModel 状态。
- 不应为 UI 再新增平行路由逻辑、平行任务状态源或 schema 变更。
- 当前发布前验收入口以 `docs/RELEASE_CHECK.md` 为准。
