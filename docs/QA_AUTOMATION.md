# QA 自动化

## 工具目录

- `tools/qa/`

## 常用命令

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-1-message-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-2-ai-suggestion-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-3-auto-reply-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-4-ai-provider-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-5-ocr-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-6-manual-send-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-7-backup-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-8-restore-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-9-restore-preview-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-uia-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\reset-qa-data.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\clear-qa-data.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
```

## QA 数据标记规则

- 基线种子继续使用：`[P1.3_QA]`
- 新的运行态 smoke 数据统一使用：`[P1_QA_RUNTIME]`
- 兼容清理的历史标记：
  - `[P1.4.1_QA]`
  - 已知旧乱码前缀：`【P。3——QA`

## 工具说明

- `tools/qa/run-qa-data-status.ps1`
  - 调用 `Orderly.App.exe --qa-data-status`
- `tools/qa/reset-qa-data.ps1`
  - 调用 `Orderly.App.exe --reset-qa-data`
- `tools/qa/clear-qa-data.ps1`
  - 调用 `Orderly.App.exe --clear-qa-data`
- `tools/qa/run-uia-smoke.ps1`
  - 默认执行 `reset -> --qa-mode 启动 -> AddOrder -> AddNote 模板插入 -> SQLite 回读 -> reset`
  - 通过 `MainWindowHandle` 等待并定位 WPF 主窗口
  - 不依赖浏览器、localhost 或 DOM
- `tools/qa/run-p1-smoke.ps1`
  - 默认编排 `reset -> status -> uia smoke -> status`
- `tools/qa/run-p2-1-message-smoke.ps1`
  - 覆盖手工录入沟通记录、按订单回读、QA reset 恢复。
- `tools/qa/run-p2-2-ai-suggestion-smoke.ps1`
  - 覆盖 AI 建议生成、接受、拒绝、`ActivityLog` 留痕、QA reset 恢复。
- `tools/qa/run-p2-3-auto-reply-smoke.ps1`
  - 覆盖回复草稿准备、复制、标记已发送、拒绝、`ActivityLog` 留痕、QA reset 恢复。
- `tools/qa/run-p2-4-ai-provider-smoke.ps1`
  - 覆盖本地 stub、缺配置 fallback、缺 key fallback、失败 fallback。
- `tools/qa/run-p2-5-ocr-smoke.ps1`
  - 覆盖 OCR task 创建、fallback 文本、转沟通记录、幂等 metadata、`ActivityLog` 留痕、QA reset 恢复。
- `tools/qa/run-p2-6-manual-send-smoke.ps1`
  - 覆盖草稿复制、剪贴板写入、metadata、手工确认已发送、`ActivityLog` 留痕、QA reset 恢复。
- `tools/qa/run-p2-7-backup-smoke.ps1`
  - 覆盖本地备份导出、校验、篡改备份失败、`SyncRecord(local-backup)`、`ActivityLog` 留痕、QA reset 恢复。
- `tools/qa/run-p2-8-restore-smoke.ps1`
  - 覆盖空库恢复、QA-only 受控恢复、非空生产库拒绝、非法 JSON/错误 checksum 拒绝、恢复审计与基线恢复。
- `tools/qa/run-p2-9-restore-preview-smoke.ps1`
  - 覆盖恢复预览摘要、按钮门控、风险确认清空、并串跑 P2.8 恢复边界。
- `tools/qa/run-p2-full-regression.ps1`
  - 顺序编排 `P2.1 -> P2.9` 正式 smoke，失败即停。
- `tools/qa/run-p2-5-deepseek-live-smoke.ps1`
  - 联网真实 provider 验证脚本，不属于默认 closeout 回归，不纳入 `run-p2-full-regression.ps1`。

## 注意事项

- Orderly 是 WPF 桌面应用，不使用 localhost，不使用浏览器 DOM。
- `run-p2-full-regression.ps1` 与 closeout 默认回归不依赖公网，也不调用真实 AI API。
- UIA 受 WPF 虚拟化和焦点状态影响，失败时先看截图和 `smoke-report.json`。
- 运行 `run-uia-smoke.ps1` 前请关闭已有 `Orderly.App` 实例；脚本不会强杀外部正在使用的实例。
- 从 `powershell.exe` 启动也可以；脚本会自动切到 `pwsh` 7 执行。
- 如果 UIA 某一步不稳定，必须结合 SQLite 回读结果如实判断，不允许伪造 PASS。

## 输出路径

- `artifacts\qa-smoke\<timestamp>\`

## 提交约束

- `artifacts/` 是运行产物目录，不提交进 git。
- `tools/qa/` 只放正式脚本和说明，不放截图、临时报告或一次性调试脚本。

## 当前未覆盖

- 搜索 / 筛选的完整断言
- 今日 / 逾期 / 明日跟进动作闭环
- FollowUp 完成 / 延期 / 取消
- Deal 阶段推进
- 客户 / 订单状态切换
- 视觉检查、125% 缩放、Final Visual QA
- 真实 AI API、真实 OCR 引擎、真实平台发送、云同步
