using Orderly.Core.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
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

    private async Task<BackupValidationResult> ValidateCoreAsync(string backupPath, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        BackupManifest? manifest = null;
        string actualChecksum = string.Empty;
        string actualIntegrityTag = string.Empty;
        var isChecksumValid = false;
        var isIntegrityValid = false;

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
            errors.Add($"读取备份文件失败：{SanitizeBackupErrorSummary(ex.Message, backupPath)}");
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
            errors.Add($"JSON 解析失败：{SanitizeBackupErrorSummary(ex.Message, backupPath)}");
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
                else
                {
                    isChecksumValid = string.Equals(manifest.Checksum, actualChecksum, StringComparison.OrdinalIgnoreCase);
                }

                if (!isChecksumValid)
                {
                    errors.Add("checksum 校验失败。");
                }

                try
                {
                    var integrityResult = VerifyIntegrityTag(manifest);
                    actualIntegrityTag = integrityResult.ActualTag;
                    isIntegrityValid = integrityResult.IsValid;

                    if (integrityResult.HasTag && !integrityResult.IsValid)
                    {
                        errors.Add("integrityTag 校验失败。");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"integrityTag 校验失败：{SanitizeBackupErrorSummary(ex.Message, backupPath)}");
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

                if (manifest.Tables.TryGetValue(LauncherLocalAccountsTableName, out var launcherTableElement))
                {
                    if (launcherTableElement.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add($"表 {LauncherLocalAccountsTableName} 不是数组。");
                    }

                    if (manifest.Counts.TryGetValue(LauncherLocalAccountsTableName, out var launcherCount)
                        && launcherTableElement.ValueKind == JsonValueKind.Array
                        && launcherTableElement.GetArrayLength() != launcherCount)
                    {
                        errors.Add($"表 {LauncherLocalAccountsTableName} 的 counts 与实际记录数不一致。");
                    }

                    if (!isIntegrityValid)
                    {
                        errors.Add($"包含 {LauncherLocalAccountsTableName} 的备份必须通过 keyed integrity 校验。");
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
            ActualIntegrityTag = actualIntegrityTag,
            IsChecksumValid = isChecksumValid,
            IsIntegrityValid = isIntegrityValid,
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
                : string.Empty,
            IntegrityAlgorithm = root.TryGetProperty("integrityAlgorithm", out var integrityAlgorithmElement)
                && integrityAlgorithmElement.ValueKind == JsonValueKind.String
                ? integrityAlgorithmElement.GetString() ?? string.Empty
                : string.Empty,
            IntegrityKeyScope = root.TryGetProperty("integrityKeyScope", out var integrityKeyScopeElement)
                && integrityKeyScopeElement.ValueKind == JsonValueKind.String
                ? integrityKeyScopeElement.GetString() ?? string.Empty
                : string.Empty,
            IntegrityTag = root.TryGetProperty("integrityTag", out var integrityTagElement)
                && integrityTagElement.ValueKind == JsonValueKind.String
                ? integrityTagElement.GetString() ?? string.Empty
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
}
