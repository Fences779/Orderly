# P2.8 Controlled Restore Summary

## 1. 做了什么

- 在 `设置 -> 本地备份` 增加最小恢复入口：
  - `选择备份文件`
  - `校验备份`
  - `恢复到空库/QA库`
  - `恢复状态`
- 扩展 `IBackupService / LocalBackupService`：
  - `PreviewRestoreAsync`
  - `RestoreBackupAsync`
- 复用 P2.7 备份格式、`BackupManifest / BackupResult / BackupValidationResult`、`ISyncService / LocalSyncService`、`ActivityLog`。
- 恢复前统一做备份校验 + 目标库状态预检查。
- 恢复后写 `SyncRecord(local-restore)` 与恢复审计日志。

## 2. 没做什么

- 没有开放非空生产库覆盖恢复。
- 没有接云端。
- 没有做多设备同步。
- 没有做冲突合并。
- 没有做自动导入。
- 没有接微信 / 闲鱼 / 平台。
- 没有重构订单主链路。

## 3. UI 入口

- 位置：`设置 -> 本地备份`
- 关键元素：
  - `选择备份文件`
  - `校验备份`
  - `恢复到空库/QA库`
  - `恢复状态`
  - 风险提示：`仅支持空库或测试库恢复，不覆盖已有生产数据。`

关键文件：
- `src/Orderly.App/Views/MainWindow.xaml`
- `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs`

## 4. 恢复架构

- UI / ViewModel：
  - `MainViewModel -> IBackupService`
- Service：
  - `src/Orderly.Core/Services/IBackupService.cs`
  - `src/Orderly.Data/Services/LocalBackupService.cs`
- 目标库状态判断：
  - 空库
  - QA/测试库
  - 非空生产库
- QA 清理复用：
  - `src/Orderly.Data/Services/QaDataMaintenanceService.cs`
- 留痕复用：
  - `ISyncService / LocalSyncService`
  - `SyncRecordRepository`
  - `ActivityLogRepository`

## 5. 恢复前预检查

- JSON 可解析。
- `schemaVersion` 必须受支持，当前仅支持 `1`。
- `checksum` 必须通过。
- `counts` 与 `tables` 实际数组长度必须一致。
- 恢复所需关键表必须存在：
  - `Customers`
  - `Deals`
  - `Orders`
  - `FollowUps`
  - `CustomerNotes`
  - `PriceAdjustments`
  - `ActivityLogs`
  - `ConversationMessages`
  - `AiSuggestions`
  - `OcrResults`
- 目标库必须满足允许恢复条件。

## 6. 允许 / 拒绝恢复条件

- 允许：
  - 目标库为空。
  - 目标库是 QA-only / 测试库，并且先清理 QA 数据。
- 拒绝：
  - 目标库存在非 QA 业务数据。
  - 备份 JSON 无法解析。
  - `schemaVersion` 不支持。
  - `checksum` 错误。
  - `counts` 与 `tables` 不一致。
  - 恢复关键表缺失。

## 7. 恢复范围和顺序

恢复表范围：
- `Customers`
- `Deals`
- `Orders`
- `FollowUps`
- `CustomerNotes`
- `PriceAdjustments`
- `ActivityLogs`
- `ConversationMessages`
- `AiSuggestions`
- `OcrResults`

写入顺序：
- `Customers`
- `Deals`
- `Orders`
- `FollowUps`
- `CustomerNotes`
- `PriceAdjustments`
- `ActivityLogs`
- `ConversationMessages`
- `AiSuggestions`
- `OcrResults`

说明：
- 为保持外键关系，恢复时保留备份内 `Id`。
- `ActivityLogs` 恢复完成后会额外追加 `BackupRestoreStarted / BackupRestoreSucceeded / BackupRestoreFailed` 审计记录，因此恢复后 `ActivityLogs` 总数会比备份多审计行。

## 8. SyncRecord / ActivityLog

- `SyncRecord`
  - `EntityType = "local-restore"`
  - 成功：
    - `SyncStatus = Synced`
    - `MetadataJson` 包含：
      - `backupPath`
      - `counts`
      - `checksum`
      - `schemaVersion`
      - `restoredAt`
      - `createdBy = "p2.8"`
  - 失败：
    - `SyncStatus = Failed`
    - `MetadataJson` 包含：
      - `backupPath`
      - `errorSummary`
      - `createdBy = "p2.8"`

- `ActivityLog`
  - `BackupRestoreStarted`
  - `BackupRestoreSucceeded`
  - `BackupRestoreFailed`

## 9. QA 结果

- Build：
  - `dotnet build Orderly.sln -c Debug`
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
- P2.8 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-8-restore-smoke.ps1`

P2.8 smoke 覆盖点：
- 先生成一份 P2.7 格式备份。
- 备份校验通过。
- 空库恢复成功。
- QA-only 目标库先清理后恢复成功。
- 恢复后核心表 counts 与备份一致。
- `SyncRecord(local-restore)` 成功记录存在。
- `ActivityLog` 有 `BackupRestoreStarted / BackupRestoreSucceeded`。
- 非空生产库恢复被拒绝。
- 无效 JSON 恢复失败。
- checksum 错误恢复失败。
- `reset-qa-data` / `QaDataMaintenanceService.ResetAsync()` 后基线恢复稳定。

UIA 覆盖说明：
- 本轮没有新增按钮级 UIA。
- 当前恢复验证以 service / repository / QA 脚本为主。

## 10. 下一步 P2.9 建议

- 增加恢复前摘要弹窗，展示备份 counts、目标库状态和将要清理的范围。
- 如果后续要支持更多恢复场景，优先做“独立测试库恢复”和“只读 preview 明细”，不要直接开放生产覆盖。
- 如需开放更高风险恢复，先补回滚快照、二次确认和更细粒度表差异展示。
