# P2.7 Local Backup Summary

## 1. 做了什么

- 新增 `IBackupService / LocalBackupService`，把本地备份能力收口在 service 层。
- 在 `设置` 低频区新增最小入口：
  - `导出备份`
  - `校验备份`
  - `最近备份状态`
- 导出格式固定为本地 `JSON`，文件名建议为 `orderly-backup-yyyyMMdd-HHmmss.json`。
- 导出成功写 `SyncRecord(local-backup)`，并写 `ActivityLog.BackupExported`。
- 校验成功 / 失败分别写：
  - `ActivityLog.BackupValidationSucceeded`
  - `ActivityLog.BackupValidationFailed`
- 新增 `tools/qa/run-p2-7-backup-smoke.ps1`，覆盖导出、解析、校验、篡改失败、留痕、`reset-qa-data` 回稳。

## 2. 没做什么

- 没有接云端。
- 没有接微信 / 闲鱼 / 任何平台同步。
- 没有做多设备实时同步。
- 没有做账号体系。
- 没有做复杂冲突解决。
- 没有做自动上传。
- 没有开放生产库覆盖恢复。
- 没有新增复杂备份管理器。
- 没有重构订单主链路。

## 3. UI 入口

- 位置：`设置 -> 本地备份`
- 元素：
  - `导出备份`
  - `校验备份`
  - `最近备份状态`
- 关键文件：
  - `src/Orderly.App/Views/MainWindow.xaml`
  - `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs`

## 4. 备份架构

- UI / ViewModel：
  - `MainViewModel -> IBackupService`
- Service：
  - `src/Orderly.Core/Services/IBackupService.cs`
  - `src/Orderly.Data/Services/LocalBackupService.cs`
- 留痕：
  - `ISyncService / LocalSyncService`
  - `SyncRecordRepository`
  - `ActivityLogRepository`
- 数据源：
  - 直接读取本地 SQLite 当前核心表，不接云端，不调用平台。

## 5. 备份范围

- 当前导出表：
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
- 当前未导出：
  - `AppSettings`
  - 环境变量 / API Key / token
  - `SyncRecords` 历史表本体

说明：
- `OcrResults.SourcePath` 如果是绝对路径，会在备份中裁成文件名，避免把本机隐私路径写入 JSON。

## 6. 文件格式

- 顶层字段：
  - `schemaVersion`
  - `app = "Orderly"`
  - `exportedAt`
  - `tables`
  - `counts`
  - `checksum`
- 编码：UTF-8
- 当前 schemaVersion：`1`

## 7. 数据路径

- 导出时使用用户选择路径。
- 设置页保存对话框默认目录：
  - `Documents\Orderly\Backups`
- 数据库路径仍为：
  - `%LocalAppData%\Orderly\orderly.db`

## 8. 校验规则

- 至少检查：
  - JSON 可解析
  - 顶层对象存在
  - `schemaVersion` 存在
  - `app` 为 `Orderly`
  - `exportedAt` 存在且格式可解析
  - `counts` 存在
  - `tables` 存在
  - 关键表存在：
    - `Customers`
    - `Deals`
    - `Orders`
    - `ActivityLogs`
    - `ConversationMessages`
    - `AiSuggestions`
    - `OcrResults`
  - 关键表 `counts` 存在
  - `counts` 与表数组长度一致
  - `checksum` 存在且可通过

## 9. SyncRecord / ActivityLog

- `SyncRecord`
  - `EntityType = "local-backup"`
  - 导出成功：
    - `SyncStatus = Synced`
    - `MetadataJson` 包含：
      - `backupPath`
      - `counts`
      - `checksum`
      - `schemaVersion`
      - `exportedAt`
      - `createdBy = "p2.7"`
  - 导出失败：
    - `SyncStatus = Failed`
    - `MetadataJson` 包含：
      - `backupPath`
      - `errorSummary`
      - `createdBy = "p2.7"`

- `ActivityLog`
  - `BackupExported`
  - `BackupValidationSucceeded`
  - `BackupValidationFailed`

## 10. 恢复策略

- 本轮不开放“从备份恢复”。
- 原因：
  - 当前数据库里有订单、消息、AI 建议、OCR、ActivityLog 等关联数据，直接覆盖恢复风险高。
  - 在未加“空库恢复 / QA 库恢复 / 二次确认 / 幂等导入”前，不应对生产库开放写回。
- P2.8 建议做受控恢复：
  - 仅支持空库恢复或 QA / 测试库恢复
  - 恢复前强提示
  - 二次确认
  - 恢复过程写 `SyncRecord / ActivityLog`
  - 恢复后自动跑校验摘要

## 11. QA

- Build：
  - `dotnet build Orderly.sln -c Debug`
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
- P2.7 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-7-backup-smoke.ps1`

P2.7 smoke 覆盖点：
- 可生成备份 JSON
- JSON 可解析
- `schemaVersion / exportedAt / counts / checksum` 存在
- 核心表 counts 存在
- 校验备份成功
- 无效 JSON 校验失败
- `SyncRecord(local-backup)` 导出成功记录存在
- `ActivityLog` 有导出 / 校验成功 / 校验失败记录
- `reset-qa-data` 后 `ActivityLogs / SyncRecords` 基线恢复稳定

UIA 覆盖说明：
- 本轮未新增按钮级 UIA 自动化。
- 已补 service / repository 级 smoke。

## 12. 下一步 P2.8 建议

- 做“受控恢复”而不是直接开放覆盖恢复。
- 增加“仅空库恢复 / QA 库恢复”限制。
- 增加恢复前预检查：
  - 目标库是否为空
  - schemaVersion 是否兼容
  - checksum 是否通过
- 增加恢复后摘要与异常回滚策略。
