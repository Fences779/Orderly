using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
    public async Task<BackupResult> RestoreBackupAsync(
        string backupPath,
        bool clearQaDataIfNeeded = false,
        string createdBy = "p2.8",
        CancellationToken cancellationToken = default)
    {
        var entityId = GenerateBackupEntityId();
        BackupValidationResult validation = new()
        {
            BackupPath = backupPath
        };
        TargetInspectionResult inspection = TargetInspectionResult.Empty();
        var tagForQaScope = false;
        var restoreStartedLogged = false;

        try
        {
            validation = await ValidateCoreAsync(backupPath, cancellationToken);
            tagForQaScope = IsQaTaggedBackup(validation.Manifest);

            await using (var inspectionConnection = _connectionFactory.CreateConnection())
            {
                await inspectionConnection.OpenAsync(cancellationToken);
                inspection = await InspectTargetAsync(inspectionConnection, transaction: null, cancellationToken);
            }

            tagForQaScope = tagForQaScope || inspection.TargetState == BackupRestoreTargetState.QaDatabase;

            if (!validation.IsValid || validation.Manifest is null)
            {
                throw new InvalidOperationException(string.Join("；", validation.Errors));
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            inspection = await InspectTargetAsync(connection, transaction, cancellationToken);
            tagForQaScope = tagForQaScope || inspection.TargetState == BackupRestoreTargetState.QaDatabase;

            if (inspection.TargetState == BackupRestoreTargetState.NonEmptyProductionDatabase)
            {
                throw new InvalidOperationException("目标库包含非 QA 生产数据，禁止覆盖恢复。");
            }

            var qaDataCleared = false;
            if (inspection.TargetState == BackupRestoreTargetState.QaDatabase)
            {
                if (!clearQaDataIfNeeded)
                {
                    throw new InvalidOperationException("目标库当前为 QA/测试数据，恢复前必须先清理 QA 数据。");
                }

                var maintenanceService = new QaDataMaintenanceService(_connectionFactory);
                await maintenanceService.ClearAsync(connection, transaction, cancellationToken);
                qaDataCleared = true;

                var clearedInspection = await InspectTargetAsync(connection, transaction, cancellationToken);
                if (clearedInspection.TargetState != BackupRestoreTargetState.EmptyDatabase)
                {
                    throw new InvalidOperationException("清理 QA 数据后目标库仍非空，已停止恢复。");
                }
            }

            foreach (var tableName in RestoreOrderedTableNames)
            {
                if (!validation.Manifest.Tables.TryGetValue(tableName, out var tableElement))
                {
                    throw new InvalidOperationException($"备份缺少恢复所需表：{tableName}。");
                }

                await InsertTableRowsAsync(connection, transaction, tableName, tableElement, cancellationToken);
            }

            await RestoreLauncherSnapshotAsync(validation.Manifest, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var restoredAt = DateTimeOffset.Now;
            await _activityLogRepository.CreateAsync(new ActivityLog
            {
                Type = ActivityType.BackupRestoreStarted,
                Title = "开始恢复本地备份",
                Description = $"准备恢复 {Path.GetFileName(backupPath)}",
                Operator = RestoreOperator,
                MetadataJson = BuildRestoreStartedMetadataJson(
                    backupPath,
                    createdBy,
                    inspection.TargetState,
                    validation.Manifest.Counts,
                    validation.Manifest.Checksum,
                    validation.Manifest.IntegrityAlgorithm,
                    validation.Manifest.IntegrityKeyScope,
                    validation.Manifest.IntegrityTag,
                    validation.Manifest.SchemaVersion,
                    restoredAt,
                    tagForQaScope)
            }, cancellationToken);
            restoreStartedLogged = true;

            var syncRecord = await _syncService.MarkSyncedAsync(
                RestoreEntityType,
                entityId,
                metadataJson: BuildRestoreSuccessMetadataJson(
                    backupPath,
                    validation.Manifest,
                    createdBy,
                    inspection.TargetState,
                    qaDataCleared,
                    restoredAt,
                    tagForQaScope),
                cancellationToken: cancellationToken);

            await _activityLogRepository.CreateAsync(new ActivityLog
            {
                Type = ActivityType.BackupRestoreSucceeded,
                Title = "恢复本地备份成功",
                Description = $"已恢复 {Path.GetFileName(backupPath)}",
                Operator = RestoreOperator,
                MetadataJson = BuildRestoreActivityMetadataJson(
                    backupPath,
                    validation.Manifest,
                    createdBy,
                    inspection.TargetState,
                    qaDataCleared,
                    restoredAt,
                    tagForQaScope,
                    operation: "restore-succeeded")
            }, cancellationToken);

            return new BackupResult
            {
                SyncRecordId = syncRecord.Id,
                SyncStatus = syncRecord.SyncStatus,
                BackupPath = backupPath,
                Manifest = validation.Manifest,
                TargetState = inspection.TargetState,
                QaDataCleared = qaDataCleared,
                CompletedAt = restoredAt
            };
        }
        catch (Exception ex)
        {
            var errorSummary = SanitizeBackupErrorSummary(ex.Message, backupPath);
            if (!restoreStartedLogged)
            {
                await _activityLogRepository.CreateAsync(new ActivityLog
                {
                    Type = ActivityType.BackupRestoreStarted,
                    Title = "开始恢复本地备份",
                    Description = $"准备恢复 {Path.GetFileName(backupPath)}",
                    Operator = RestoreOperator,
                    MetadataJson = BuildRestoreStartedMetadataJson(
                        backupPath,
                        createdBy,
                        inspection.TargetState,
                        validation.Manifest?.Counts,
                        validation.Manifest?.Checksum,
                        validation.Manifest?.IntegrityAlgorithm,
                        validation.Manifest?.IntegrityKeyScope,
                        validation.Manifest?.IntegrityTag,
                        validation.Manifest?.SchemaVersion,
                        DateTimeOffset.Now,
                        tagForQaScope)
                }, cancellationToken);
            }

            var failureMetadata = BuildRestoreFailureMetadataJson(
                backupPath,
                createdBy,
                errorSummary,
                inspection.TargetState,
                tagForQaScope);

            await _syncService.MarkFailedAsync(
                RestoreEntityType,
                entityId,
                errorSummary,
                failureMetadata,
                cancellationToken);

            await _activityLogRepository.CreateAsync(new ActivityLog
            {
                Type = ActivityType.BackupRestoreFailed,
                Title = "恢复本地备份失败",
                Description = $"恢复失败：{errorSummary}",
                Operator = RestoreOperator,
                MetadataJson = failureMetadata
            }, cancellationToken);

            throw;
        }
    }

    private async Task InsertTableRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        JsonElement tableElement,
        CancellationToken cancellationToken)
    {
        if (tableElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"表 {tableName} 的备份结构无效。");
        }

        foreach (var row in tableElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"表 {tableName} 存在非对象行。");
            }

            var properties = row.EnumerateObject().ToArray();
            var columnNames = string.Join(", ", properties.Select(static property => $"\"{property.Name}\""));
            var parameterNames = string.Join(", ", properties.Select((_, index) => $"$p{index}"));

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {tableName} ({columnNames}) VALUES ({parameterNames});";

            for (var index = 0; index < properties.Length; index++)
            {
                command.Parameters.AddWithValue($"$p{index}", ConvertJsonValue(properties[index].Value));
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
