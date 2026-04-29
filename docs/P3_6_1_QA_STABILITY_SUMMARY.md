# P3.6.1 QA Stability Summary

日期：2026-04-29

## 目标

- 修复 `run-p1-smoke.ps1` / `run-p3-full-regression.ps1` 被既有 UIA `SendWait` 抖动阻塞的问题。
- 只改 QA 脚本和文档，不改任何 `.xaml`、业务主链路、数据库 schema。

## 根因判断

- `tools/qa/run-uia-smoke.ps1` 之前在点击、文本输入、ComboBox 选择上直接依赖 `System.Windows.Forms.SendKeys.SendWait`。
- 脚本缺少对以下状态的显式等待：
  - 主窗口 ready
  - 控件 enabled / visible
  - 焦点稳定
  - 文本写入后的回读确认
- P3.6 给 `App.xaml.cs` / `MainViewModel` 增加了导航路由服务注入和状态字段，虽然不改变 UI/XAML 和业务行为，但会让启动服务图更重，进而放大旧 UIA race。

## 修复

- 点击优先走 `InvokePattern / SelectionItemPattern / LegacyIAccessiblePattern`，最后才用原生鼠标点击兜底。
- 文本输入优先走 `ValuePattern`，其次 `LegacyIAccessiblePattern.SetValue`，最后才用 `SendWait` 兜底。
- ComboBox 选择优先走 `ExpandCollapsePattern + ListItem 选择`，失败后再走键盘兜底。
- 为窗口、控件、焦点、文本回读增加显式等待与 retry。
- `SendWait` 异常现在会记录到日志并重试；失败会真实报错，不会假 PASS。

## 验证

- `dotnet build Orderly.sln -c Debug`：PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1`：PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1`：PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1`：PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1`：PASS

## 影响面

- 仅影响 QA 自动化脚本和文档。
- 不影响 P1/P2/P3 业务功能、UI/XAML、数据库结构。
