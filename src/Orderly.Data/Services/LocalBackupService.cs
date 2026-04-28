using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed class LocalBackupService : IBackupService
{
    private const int CurrentSchemaVersion = 1;
    private const string BackupEntityType = "local-backup";
    private const string RestoreEntityType = "local-restore";
    private const string RestoreOperator = "local-restore";

    private static readonly string[] IncludedTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "FollowUps",
        "CustomerNotes",
        "PriceAdjustments",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults"
    ];

    private static readonly string[] RestoreOrderedTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "FollowUps",
        "CustomerNotes",
        "PriceAdjustments",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults"
    ];

    private static readonly string[] TargetInspectionTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "FollowUps",
        "CustomerNotes",
        "PriceAdjustments",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults",
        "SyncRecords"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISyncService _syncService;
    private readonly ISyncRecordRepository _syncRecordRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalBackupService(
        SqliteConnectionFactory connectionFactory,
        ISyncService syncService,
        ISyncRecordRepository syncRecordRepository,
        IActivityLogRepository activityLogRepository)
    {
        _connectionFactory = connectionFactory;
        _syncService = syncService;
        _syncRecordRepository = syncRecordRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<BackupResult> ExportAsync(
        string outputPath,
        string createdBy = "p2.7",
        bool tagForQaScope = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("备份文件路径不能为空。", nameof(outputPath));
        }

        var entityId = GenerateBackupEntityId();

        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var manifest = await BuildManifestAsync(cancellationToken);
            manifest.Checksum = ComputeChecksum(manifest);

            var json = JsonSerializer.Serialize(manifest, SerializerOptions);
            await File.WriteAllTextAsync(outputPath, json, Utf8NoBom, cancellationToken);

            var syncMetadata = BuildExportMetadataJson(outputPath, manifest, createdBy, tagForQaScope);
            var syncRecord = await _syncService.MarkSyncedAsync(
                BackupEntityType,
                entityId,
                metadataJson: syncMetadata,
                cancellationToken: cancellationToken);

            await _activityLogRepository.CreateAsync(new ActivityLog
            {
                Type = ActivityType.BackupExported,
                Title = "导出本地备份",
                Description = $"已导出 {Path.GetFileName(outputPath)}",
                Operator = "local-backup",
                MetadataJson = BuildActivityMetadataJson(
                    outputPath,
                    manifest,
                    createdBy,
                    tagForQaScope,
                    operation: "export")
            }, cancellationToken);

            return new BackupResult
            {
                SyncRecordId = syncRecord.Id,
                SyncStatus = syncRecord.SyncStatus,
                BackupPath = outputPath,
                Manifest = manifest
            };
        }
        catch (Exception ex)
        {
            await _syncService.MarkFailedAsync(
                BackupEntityType,
                entityId,
                ex.Message,
                BuildFailureMetadataJson(outputPath, createdBy, ex.Message, tagForQaScope),
                cancellationToken);

            throw;
        }
    }

    public async Task<BackupValidationResult> ValidateAsync(
        string backupPath,
        string createdBy = "p2.7",
        bool tagForQaScope = false,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidateCoreAsync(backupPath, cancellationToken);
        var shouldTagQaScope = tagForQaScope || IsQaTaggedBackup(result.Manifest);
        await WriteValidationActivityAsync(result, createdBy, shouldTagQaScope, cancellationToken);
        return result;
    }

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

    public async Task<BackupResult?> GetLatestBackupAsync(CancellationToken cancellationToken = default)
    {
        var record = await _syncRecordRepository.GetLatestByEntityTypeAsync(BackupEntityType, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var metadata = ParseMetadata(record.MetadataJson);
        var manifest = new BackupManifest
        {
            SchemaVersion = metadata["schemaVersion"]?.GetValue<int?>() ?? CurrentSchemaVersion,
            App = metadata["app"]?.GetValue<string>() ?? "Orderly",
            ExportedAt = TryGetDateTimeOffset(metadata["exportedAt"]) ?? new DateTimeOffset(record.UpdatedAt),
            Counts = ParseCounts(metadata["counts"] as JsonObject),
            Checksum = metadata["checksum"]?.GetValue<string>() ?? string.Empty
        };

        return new BackupResult
        {
            SyncRecordId = record.Id,
            SyncStatus = record.SyncStatus,
            BackupPath = metadata["backupPath"]?.GetValue<string>() ?? string.Empty,
            ErrorSummary = !string.IsNullOrWhiteSpace(record.ErrorMessage)
                ? record.ErrorMessage
                : metadata["errorSummary"]?.GetValue<string>() ?? string.Empty,
            Manifest = manifest
        };
    }

    private async Task<BackupManifest> BuildManifestAsync(CancellationToken cancellationToken)
    {
        var manifest = new BackupManifest
        {
            SchemaVersion = CurrentSchemaVersion,
            App = "Orderly",
            ExportedAt = DateTimeOffset.Now
        };

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        foreach (var tableName in IncludedTableNames)
        {
            var rows = await ReadTableRowsAsync(connection, transaction: null, tableName, cancellationToken);
            manifest.Tables[tableName] = JsonSerializer.SerializeToElement(rows, SerializerOptions);
            manifest.Counts[tableName] = rows.Count;
        }

        return manifest;
    }

    private async Task<List<Dictionary<string, object?>>> ReadTableRowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM {tableName} WHERE DeletedAt IS NULL ORDER BY Id ASC;";

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var columnName = reader.GetName(index);
                row[columnName] = SanitizeValue(tableName, columnName, reader.GetValue(index));
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<BackupValidationResult> ValidateCoreAsync(string backupPath, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        BackupManifest? manifest = null;
        string actualChecksum = string.Empty;

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            errors.Add("备份文件路径不能为空。");
            return new BackupValidationResult
            {
                BackupPath = backupPath,
                Errors = errors
            };
        }

        if (!File.Exists(backupPath))
        {
            errors.Add("备份文件不存在。");
            return new BackupValidationResult
            {
                BackupPath = backupPath,
                Errors = errors
            };
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(backupPath, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add($"读取备份文件失败：{ex.Message}");
            return new BackupValidationResult
            {
                BackupPath = backupPath,
                Errors = errors
            };
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON 解析失败：{ex.Message}");
            return new BackupValidationResult
            {
                BackupPath = backupPath,
                Errors = errors
            };
        }

        using (document)
        {
            var root = document.RootElement;
            manifest = ParseManifest(root, errors);
            if (manifest is not null)
            {
                if (manifest.SchemaVersion != CurrentSchemaVersion)
                {
                    errors.Add($"schemaVersion {manifest.SchemaVersion} 不受支持。当前仅支持 {CurrentSchemaVersion}。");
                }

                actualChecksum = ComputeChecksum(manifest);
                if (string.IsNullOrWhiteSpace(manifest.Checksum))
                {
                    errors.Add("缺少 checksum。");
                }
                else if (!string.Equals(manifest.Checksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("checksum 校验失败。");
                }

                foreach (var tableName in IncludedTableNames)
                {
                    if (!manifest.Tables.TryGetValue(tableName, out var tableElement))
                    {
                        errors.Add($"缺少关键表：{tableName}。");
                        continue;
                    }

                    if (tableElement.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add($"表 {tableName} 不是数组。");
                        continue;
                    }

                    if (!manifest.Counts.TryGetValue(tableName, out var count))
                    {
                        errors.Add($"counts 缺少 {tableName}。");
                        continue;
                    }

                    if (tableElement.GetArrayLength() != count)
                    {
                        errors.Add($"表 {tableName} 的 counts 与实际记录数不一致。");
                    }
                }
            }
        }

        return new BackupValidationResult
        {
            BackupPath = backupPath,
            IsValid = errors.Count == 0,
            Manifest = manifest,
            ActualChecksum = actualChecksum,
            IsChecksumValid = manifest is not null
                && !string.IsNullOrWhiteSpace(manifest.Checksum)
                && string.Equals(manifest.Checksum, actualChecksum, StringComparison.OrdinalIgnoreCase),
            Errors = errors
        };
    }

    private static BackupManifest? ParseManifest(JsonElement root, ICollection<string> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("备份 JSON 顶层必须是对象。");
            return null;
        }

        if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement) || schemaVersionElement.ValueKind != JsonValueKind.Number)
        {
            errors.Add("缺少 schemaVersion。");
        }

        if (!root.TryGetProperty("app", out var appElement) || appElement.ValueKind != JsonValueKind.String)
        {
            errors.Add("缺少 app。");
        }
        else if (!string.Equals(appElement.GetString(), "Orderly", StringComparison.Ordinal))
        {
            errors.Add("app 不是 Orderly。");
        }

        if (!root.TryGetProperty("exportedAt", out var exportedAtElement) || exportedAtElement.ValueKind != JsonValueKind.String)
        {
            errors.Add("缺少 exportedAt。");
        }

        if (!root.TryGetProperty("counts", out var countsElement) || countsElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("缺少 counts。");
        }

        if (!root.TryGetProperty("tables", out var tablesElement) || tablesElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("缺少 tables。");
        }

        if (errors.Count > 0)
        {
            return null;
        }

        var manifest = new BackupManifest
        {
            SchemaVersion = schemaVersionElement.GetInt32(),
            App = appElement.GetString() ?? "Orderly",
            ExportedAt = DateTimeOffset.TryParse(exportedAtElement.GetString(), out var exportedAt)
                ? exportedAt
                : default,
            Checksum = root.TryGetProperty("checksum", out var checksumElement) && checksumElement.ValueKind == JsonValueKind.String
                ? checksumElement.GetString() ?? string.Empty
                : string.Empty
        };

        foreach (var property in countsElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count))
            {
                manifest.Counts[property.Name] = count;
            }
        }

        foreach (var property in tablesElement.EnumerateObject())
        {
            manifest.Tables[property.Name] = property.Value.Clone();
        }

        if (manifest.ExportedAt == default)
        {
            errors.Add("exportedAt 格式无效。");
        }

        return manifest;
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
            "DeletedAt IS NULL"
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

    private async Task WriteValidationActivityAsync(
        BackupValidationResult result,
        string createdBy,
        bool tagForQaScope,
        CancellationToken cancellationToken)
    {
        var activityType = result.IsValid
            ? ActivityType.BackupValidationSucceeded
            : ActivityType.BackupValidationFailed;
        var title = result.IsValid ? "校验备份成功" : "校验备份失败";
        var description = result.IsValid
            ? $"已校验 {Path.GetFileName(result.BackupPath)}"
            : $"校验失败：{string.Join("；", result.Errors)}";

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = activityType,
            Title = title,
            Description = description,
            Operator = "local-backup",
            MetadataJson = BuildValidationActivityMetadataJson(result, createdBy, tagForQaScope)
        }, cancellationToken);
    }

    private static object ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Number when element.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => element.ToString()
        };
    }

    private static object? SanitizeValue(string tableName, string columnName, object value)
    {
        if (value == DBNull.Value)
        {
            return null;
        }

        if (tableName == "OcrResults"
            && string.Equals(columnName, "SourcePath", StringComparison.Ordinal)
            && value is string sourcePath
            && Path.IsPathRooted(sourcePath))
        {
            return Path.GetFileName(sourcePath);
        }

        return value;
    }

    private static bool IsQaTaggedBackup(BackupManifest? manifest)
    {
        if (manifest is null)
        {
            return false;
        }

        foreach (var tableElement in manifest.Tables.Values)
        {
            var raw = tableElement.GetRawText();
            if (raw.Contains(QaDataScope.CurrentDisplayMarker, StringComparison.Ordinal)
                || raw.Contains(QaDataScope.P2DisplayMarker, StringComparison.Ordinal)
                || raw.Contains(QaDataScope.CurrentTag, StringComparison.Ordinal)
                || raw.Contains("p2qa-", StringComparison.Ordinal)
                || raw.Contains("p13qa-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeChecksum(BackupManifest manifest)
    {
        var normalizedTables = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in manifest.Tables.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            normalizedTables[pair.Key] = pair.Value;
        }

        var normalizedCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in manifest.Counts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            normalizedCounts[pair.Key] = pair.Value;
        }

        var checksumPayload = new
        {
            schemaVersion = manifest.SchemaVersion,
            app = manifest.App,
            exportedAt = manifest.ExportedAt,
            tables = normalizedTables,
            counts = normalizedCounts
        };

        var payloadJson = JsonSerializer.Serialize(checksumPayload);
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(payloadJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static JsonObject ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static Dictionary<string, int> ParseCounts(JsonObject? countsNode)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (countsNode is null)
        {
            return counts;
        }

        foreach (var pair in countsNode)
        {
            if (pair.Value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var count))
            {
                counts[pair.Key] = count;
            }
        }

        return counts;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string BuildExportMetadataJson(
        string backupPath,
        BackupManifest manifest,
        string createdBy,
        bool tagForQaScope)
    {
        var metadata = new JsonObject
        {
            ["mode"] = BackupEntityType,
            ["app"] = manifest.App,
            ["schemaVersion"] = manifest.SchemaVersion,
            ["exportedAt"] = manifest.ExportedAt.ToString("O"),
            ["backupPath"] = backupPath,
            ["checksum"] = manifest.Checksum,
            ["createdBy"] = createdBy,
            ["counts"] = JsonSerializer.SerializeToNode(manifest.Counts)
        };

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string BuildFailureMetadataJson(string backupPath, string createdBy, string errorSummary, bool tagForQaScope)
    {
        var metadata = new JsonObject
        {
            ["mode"] = BackupEntityType,
            ["backupPath"] = backupPath,
            ["createdBy"] = createdBy,
            ["errorSummary"] = errorSummary
        };

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string BuildActivityMetadataJson(
        string backupPath,
        BackupManifest manifest,
        string createdBy,
        bool tagForQaScope,
        string operation)
    {
        var metadata = new JsonObject
        {
            ["operation"] = operation,
            ["backupPath"] = backupPath,
            ["checksum"] = manifest.Checksum,
            ["createdBy"] = createdBy,
            ["schemaVersion"] = manifest.SchemaVersion,
            ["counts"] = JsonSerializer.SerializeToNode(manifest.Counts)
        };

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string BuildValidationActivityMetadataJson(
        BackupValidationResult result,
        string createdBy,
        bool tagForQaScope)
    {
        var metadata = new JsonObject
        {
            ["operation"] = "validate",
            ["backupPath"] = result.BackupPath,
            ["createdBy"] = createdBy,
            ["isValid"] = result.IsValid,
            ["actualChecksum"] = result.ActualChecksum
        };

        if (result.Manifest is not null)
        {
            metadata["checksum"] = result.Manifest.Checksum;
            metadata["schemaVersion"] = result.Manifest.SchemaVersion;
            metadata["counts"] = JsonSerializer.SerializeToNode(result.Manifest.Counts);
        }

        if (result.Errors.Count > 0)
        {
            metadata["errors"] = JsonSerializer.SerializeToNode(result.Errors);
        }

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(result.BackupPath));
    }

    private static string BuildRestoreSuccessMetadataJson(
        string backupPath,
        BackupManifest manifest,
        string createdBy,
        BackupRestoreTargetState targetState,
        bool qaDataCleared,
        DateTimeOffset restoredAt,
        bool tagForQaScope)
    {
        var metadata = new JsonObject
        {
            ["mode"] = RestoreEntityType,
            ["backupPath"] = backupPath,
            ["checksum"] = manifest.Checksum,
            ["schemaVersion"] = manifest.SchemaVersion,
            ["counts"] = JsonSerializer.SerializeToNode(manifest.Counts),
            ["restoredAt"] = restoredAt.ToString("O"),
            ["createdBy"] = createdBy,
            ["targetState"] = targetState.ToString(),
            ["qaDataCleared"] = qaDataCleared
        };

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string BuildRestoreFailureMetadataJson(
        string backupPath,
        string createdBy,
        string errorSummary,
        BackupRestoreTargetState targetState,
        bool tagForQaScope)
    {
        var metadata = new JsonObject
        {
            ["mode"] = RestoreEntityType,
            ["backupPath"] = backupPath,
            ["createdBy"] = createdBy,
            ["errorSummary"] = errorSummary,
            ["targetState"] = targetState.ToString()
        };

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string BuildRestoreStartedMetadataJson(
        string backupPath,
        string createdBy,
        BackupRestoreTargetState targetState,
        IReadOnlyDictionary<string, int>? counts,
        string? checksum,
        int? schemaVersion,
        DateTimeOffset restoredAt,
        bool tagForQaScope)
    {
        var metadata = new JsonObject
        {
            ["operation"] = "restore-started",
            ["backupPath"] = backupPath,
            ["createdBy"] = createdBy,
            ["targetState"] = targetState.ToString(),
            ["restoredAt"] = restoredAt.ToString("O")
        };

        if (!string.IsNullOrWhiteSpace(checksum))
        {
            metadata["checksum"] = checksum;
        }

        if (schemaVersion is not null)
        {
            metadata["schemaVersion"] = schemaVersion;
        }

        if (counts is not null)
        {
            metadata["counts"] = JsonSerializer.SerializeToNode(counts);
        }

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string BuildRestoreActivityMetadataJson(
        string backupPath,
        BackupManifest manifest,
        string createdBy,
        BackupRestoreTargetState targetState,
        bool qaDataCleared,
        DateTimeOffset restoredAt,
        bool tagForQaScope,
        string operation)
    {
        var metadata = new JsonObject
        {
            ["operation"] = operation,
            ["backupPath"] = backupPath,
            ["checksum"] = manifest.Checksum,
            ["schemaVersion"] = manifest.SchemaVersion,
            ["counts"] = JsonSerializer.SerializeToNode(manifest.Counts),
            ["createdBy"] = createdBy,
            ["targetState"] = targetState.ToString(),
            ["qaDataCleared"] = qaDataCleared,
            ["restoredAt"] = restoredAt.ToString("O")
        };

        return TagQaMetadataIfNeeded(metadata.ToJsonString(), tagForQaScope, Path.GetFileName(backupPath));
    }

    private static string TagQaMetadataIfNeeded(string metadataJson, bool tagForQaScope, string? key)
    {
        if (!tagForQaScope)
        {
            return metadataJson;
        }

        return QaDataScope.EnsureActivityMetadataTagged(metadataJson, "runtime", key);
    }

    private static int GenerateBackupEntityId()
    {
        return Random.Shared.Next(1, int.MaxValue);
    }

    private sealed record TargetInspectionResult(
        BackupRestoreTargetState TargetState,
        IReadOnlyDictionary<string, int> Counts,
        IReadOnlyDictionary<string, int> QaScopedCounts)
    {
        public static TargetInspectionResult Empty()
        {
            return new(
                BackupRestoreTargetState.Unknown,
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal));
        }
    }
}
