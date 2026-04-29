# QA 自动化

日期：2026-04-29
口径：当前 `main` 主线

## 当前结论

- 当前 `main` 的发布前验收基线是 `QA-only baseline`
- 当前 `main` 没有正式 tests project
- `dotnet test` 暂不作为发布前必跑项，除非后续在 `main` 恢复正式测试工程
- `dotnet build` 与当前 smoke 脚本是必跑项
- UI / 视觉 / XAML / 125% 缩放验收不在 Codex 当前阶段范围内，后续由 Antigravity 负责

## 当前发布前必跑命令

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
git status --short
```

## 脚本说明

- `run-qa-data-status.ps1`
  - 输出当前 QA 数据状态
- `run-p3-2-pipeline-smoke.ps1`
  - 验证 `PipelineStage` 推导、fallback、不写回旧状态
- `run-p3-5-search-smoke.ps1`
  - 验证统一搜索、筛选与 `QuickAction` 只读投影
- `run-p3-6-navigation-smoke.ps1`
  - 验证统一导航路由、`TargetSection / ActionHint`、高风险动作标记
- `run-p1-smoke.ps1`
  - 验证最小 UIA 主链路
  - 内含 `reset -> qa-data-status -> UIA smoke -> qa-data-status`

## 可选扩展回归

以下命令可作为更高覆盖率的扩展验证，但不是当前 P4.2A 发布前最低必跑集合：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 边界

- 默认回归不联网
- 默认回归不调用真实 AI API
- 不验证云同步
- 不验证生产库覆盖恢复
- 不覆盖最终 UI / XAML 视觉表现
- 不覆盖 125% 缩放收尾
- `artifacts/` 只存运行产物，不提交

## 统一入口

- 发布主入口：`docs/RELEASE_CHECK.md`
- QA 脚本入口：`tools/qa/README.md`

