# P2.9 Restore Preview Summary

## 1. 做了什么

- 在 `设置 -> 本地备份` 增加 `恢复预览` 区域。
- 扩展 `BackupRestorePreviewResult`，补齐恢复前摘要和门控字段：
  - `BackupPath`
  - `FileName`
  - `ExportedAt`
  - `SchemaVersion`
  - `Checksum`
  - `IsChecksumValid`
  - `Counts`
  - `TargetState`
  - `TargetCounts`
  - `WillClearQaData`
  - `CanRestore`
  - `RefuseReason`
- `LocalBackupService.PreviewRestoreAsync()` 继续保持只读，不写入任何恢复数据。
- `MainViewModel.BackupCommands` 增加 preview 状态持有、风险确认勾选、恢复按钮门控和确认重置逻辑。
- 恢复执行仍只复用 P2.8 `RestoreBackupAsync()`，没有改恢复核心写入流程。

## 2. 没做什么

- 没有开放生产库覆盖恢复。
- 没有做 merge。
- 没有做云同步。
- 没有做多设备同步。
- 没有做自动导入。
- 没有接微信 / 闲鱼 / 平台。
- 没有新增复杂恢复向导。
- 没有新增按钮级 UIA。

## 3. UI 入口

- 位置：`设置 -> 本地备份 -> 恢复预览`
- 关键元素：
  - `选择备份文件`
  - `生成恢复预览`
  - `恢复预览`
  - `风险提示`
  - `确认勾选框`
  - `恢复到空库/QA库`
  - `恢复状态`

关键文件：
- `src/Orderly.App/Views/MainWindow.xaml`
- `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs`

## 4. Preview 架构

- UI / ViewModel：
  - `MainViewModel -> IBackupService.PreviewRestoreAsync()`
- Service：
  - `src/Orderly.Core/Services/IBackupService.cs`
  - `src/Orderly.Data/Services/LocalBackupService.cs`
- 执行恢复：
  - preview 只做只读预检查
  - 恢复写入仍走 `RestoreBackupAsync()`

## 5. Preview 字段

- 备份摘要：
  - `BackupPath`
  - `FileName`
  - `ExportedAt`
  - `SchemaVersion`
  - `Checksum`
  - `IsChecksumValid`
- 数据摘要：
  - `Counts`
  - `TargetCounts`
- 目标状态：
  - `TargetState`
  - `WillClearQaData`
- 恢复门控：
  - `CanRestore`
  - `RefuseReason`

## 6. 确认机制

- 用户选择新备份文件后：
  - 清空旧 preview
  - 清空旧确认勾选
- 用户重新生成 preview 后：
  - 清空旧确认勾选
- 恢复按钮必须同时满足：
  - 当前 preview 属于允许恢复状态
  - 用户已勾选风险确认
- 恢复完成或失败后：
  - 刷新最近备份状态
  - 刷新当前 preview 状态
  - 清空确认勾选

## 7. 允许 / 拒绝恢复规则

- 允许：
  - `TargetState = Empty`
  - `TargetState = QaOnly`，并在恢复时先清理 QA 数据
- 拒绝：
  - `TargetState = ProductionNonEmpty`
  - `TargetState = Unknown`
  - `checksum` 校验失败
  - `schemaVersion` 不支持
  - 关键表缺失
  - `counts` 与实际表数组长度不一致
  - JSON 结构无效

## 8. QA 结果

- Build：
  - `dotnet build Orderly.sln -c Debug`
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
- P2.9 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-9-restore-preview-smoke.ps1`

P2.9 smoke 覆盖点：
- 生成 P2.7 格式备份。
- Preview 返回 `exportedAt / schemaVersion / checksum / counts`。
- 空库 preview：`CanRestore = true`。
- QA-only preview：`CanRestore = true` 且 `WillClearQaData = true`。
- 非空生产库 preview：`CanRestore = false` 且有 `RefuseReason`。
- checksum 错误 preview：`CanRestore = false`。
- ViewModel 层验证：
  - 确认前 `RestoreBackupCommand.CanExecute = false`
  - 切换文件后确认状态清空
  - 重新 preview 后确认状态清空
- 串跑 `tools\qa\run-p2-8-restore-smoke.ps1`，确认 P2.8 restore 边界仍通过。
- `reset-qa-data` 后默认 QA 基线稳定。

UIA 覆盖说明：
- 本轮未新增按钮级 UIA。
- 当前重点 QA 仍在 service / ViewModel / 脚本层。

## 9. 下一步 P2.10 建议

- 如果继续做恢复能力，优先补“恢复前差异摘要”和“恢复后结果摘要”，不要直接放开生产覆盖。
- 如需提高恢复安全性，先做恢复前快照或临时备份，再讨论更高风险入口。
- 如需更细化 UI 验证，可单独补设置页恢复区的按钮级 UIA，而不是扩展恢复逻辑本身。
