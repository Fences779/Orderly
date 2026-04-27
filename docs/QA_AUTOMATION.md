# QA 自动化

## 工具目录

- `tools/qa/`

## 常用命令

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
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

## 注意事项

- Orderly 是 WPF 桌面应用，不使用 localhost，不使用浏览器 DOM。
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
