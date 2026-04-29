# Release Check

日期：2026-04-30
阶段：P4.4 Release Freeze
口径：当前 `main` 主线

## 当前发布结论口径

- 当前项目是 `WPF + SQLite` 的 PC 成交助手主线项目
- 当前发布前验收基线为 `QA-only baseline`
- 当前 `main` 没有正式 tests project
- 未正式恢复 tests project 前，`dotnet test` 暂不作为必跑项
- 当前剩余非视觉阻断项：无
- UI / 视觉 / XAML / 125% 缩放验收不属于 Codex 当前阶段，由 Antigravity 后续负责

## 发布前必跑命令

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
git status --short
```

## 通过标准

- `dotnet build` 通过
- 当前 QA smoke 脚本全部通过
- `git status --short` 输出符合预期，未出现误提交产物或未知高风险变更

## 当前不是发布阻断项的内容

- `dotnet test`
  - 只有在 `main` 恢复正式 tests project 后，才升级为必跑项
- 历史小程序 / 云函数文档
  - 仅作 legacy / historical reference，不作为当前发布判断依据
- UI / 视觉终验
  - 当前阶段不在本入口内

## 相关文档

- `README.md`
- `docs/QA_AUTOMATION.md`
- `tools/qa/README.md`
- `docs/P4_ENGINEERING_AUDIT.md`
