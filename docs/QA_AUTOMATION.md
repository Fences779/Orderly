# QA 自动化

## 目录

- `tools/qa/`

## 2026-04-29 已执行结果

- `dotnet build Orderly.sln -c Debug`：PASS
- `run-p1-smoke.ps1`：PASS
- `run-p2-full-regression.ps1`：PASS
- `run-p3-1-workbench-smoke.ps1`：PASS
- `run-p3-2-pipeline-smoke.ps1`：PASS
- `run-p3-5-search-smoke.ps1`：PASS
- `run-p3-full-regression.ps1`：PASS

## 常用命令

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-1-workbench-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 脚本说明

- `run-p1-smoke.ps1`
  - `reset -> qa-data-status -> UIA smoke -> qa-data-status`
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
- `run-p3-full-regression.ps1`
  - `build -> P1 -> P2 full -> P3.1 -> P3.2 -> P3.4 -> P3.5`
  - 失败即停，输出 `PASS / FAILED`

## 边界

- 默认回归全部是本地 smoke
- 不依赖公网
- 不调用真实 AI API
- 不验证平台发送
- 不覆盖最终 UI / XAML 视觉表现
- `artifacts/` 是运行产物，不提交

## 已知未覆盖

- 任务卡片的最终视觉 polish
- 搜索框与筛选栏的最终 UI 接入
- 真实外部平台回执
