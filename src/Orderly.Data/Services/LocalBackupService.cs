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

    private static readonly string[] RequiredTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults"
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
        var activityType = result.IsValid
            ? ActivityType.BackupValidationSucceeded
            : ActivityType.BackupValidationFailed;
        var title = result.IsValid ? "校验备份成功" : "校验备份失败";
        var description = result.IsValid
            ? $"已校验 {Path.GetFileName(backupPath)}"
            : $"校验失败：{string.Join("；", result.Errors)}";

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = activityType,
            Title = title,
            Description = description,
            Operator = "local-backup",
            MetadataJson = BuildValidationActivityMetadataJson(result, createdBy, tagForQaScope)
        }, cancellationToken);

        return result;
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
            var rows = await ReadTableRowsAsync(connection, tableName, cancellationToken);
            manifest.Tables[tableName] = JsonSerializer.SerializeToElement(rows, SerializerOptions);
            manifest.Counts[tableName] = rows.Count;
        }

        return manifest;
    }

    private async Task<List<Dictionary<string, object?>>> ReadTableRowsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
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
                actualChecksum = ComputeChecksum(manifest);
                if (string.IsNullOrWhiteSpace(manifest.Checksum))
                {
                    errors.Add("缺少 checksum。");
                }
                else if (!string.Equals(manifest.Checksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("checksum 校验失败。");
                }

                foreach (var tableName in RequiredTableNames)
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
}
