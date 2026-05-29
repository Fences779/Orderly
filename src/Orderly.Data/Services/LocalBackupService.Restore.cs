using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
    public async Task<BackupRestorePreviewResult> PreviewRestoreAsync(
        string backupPath,
        string createdBy = "p2.8",
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateCoreAsync(backupPath, cancellationToken);

        TargetInspectionResult inspection;
        await using (var connection = _connectionFactory.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            inspection = await InspectTargetAsync(connection, transaction: null, cancellationToken);
        }

        return BuildRestorePreviewResult(backupPath, validation, inspection);
    }

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
                        validation.Manifest?.SchemaVersion,
                        DateTimeOffset.Now,
                        tagForQaScope)
                }, cancellationToken);
            }

            var failureMetadata = BuildRestoreFailureMetadataJson(
                backupPath,
                createdBy,
                ex.Message,
                inspection.TargetState,
                tagForQaScope);

            await _syncService.MarkFailedAsync(
                RestoreEntityType,
                entityId,
                ex.Message,
                failureMetadata,
                cancellationToken);

            await _activityLogRepository.CreateAsync(new ActivityLog
            {
                Type = ActivityType.BackupRestoreFailed,
                Title = "恢复本地备份失败",
                Description = $"恢复失败：{ex.Message}",
                Operator = RestoreOperator,
                MetadataJson = failureMetadata
            }, cancellationToken);

            throw;
        }
    }

    private async Task<TargetInspectionResult> InspectTargetAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var qaScopedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tableName in TargetInspectionTableNames)
        {
            var totalCount = await CountRowsAsync(connection, transaction, tableName, qaOnly: false, cancellationToken);
            counts[tableName] = totalCount;

            var qaPredicate = GetQaScopePredicate(tableName);
            qaScopedCounts[tableName] = string.IsNullOrWhiteSpace(qaPredicate)
                ? 0
                : await CountRowsAsync(connection, transaction, tableName, qaOnly: true, cancellationToken);
        }

        var totalRows = counts.Values.Sum();
        var nonQaRows = counts.Sum(pair => pair.Value - qaScopedCounts.GetValueOrDefault(pair.Key));

        var targetState = totalRows == 0
            ? BackupRestoreTargetState.EmptyDatabase
            : nonQaRows == 0
                ? BackupRestoreTargetState.QaDatabase
                : BackupRestoreTargetState.NonEmptyProductionDatabase;

        return new TargetInspectionResult(targetState, counts, qaScopedCounts);
    }

    private async Task<int> CountRowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName,
        bool qaOnly,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(1) FROM {tableName} WHERE {BuildTargetAssessmentPredicate(tableName, qaOnly)};";
        QaDataScope.AddScopeParameters(command);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static string BuildTargetAssessmentPredicate(string tableName, bool qaOnly)
    {
        var predicates = new List<string>
        {
            BuildExportPredicate(tableName)
        };

        var auditExclusion = GetAuditExclusionPredicate(tableName);
        if (!string.IsNullOrWhiteSpace(auditExclusion))
        {
            predicates.Add(auditExclusion);
        }

        if (qaOnly)
        {
            var qaPredicate = GetQaScopePredicate(tableName);
            if (!string.IsNullOrWhiteSpace(qaPredicate))
            {
                predicates.Add(qaPredicate);
            }
        }

        return string.Join(" AND ", predicates.Select(static predicate => $"({predicate})"));
    }

    private static string BuildExportPredicate(string tableName)
    {
        return tableName switch
        {
            "ReplyTemplates" => "1=1",
            _ => "DeletedAt IS NULL"
        };
    }

    private static string? GetQaScopePredicate(string tableName)
    {
        return tableName switch
        {
            "Customers" => QaDataScope.BuildCustomerScopePredicate(),
            "Deals" => QaDataScope.BuildDealScopePredicate(),
            "Orders" => QaDataScope.BuildOrderScopePredicate(),
            "FollowUps" => QaDataScope.BuildFollowUpScopePredicate(),
            "CustomerNotes" => QaDataScope.BuildNoteScopePredicate(),
            "PriceAdjustments" => QaDataScope.BuildPriceAdjustmentScopePredicate(),
            "ActivityLogs" => QaDataScope.BuildActivityLogScopePredicate(),
            "ConversationMessages" => QaDataScope.BuildConversationMessageScopePredicate(),
            "AiSuggestions" => QaDataScope.BuildAiSuggestionScopePredicate(),
            "OcrResults" => QaDataScope.BuildOcrResultScopePredicate(),
            "SyncRecords" => QaDataScope.BuildSyncRecordScopePredicate(),
            _ => null
        };
    }

    private static string? GetAuditExclusionPredicate(string tableName)
    {
        return tableName switch
        {
            "ActivityLogs" => $"""
                Type NOT IN ({(int)ActivityType.BackupExported}, {(int)ActivityType.BackupValidationSucceeded}, {(int)ActivityType.BackupValidationFailed}, {(int)ActivityType.BackupRestoreStarted}, {(int)ActivityType.BackupRestoreSucceeded}, {(int)ActivityType.BackupRestoreFailed})
                AND NOT (
                    Type = {(int)ActivityType.SyncFailed}
                    AND (
                        instr(ifnull(MetadataJson, ''), '"mode":"{BackupEntityType}"') > 0
                        OR instr(ifnull(MetadataJson, ''), '"mode":"{RestoreEntityType}"') > 0
                    )
                )
                """,
            "SyncRecords" => $"EntityType NOT IN ('{BackupEntityType}', '{RestoreEntityType}')",
            _ => null
        };
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

    private async Task RestoreLauncherSnapshotAsync(BackupManifest manifest, CancellationToken cancellationToken)
    {
        if (_launcherConnectionFactory is null)
        {
            return;
        }

        if (!manifest.Tables.TryGetValue(LauncherLocalAccountsTableName, out var launcherTableElement))
        {
            return;
        }

        if (launcherTableElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 的备份结构无效。");
        }

        List<LauncherAccountBackupRow>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<LauncherAccountBackupRow>>(launcherTableElement.GetRawText());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 解析失败：{ex.Message}");
        }

        if (rows is null || rows.Count == 0)
        {
            return;
        }

        if (rows.Count > 1)
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 包含多条账号快照，不允许恢复。");
        }

        var row = rows[0];
        ValidateLauncherSnapshotRow(row);

        var currentSessionAccountId = _sessionContextService?.Current?.AccountId;
        if (!string.IsNullOrWhiteSpace(currentSessionAccountId)
            && !string.Equals(currentSessionAccountId, row.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份中的账号标识与当前会话账号不一致，已拒绝恢复。");
        }

        var restoredDatabasePath = _connectionFactory.DatabasePath;

        await using var connection = _launcherConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LocalAccounts (
                AccountId,
                Username,
                DisplayName,
                PasswordHash,
                PasswordSalt,
                PasswordIterations,
                PinHash,
                PinSalt,
                PinIterations,
                RecoveryKeyHash,
                RecoveryKeySalt,
                RecoveryKeyIterations,
                RecoveryEncryptedDataKey,
                RecoveryDataKeyNonce,
                RecoveryDataKeyTag,
                EncryptedDataKey,
                DataKeyNonce,
                DataKeyTag,
                AdminOwnerAccountId,
                AdminEncryptedDataKey,
                AdminDataKeyNonce,
                AdminDataKeyTag,
                DatabasePath,
                Role,
                IsEnabled,
                CreatedAt,
                UpdatedAt,
                LastLoginAt
            )
            VALUES (
                $accountId,
                $username,
                $displayName,
                $passwordHash,
                $passwordSalt,
                $passwordIterations,
                $pinHash,
                $pinSalt,
                $pinIterations,
                $recoveryKeyHash,
                $recoveryKeySalt,
                $recoveryKeyIterations,
                $recoveryEncryptedDataKey,
                $recoveryDataKeyNonce,
                $recoveryDataKeyTag,
                $encryptedDataKey,
                $dataKeyNonce,
                $dataKeyTag,
                $adminOwnerAccountId,
                $adminEncryptedDataKey,
                $adminDataKeyNonce,
                $adminDataKeyTag,
                $databasePath,
                $role,
                $isEnabled,
                $createdAt,
                $updatedAt,
                $lastLoginAt
            )
            ON CONFLICT(AccountId) DO UPDATE SET
                Username = excluded.Username,
                DisplayName = excluded.DisplayName,
                PasswordHash = excluded.PasswordHash,
                PasswordSalt = excluded.PasswordSalt,
                PasswordIterations = excluded.PasswordIterations,
                PinHash = excluded.PinHash,
                PinSalt = excluded.PinSalt,
                PinIterations = excluded.PinIterations,
                RecoveryKeyHash = excluded.RecoveryKeyHash,
                RecoveryKeySalt = excluded.RecoveryKeySalt,
                RecoveryKeyIterations = excluded.RecoveryKeyIterations,
                RecoveryEncryptedDataKey = excluded.RecoveryEncryptedDataKey,
                RecoveryDataKeyNonce = excluded.RecoveryDataKeyNonce,
                RecoveryDataKeyTag = excluded.RecoveryDataKeyTag,
                EncryptedDataKey = excluded.EncryptedDataKey,
                DataKeyNonce = excluded.DataKeyNonce,
                DataKeyTag = excluded.DataKeyTag,
                AdminOwnerAccountId = excluded.AdminOwnerAccountId,
                AdminEncryptedDataKey = excluded.AdminEncryptedDataKey,
                AdminDataKeyNonce = excluded.AdminDataKeyNonce,
                AdminDataKeyTag = excluded.AdminDataKeyTag,
                DatabasePath = excluded.DatabasePath,
                Role = excluded.Role,
                IsEnabled = excluded.IsEnabled,
                CreatedAt = excluded.CreatedAt,
                UpdatedAt = excluded.UpdatedAt,
                LastLoginAt = excluded.LastLoginAt;
            """;
        command.Parameters.AddWithValue("$accountId", row.AccountId);
        command.Parameters.AddWithValue("$username", row.Username);
        command.Parameters.AddWithValue("$displayName", row.DisplayName);
        command.Parameters.AddWithValue("$passwordHash", FromBase64(row.PasswordHash, "PasswordHash"));
        command.Parameters.AddWithValue("$passwordSalt", FromBase64(row.PasswordSalt, "PasswordSalt"));
        command.Parameters.AddWithValue("$passwordIterations", row.PasswordIterations);
        command.Parameters.AddWithValue("$pinHash", FromBase64(row.PinHash, "PinHash"));
        command.Parameters.AddWithValue("$pinSalt", FromBase64(row.PinSalt, "PinSalt"));
        command.Parameters.AddWithValue("$pinIterations", row.PinIterations);
        command.Parameters.AddWithValue("$recoveryKeyHash", ToDbBlobFromBase64(row.RecoveryKeyHash));
        command.Parameters.AddWithValue("$recoveryKeySalt", ToDbBlobFromBase64(row.RecoveryKeySalt));
        command.Parameters.AddWithValue("$recoveryKeyIterations", row.RecoveryKeyIterations is null ? DBNull.Value : row.RecoveryKeyIterations.Value);
        command.Parameters.AddWithValue("$recoveryEncryptedDataKey", ToDbBlobFromBase64(row.RecoveryEncryptedDataKey));
        command.Parameters.AddWithValue("$recoveryDataKeyNonce", ToDbBlobFromBase64(row.RecoveryDataKeyNonce));
        command.Parameters.AddWithValue("$recoveryDataKeyTag", ToDbBlobFromBase64(row.RecoveryDataKeyTag));
        command.Parameters.AddWithValue("$encryptedDataKey", FromBase64(row.EncryptedDataKey, "EncryptedDataKey"));
        command.Parameters.AddWithValue("$dataKeyNonce", FromBase64(row.DataKeyNonce, "DataKeyNonce"));
        command.Parameters.AddWithValue("$dataKeyTag", FromBase64(row.DataKeyTag, "DataKeyTag"));
        command.Parameters.AddWithValue("$adminOwnerAccountId", string.IsNullOrWhiteSpace(row.AdminOwnerAccountId) ? DBNull.Value : row.AdminOwnerAccountId);
        command.Parameters.AddWithValue("$adminEncryptedDataKey", ToDbBlobFromBase64(row.AdminEncryptedDataKey));
        command.Parameters.AddWithValue("$adminDataKeyNonce", ToDbBlobFromBase64(row.AdminDataKeyNonce));
        command.Parameters.AddWithValue("$adminDataKeyTag", ToDbBlobFromBase64(row.AdminDataKeyTag));
        command.Parameters.AddWithValue("$databasePath", restoredDatabasePath);
        command.Parameters.AddWithValue("$role", row.Role);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", row.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", row.UpdatedAt);
        command.Parameters.AddWithValue("$lastLoginAt", string.IsNullOrWhiteSpace(row.LastLoginAt) ? DBNull.Value : row.LastLoginAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateLauncherSnapshotRow(LauncherAccountBackupRow row)
    {
        if (string.IsNullOrWhiteSpace(row.AccountId)
            || string.IsNullOrWhiteSpace(row.Username)
            || string.IsNullOrWhiteSpace(row.DisplayName)
            || string.IsNullOrWhiteSpace(row.PasswordHash)
            || string.IsNullOrWhiteSpace(row.PasswordSalt)
            || string.IsNullOrWhiteSpace(row.PinHash)
            || string.IsNullOrWhiteSpace(row.PinSalt)
            || string.IsNullOrWhiteSpace(row.EncryptedDataKey)
            || string.IsNullOrWhiteSpace(row.DataKeyNonce)
            || string.IsNullOrWhiteSpace(row.DataKeyTag)
            || string.IsNullOrWhiteSpace(row.CreatedAt)
            || string.IsNullOrWhiteSpace(row.UpdatedAt))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 存在缺失关键字段的账号快照。");
        }
    }

    private static BackupRestorePreviewResult BuildRestorePreviewResult(
        string backupPath,
        BackupValidationResult validation,
        TargetInspectionResult inspection)
    {
        var errors = new List<string>(validation.Errors);
        var willClearQaData = inspection.TargetState == BackupRestoreTargetState.QaDatabase;

        if (inspection.TargetState == BackupRestoreTargetState.NonEmptyProductionDatabase)
        {
            errors.Add("目标库包含非 QA 生产数据，禁止覆盖恢复。");
        }

        if (validation.IsValid && inspection.TargetState == BackupRestoreTargetState.Unknown)
        {
            errors.Add("目标库状态未知，已停止恢复。");
        }

        var refuseReason = errors.Count > 0
            ? string.Join("；", errors.Distinct(StringComparer.Ordinal))
            : string.Empty;

        var summary = validation.IsValid
            ? inspection.TargetState switch
            {
                BackupRestoreTargetState.EmptyDatabase => "备份校验通过，当前目标库为空，可执行恢复。",
                BackupRestoreTargetState.QaDatabase => "备份校验通过，当前目标库为 QA/测试数据，清理 QA 数据后可恢复。",
                BackupRestoreTargetState.NonEmptyProductionDatabase => "备份校验通过，但当前目标库包含非 QA 生产数据，已禁止恢复。",
                _ => "备份校验通过，但未能识别目标库状态。"
            }
            : $"备份校验失败：{string.Join("；", validation.Errors)}";

        var canRestore = validation.IsValid
            && (inspection.TargetState == BackupRestoreTargetState.EmptyDatabase
                || inspection.TargetState == BackupRestoreTargetState.QaDatabase);

        return new BackupRestorePreviewResult
        {
            BackupPath = backupPath,
            FileName = Path.GetFileName(backupPath),
            ExportedAt = validation.Manifest?.ExportedAt,
            SchemaVersion = validation.Manifest?.SchemaVersion,
            Checksum = validation.Manifest?.Checksum ?? string.Empty,
            IsChecksumValid = validation.IsChecksumValid,
            Counts = validation.Manifest?.Counts ?? new Dictionary<string, int>(StringComparer.Ordinal),
            Validation = validation,
            TargetState = inspection.TargetState,
            TargetCounts = inspection.Counts,
            WillClearQaData = willClearQaData,
            RequiresQaDataClear = willClearQaData,
            IsQaTaggedBackup = IsQaTaggedBackup(validation.Manifest),
            CanRestore = canRestore,
            RefuseReason = refuseReason,
            Summary = summary,
            Errors = errors
        };
    }
}
