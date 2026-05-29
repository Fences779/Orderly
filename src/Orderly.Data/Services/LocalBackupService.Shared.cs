using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
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

    private static string? ToBase64Nullable(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return null;
        }

        return Convert.ToBase64String((byte[])reader[index]);
    }

    private static byte[] FromBase64(string base64, string fieldName)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"字段 {fieldName} 的 Base64 数据无效：{ex.Message}");
        }
    }

    private static object ToDbBlobFromBase64(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return DBNull.Value;
        }

        return FromBase64(base64, "blob");
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
}
