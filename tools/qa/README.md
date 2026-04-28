# QA Tools

正式 QA 脚本统一放在 `tools/qa`，只从仓库根推导路径，不依赖 `artifacts` 里的临时脚本，也不写死任何用户目录。

## 前置条件

- 已在仓库根完成 Debug 构建，目标文件为 `src\Orderly.App\bin\Debug\net8.0-windows\Orderly.App.exe`。
- 执行前请关闭已有 `Orderly.App` 实例；正式脚本默认不会强杀现有进程。
- 从 `powershell.exe` 调用也可以；脚本会自动跳转到 `pwsh` 7，以兼容 UTF-8 和 .NET 8 依赖。
- 截图与报告输出到 `artifacts\qa-smoke\<timestamp>`。`artifacts/` 已被 `.gitignore` 忽略，不会进入提交。

## 脚本列表

- `run-qa-data-status.ps1`
  - 调用应用内正式入口 `--qa-data-status`，输出当前 QA 数据统计。
- `reset-qa-data.ps1`
  - 调用 `--reset-qa-data`，先清理再重建 QA 基线，适合作为 smoke 前置。
- `clear-qa-data.ps1`
  - 调用 `--clear-qa-data`，只清理已识别的 QA 数据。
- `run-uia-smoke.ps1`
  - 默认执行 `reset-qa-data -> --qa-mode 启动 -> UIA 写入 -> SQLite 回读 -> reset-qa-data`。
  - 使用 `MainWindowHandle` 等待并定位 WPF 主窗口。
  - 覆盖：切到客户/订单页、选择 `p13qa-customer-a`、AddOrder 保存、切回工作台、AddNote 模板插入并保存、SQLite 回读订单/备注/ActivityLog、最终恢复 QA 基线。
  - 失败时输出明确错误，并保存截图、`smoke-report.json`、`smoke-log.txt`。
  - 可传 `-SkipReset` 跳过开头 reset；可传 `-SkipFinalReset` 跳过结尾 reset。
- `run-p1-smoke.ps1`
  - 默认执行 `reset-qa-data -> run-qa-data-status -> run-uia-smoke --qa-mode -SkipReset -> run-qa-data-status`。
  - 可传 `-SkipReset` 跳过最外层 reset。
- `run-p2-1-message-smoke.ps1`
  - 覆盖手工录入沟通记录、按订单回读、QA reset 恢复。
- `run-p2-2-ai-suggestion-smoke.ps1`
  - 覆盖 AI 建议生成、接受、拒绝、`ActivityLog` 留痕、QA reset 恢复。
- `run-p2-3-auto-reply-smoke.ps1`
  - 覆盖回复草稿准备、复制、标记已发送、拒绝、`ActivityLog` 留痕、QA reset 恢复。
- `run-p2-4-ai-provider-smoke.ps1`
  - 覆盖本地 stub、缺配置、DeepSeek 缺 key、Provider 失败 fallback。
- `run-p2-5-ocr-smoke.ps1`
  - 覆盖 OCR task 创建、fallback 文本、转沟通记录、metadata、`ActivityLog` 留痕、QA reset 恢复。
- `run-p2-6-manual-send-smoke.ps1`
  - 覆盖草稿复制、剪贴板写入、手工确认已发送、metadata、`ActivityLog` 留痕、QA reset 恢复。
- `run-p2-7-backup-smoke.ps1`
  - 覆盖本地备份导出、校验、篡改备份失败、`SyncRecord(local-backup)`、`ActivityLog` 留痕、QA reset 恢复。
- `run-p2-8-restore-smoke.ps1`
  - 覆盖空库恢复、QA-only 受控恢复、非空生产库拒绝、非法 JSON/错误 checksum 拒绝、恢复审计与基线恢复。
- `run-p2-9-restore-preview-smoke.ps1`
  - 覆盖恢复预览摘要、ViewModel 门控、风险确认重置，并串跑 P2.8 restore smoke。
- `run-p2-full-regression.ps1`
  - 顺序编排 `run-p2-1-message-smoke.ps1` 到 `run-p2-9-restore-preview-smoke.ps1`，失败即停，输出清晰 PASS/FAILED。
- `run-p2-5-deepseek-live-smoke.ps1`
  - 读取用户环境变量里的 `DEEPSEEK_API_KEY / ORDERLY_AI_PROVIDER / ORDERLY_AI_MODEL`。
  - 发起一次真实 DeepSeek 联网请求，仅校验返回内容非空。
  - 输出脱敏摘要，并把结果写入 `artifacts\qa-smoke\<timestamp>\deepseek-live-smoke-report.json`。
  - 该脚本不属于默认 closeout 回归，也不被 `run-p2-full-regression.ps1` 调用。

## 常用命令

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\reset-qa-data.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\clear-qa-data.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-uia-smoke.ps1
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
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-5-deepseek-live-smoke.ps1
```

## 当前覆盖范围

- 覆盖：
  - QA 数据状态、清理、重置。
  - `--qa-mode` 主窗口启动。
  - AddOrder 真实保存。
  - AddNote 模板插入后真实保存。
  - SQLite 中订单、备注、运行态 ActivityLog 回读。
  - smoke 结束后恢复 QA 基线。
- 额外说明：
  - P2.0 已纳入 QA 数据状态/清理/重置的表级维护范围：`ConversationMessages / AiSuggestions / OcrResults / SyncRecords`。
- `run-p1-smoke.ps1` 仍然只承担 P1 回归保护，不证明 P2.0 功能通过。
- `run-p2-full-regression.ps1` 只串行编排本地 smoke，不调用真实 AI API、不依赖公网。
- 不覆盖：
  - 登录流程。
  - 搜索 / 筛选、FollowUp 完成 / 延期 / 取消、Deal 推进、状态切换的完整端到端动作。
  - 托盘、快捷键、悬浮窗。
  - 视觉比对、125% 缩放。
  - 真实 AI/OCR/同步/外部发送能力。

## 失败排查

- 如果提示未找到可执行文件，先执行：

```powershell
dotnet build .\src\Orderly.App\Orderly.App.csproj -c Debug
```

- 如果提示已有 `Orderly.App` 进程，请先手动关闭旧实例再重试。
- 如果提示 `pwsh` 不存在，请先安装 PowerShell 7。
- 如果 UIA 报找不到主窗口或 AutomationId，先看 `artifacts\qa-smoke\<timestamp>\smoke-report.json` 与对应截图，再判断是启动失败、焦点问题还是 UI 结构变更。
