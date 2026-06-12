using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using System.Globalization;
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
        RequireOwnerSessionForRestore();

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
        var safeTableName = RequireKnownBackupSqlTableName(tableName);
        var allowedColumns = GetRestoreColumns(safeTableName);
        if (tableElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"表 {safeTableName} 的备份结构无效。");
        }

        var columnTypes = await ReadRestoreColumnTypesAsync(
            connection,
            transaction,
            safeTableName,
            cancellationToken);

        foreach (var row in tableElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"表 {safeTableName} 存在非对象行。");
            }

            var properties = row.EnumerateObject().ToArray();
            ValidateRestoreColumns(safeTableName, properties, allowedColumns);
            var columnNames = string.Join(", ", properties.Select(static property => QuoteSqlIdentifier(property.Name)));
            var parameterNames = string.Join(", ", properties.Select((_, index) => $"$p{index}"));

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {QuoteSqlIdentifier(safeTableName)} ({columnNames}) VALUES ({parameterNames});";

            for (var index = 0; index < properties.Length; index++)
            {
                if (!columnTypes.TryGetValue(properties[index].Name, out var declaredType))
                {
                    throw new InvalidOperationException(
                        $"表 {safeTableName} 字段 {properties[index].Name} 不存在于目标数据库。");
                }

                ValidateRestoreValueType(
                    safeTableName,
                    properties[index].Name,
                    properties[index].Value,
                    declaredType);
                ValidateRestoreValueSemantics(
                    safeTableName,
                    properties[index].Name,
                    properties[index].Value);
                command.Parameters.AddWithValue(
                    $"$p{index}",
                    ConvertRestoreJsonValue(safeTableName, properties[index].Name, properties[index].Value));
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void ValidateRestoreValueSemantics(
        string tableName,
        string columnName,
        JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null
            || TryGetSensitivePlaintextDefault(tableName, columnName, out _))
        {
            return;
        }

        if (columnName is "Id" or "CustomerId" or "DealId" or "OrderId" or "MessageId")
        {
            if (!value.TryGetInt64(out var id) || id <= 0)
            {
                throw new InvalidOperationException($"表 {tableName} 字段 {columnName} 的标识无效。");
            }

            return;
        }

        if (columnName == "Version")
        {
            if (!value.TryGetInt64(out var version) || version < 1)
            {
                throw new InvalidOperationException($"表 {tableName} 字段 Version 的版本号无效。");
            }

            return;
        }

        if (columnName is "IsSynced" or "IsPinned" or "IsFavorite")
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return;
            }

            if (!value.TryGetInt64(out var flag) || flag is not 0 and not 1)
            {
                throw new InvalidOperationException($"表 {tableName} 字段 {columnName} 的布尔值无效。");
            }

            return;
        }

        var enumType = GetRestoreEnumType(tableName, columnName);
        if (enumType is not null)
        {
            if (!value.TryGetInt32(out var enumValue) || !Enum.IsDefined(enumType, enumValue))
            {
                throw new InvalidOperationException($"表 {tableName} 字段 {columnName} 的枚举值无效。");
            }

            return;
        }

        if (columnName.EndsWith("At", StringComparison.Ordinal)
            && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var timestamp)
                || timestamp.Year is < 2000 or > 2100)
            {
                throw new InvalidOperationException($"表 {tableName} 字段 {columnName} 的时间值无效。");
            }
        }
    }

    private static Type? GetRestoreEnumType(string tableName, string columnName)
    {
        return (tableName, columnName) switch
        {
            ("Customers", "Status") => typeof(CustomerStatus),
            ("Customers", "Priority") => typeof(CustomerPriority),
            ("Deals", "Stage") => typeof(DealStage),
            ("Orders", "Status") => typeof(OrderStatus),
            ("FollowUps", "Status") => typeof(FollowUpStatus),
            ("CustomerNotes", "Type") => typeof(NoteType),
            ("PriceAdjustments", "Status") => typeof(PriceAdjustmentStatus),
            ("ActivityLogs", "Type") => typeof(ActivityType),
            ("ConversationMessages", "Direction") => typeof(MessageDirection),
            ("ConversationMessages", "Channel") => typeof(MessageChannel),
            ("AiSuggestions", "Status") => typeof(AiSuggestionStatus),
            ("OcrResults", "Status") => typeof(OcrStatus),
            _ => null
        };
    }

    private static async Task<Dictionary<string, string>> ReadRestoreColumnTypesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({QuoteSqlIdentifier(tableName)});";

        var columnTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columnTypes[reader.GetString(1)] = reader.GetString(2);
        }

        if (columnTypes.Count == 0)
        {
            throw new InvalidOperationException($"表 {tableName} 不存在于目标数据库。");
        }

        return columnTypes;
    }

    private static void ValidateRestoreValueType(
        string tableName,
        string columnName,
        JsonElement value,
        string declaredType)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var normalizedType = declaredType.Trim().ToUpperInvariant();
        var isValid = normalizedType switch
        {
            var type when type.Contains("INT", StringComparison.Ordinal) =>
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                || value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            var type when type.Contains("REAL", StringComparison.Ordinal)
                || type.Contains("FLOA", StringComparison.Ordinal)
                || type.Contains("DOUB", StringComparison.Ordinal)
                || type.Contains("NUM", StringComparison.Ordinal)
                || type.Contains("DEC", StringComparison.Ordinal) =>
                value.ValueKind == JsonValueKind.Number,
            var type when type.Contains("CHAR", StringComparison.Ordinal)
                || type.Contains("CLOB", StringComparison.Ordinal)
                || type.Contains("TEXT", StringComparison.Ordinal) =>
                value.ValueKind == JsonValueKind.String,
            var type when type.Contains("BLOB", StringComparison.Ordinal) =>
                value.ValueKind == JsonValueKind.String,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException(
                $"表 {tableName} 字段 {columnName} 的值类型与目标列 {declaredType} 不匹配。");
        }
    }

    private static HashSet<string> GetRestoreColumns(string tableName)
    {
        return RestoreTableColumns.TryGetValue(tableName, out var columns)
            ? columns
            : throw new InvalidOperationException($"表 {tableName} 不允许恢复。");
    }

    private static void ValidateRestoreColumns(
        string tableName,
        IReadOnlyCollection<JsonProperty> properties,
        HashSet<string> allowedColumns)
    {
        if (properties.Count == 0)
        {
            throw new InvalidOperationException($"表 {tableName} 存在空对象行。");
        }

        var unknownColumns = properties
            .Select(static property => property.Name)
            .Where(column => !allowedColumns.Contains(column))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unknownColumns.Length > 0)
        {
            throw new InvalidOperationException($"表 {tableName} 包含不允许恢复的列：{string.Join(", ", unknownColumns)}。");
        }
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
