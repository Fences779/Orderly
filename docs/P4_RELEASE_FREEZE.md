# P4 Release Freeze

日期：2026-04-30
阶段：P4.4 Release Freeze
状态：已归档
当前发布基线：`QA-only baseline`

## 当前工程状态

- 当前主线是 `WPF + SQLite` 的 PC 成交助手。
- P1 / P2 / P3 / P4.1 / P4.2A / P4.2B 已完成。
- 当前剩余非视觉阻断项：无。
- 工程验收与最终 UI / 视觉验收分离；Antigravity 后续接手 UI/XAML/视觉/125% 缩放/Final Visual QA。

## 发布基线结论

- 当前接受 `QA-only baseline` 作为发布前工程验收基线。
- 当前 `main` 没有正式 tests project，本轮不恢复。
- `dotnet test` 不是当前发布必跑项。
- 当前发布前统一入口以 `docs/RELEASE_CHECK.md` 为准。

## 未恢复 tests project 的原因

- 当前 `Orderly.sln` 未纳入正式 tests project，`main` 现状没有稳定可维护的 `dotnet test` 基线。
- 现阶段已有稳定的 `build + QA smoke` 发布口径，继续在 P4.4 恢复 tests project 会扩大范围，不符合本轮 freeze 目标。
- tests project 恢复应单独作为后续工程任务处理，而不是并入本轮发布冻结。

## 本轮 release check

2026-04-30 已执行：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
git status --short
```

结果摘要：

- `dotnet build Orderly.sln -c Debug`：PASS（`0 warnings / 0 errors`）
- `run-qa-data-status.ps1`：PASS
- `run-p3-2-pipeline-smoke.ps1`：PASS
- `run-p3-5-search-smoke.ps1`：PASS
- `run-p3-6-navigation-smoke.ps1`：PASS
- `run-p1-smoke.ps1`：PASS
- `git status --short`：工作区非干净；存在既有文档/代码改动与未跟踪源码文件，需要由主线持有者在正式提交/冻结前自行收口提交集

## 已完成阶段

- P1
- P2
- P3
- P4.1
- P4.2A
- P4.2B

## 本轮明确不处理

- tests project 恢复
- CustomerImportExport
- database migration
- MainViewModel 大重构
- FollowUpRepository 硬编码状态 SQL
- CompletedAt / Cancelled 语义清理
- SelectedAiSuggestion 命令刷新细节

## 交接边界

- Antigravity 下一阶段负责：
  - UI / XAML
  - 视觉 polish
  - 125% 缩放
  - Final Visual QA
- Antigravity 不应修改：
  - tests project 恢复方案
  - CustomerImportExport
  - database migration / schema
  - MainViewModel 大重构
  - 新增并行状态源、并行路由逻辑、并行业务链
