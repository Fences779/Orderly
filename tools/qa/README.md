# QA Tools

正式 QA 脚本统一放在 `tools/qa`。

## 前置条件

- 已完成 Debug 构建：`src\Orderly.App\bin\Debug\net8.0-windows\Orderly.App.exe`
- 执行前关闭已有 `Orderly.App`
- 从 `powershell.exe` 启动也可以，脚本会自动切到 `pwsh`

## 脚本列表

- `run-qa-data-status.ps1`
  - 输出当前 QA 数据统计
- `reset-qa-data.ps1`
  - 清理并重建 QA 基线
- `clear-qa-data.ps1`
  - 只清理 QA 数据
- `run-uia-smoke.ps1`
  - P1 UIA 主链路 smoke
  - 控件模式优先：`ValuePattern / InvokePattern / SelectionItemPattern / ExpandCollapsePattern`
  - 显式等待窗口 ready、控件可用、焦点稳定、文本回读
  - `SendWait` 仅作键盘兜底，异常会记录并重试，不会静默吞掉
- `run-p1-smoke.ps1`
  - `reset -> status -> UIA -> status`
- `run-p2-full-regression.ps1`
  - 顺序编排 `run-p2-1` 到 `run-p2-9`
- `run-p3-1-workbench-smoke.ps1`
  - 校验今日行动任务生成、排序稳定、QA reset 恢复
- `run-p3-2-pipeline-smoke.ps1`
  - 校验阶段推导、fallback、不落库、不破坏旧状态
- `run-p3-4-workbench-logic-smoke.ps1`
  - 校验深链字段补齐、去重、recently active 降噪、排序稳定
- `run-p3-5-search-smoke.ps1`
  - 校验统一搜索、WorkbenchTask 筛选、QuickAction 投影、QA reset 恢复
- `run-p3-6-navigation-smoke.ps1`
  - 校验 route service、TargetSection / ActionHint 收口、QuickAction 风险标记、ViewModel 安全定位
- `run-p3-full-regression.ps1`
  - `dotnet build -> P1 -> P2 full -> P3.1 -> P3.2 -> P3.4 -> P3.5 -> P3.6`

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

## 当前结论

2026-04-29：

- P1 smoke：PASS
- P2 full regression：PASS
- P3.1 workbench smoke：PASS
- P3.2 pipeline smoke：PASS
- P3.4 workbench logic smoke：PASS
- P3.5 search/action smoke：PASS
- P3.6 navigation route smoke：PASS
- P3 full regression：PASS

## 注意

- 默认回归不联网，不调用真实 AI API
- 本轮 QA 不验证任何 UI / XAML 视觉改动
- 路由 smoke 只验证逻辑和非视觉状态，不验证最终 UI 焦点表现
- `artifacts\qa-smoke\` 只存运行产物，不提交
- 如果某条 smoke 失败，先看该脚本输出，再看 `artifacts\qa-smoke\<timestamp>\`
- `run-uia-smoke.ps1` 日志会标明当前步骤、重试次数和最终失败原因，便于定位焦点/输入 race
