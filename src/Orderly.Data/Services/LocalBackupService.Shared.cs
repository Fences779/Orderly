using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
    private static readonly Regex LocalPathRegex = new(
        @"(?:[A-Za-z]:\\|\\\\)[^\r\n;；，。]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        var safeErrors = result.Errors
            .Select(error => SanitizeBackupErrorSummary(error, result.BackupPath))
            .ToArray();
        var description = result.IsValid
            ? $"已校验 {Path.GetFileName(result.BackupPath)}"
            : $"校验失败：{string.Join("；", safeErrors)}";

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

    private static object ConvertRestoreJsonValue(string tableName, string columnName, JsonElement element)
    {
        return TryGetSensitivePlaintextDefault(tableName, columnName, out var defaultValue)
            ? defaultValue
            : ConvertJsonValue(element);
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
        if (TryGetSensitivePlaintextDefault(tableName, columnName, out var defaultValue))
        {
            return defaultValue == DBNull.Value ? null : defaultValue;
        }

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

    private static bool TryGetSensitivePlaintextDefault(string tableName, string columnName, out object defaultValue)
    {
        if (SensitivePlaintextColumnDefaults.TryGetValue(tableName, out var tableDefaults)
            && tableDefaults.TryGetValue(columnName, out defaultValue!))
        {
            return true;
        }

        defaultValue = DBNull.Value;
        return false;
    }

    private static bool IsLinkedBackupPath(string backupPath)
    {
        return LocalDataFileSecurity.IsReparsePoint(backupPath)
            || IsBackupDirectoryPathLinked(Path.GetDirectoryName(backupPath));
    }

    private static bool IsBackupFileExtensionSafe(string backupPath)
    {
        return string.Equals(Path.GetExtension(backupPath), ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureBackupFileExtensionIsSafe(string backupPath)
    {
        if (!IsBackupFileExtensionSafe(backupPath))
        {
            throw new InvalidOperationException("备份文件必须是 .json 文件。");
        }
    }

    private static void EnsureBackupPathIsNotLinked(string backupPath)
    {
        if (IsLinkedBackupPath(backupPath))
        {
            throw new InvalidOperationException("备份文件不能是链接文件或位于链接目录。");
        }
    }

    private static void EnsureBackupDirectoryPathIsNotLinked(string? directoryPath)
    {
        if (IsBackupDirectoryPathLinked(directoryPath))
        {
            throw new InvalidOperationException("备份输出目录不能是链接目录，也不能位于链接目录下。");
        }
    }

    private static async Task WriteBackupJsonAtomicallyAsync(
        string backupPath,
        string json,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(backupPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        EnsureBackupDirectoryPathIsNotLinked(directory);
        Directory.CreateDirectory(directory);
        EnsureBackupDirectoryPathIsNotLinked(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(backupPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            EnsureBackupPathIsNotLinked(tempPath);
            var bytes = Utf8NoBom.GetBytes(json);
            await using (var stream = new FileStream(
                tempPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                }))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            EnsureBackupPathIsNotLinked(backupPath);
            File.Move(tempPath, backupPath, overwrite: true);
            EnsureBackupPathIsNotLinked(backupPath);
            LocalDataFileSecurity.HardenFile(backupPath);
        }
        catch
        {
            DeleteTemporaryBackupFile(tempPath);
            throw;
        }
    }

    private static async Task<string> ReadBackupJsonSafelyAsync(
        string backupPath,
        CancellationToken cancellationToken)
    {
        EnsureBackupPathIsNotLinked(backupPath);
        await using var stream = new FileStream(
            backupPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        EnsureBackupPathIsNotLinked(backupPath);

        if (stream.Length > MaxBackupFileBytes)
        {
            throw new InvalidOperationException($"备份文件过大，最大支持 {MaxBackupFileBytes / 1024L / 1024L} MB。");
        }

        using var reader = new StreamReader(
            stream,
            Utf8NoBom,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 81920,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static void DeleteTemporaryBackupFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath) && !LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsBackupDirectoryPathLinked(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var current = new DirectoryInfo(Path.GetFullPath(directoryPath));
        while (current is not null)
        {
            if (current.Exists && LocalDataFileSecurity.IsReparsePoint(current.FullName))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
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

    private static string BuildChecksumPayloadJson(BackupManifest manifest)
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

        return JsonSerializer.Serialize(checksumPayload);
    }

    private static string ComputeChecksum(BackupManifest manifest)
    {
        var payloadJson = BuildChecksumPayloadJson(manifest);
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(payloadJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void StampIntegrityTag(BackupManifest manifest)
    {
        var keyScope = ResolveBackupIntegrityKeyScope();
        var key = GetBackupIntegrityKey(keyScope);
        try
        {
            manifest.IntegrityAlgorithm = BackupIntegrityAlgorithm;
            manifest.IntegrityKeyScope = keyScope;
            manifest.IntegrityTag = ComputeIntegrityTag(manifest, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private BackupIntegrityVerificationResult VerifyIntegrityTag(BackupManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.IntegrityTag))
        {
            return BackupIntegrityVerificationResult.Missing();
        }

        if (!string.Equals(manifest.IntegrityAlgorithm, BackupIntegrityAlgorithm, StringComparison.Ordinal))
        {
            return BackupIntegrityVerificationResult.Invalid(string.Empty);
        }

        var key = GetBackupIntegrityKey(manifest.IntegrityKeyScope);
        try
        {
            var actualTag = ComputeIntegrityTag(manifest, key);
            return BackupIntegrityVerificationResult.FromComparison(
                actualTag,
                FixedTimeEqualsHex(manifest.IntegrityTag, actualTag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private string ResolveBackupIntegrityKeyScope()
    {
        return _sessionContextService?.Current?.DataKey is { Length: > 0 }
            ? BackupIntegritySessionKeyScope
            : BackupIntegrityMachineKeyScope;
    }

    private byte[] GetBackupIntegrityKey(string keyScope)
    {
        return keyScope switch
        {
            BackupIntegritySessionKeyScope => GetSessionBackupIntegrityKey(),
            BackupIntegrityMachineKeyScope => GetMachineBackupIntegrityKey(),
            _ => throw new InvalidOperationException("备份完整性 key scope 不受支持。")
        };
    }

    private byte[] GetSessionBackupIntegrityKey()
    {
        var dataKey = _sessionContextService?.Current?.DataKey;
        if (dataKey is not { Length: BackupIntegrityKeyByteLength })
        {
            throw new InvalidOperationException("当前会话缺少备份完整性校验所需的数据密钥。");
        }

        return dataKey.ToArray();
    }

    private static byte[] GetMachineBackupIntegrityKey()
    {
        var keyPath = Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), BackupIntegrityKeyFileName);
        EnsureMachineBackupIntegrityKeyDirectory(keyPath);
        if (LocalDataFileSecurity.IsReparsePoint(keyPath))
        {
            throw new InvalidOperationException("备份完整性 key 文件不能是链接文件。");
        }

        if (File.Exists(keyPath))
        {
            HardenMachineBackupIntegrityKeyFile(keyPath);
            return ReadMachineBackupIntegrityKeyFile(keyPath);
        }

        var key = RandomNumberGenerator.GetBytes(BackupIntegrityKeyByteLength);
        WriteMachineBackupIntegrityKeyFile(keyPath, key);
        return key;
    }

    private static void EnsureMachineBackupIntegrityKeyDirectory(string keyPath)
    {
        var directory = Path.GetDirectoryName(keyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("备份完整性 key 目录无效。");
        }

        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directory, "备份完整性 key 目录");
        Directory.CreateDirectory(directory);
        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directory, "备份完整性 key 目录");
        LocalDataFileSecurity.HardenDirectory(directory);
    }

    private static byte[] ReadMachineBackupIntegrityKeyFile(string keyPath)
    {
        using var stream = new FileStream(
            keyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BackupIntegrityKeyByteLength,
            FileOptions.SequentialScan);
        if (LocalDataFileSecurity.IsReparsePoint(keyPath))
        {
            throw new InvalidOperationException("备份完整性 key 文件不能是链接文件。");
        }

        if (stream.Length != BackupIntegrityKeyByteLength)
        {
            throw new InvalidOperationException("备份完整性 key 文件长度无效。");
        }

        var key = new byte[BackupIntegrityKeyByteLength];
        stream.ReadExactly(key);
        return key;
    }

    private static void WriteMachineBackupIntegrityKeyFile(string keyPath, byte[] key)
    {
        if (key.Length != BackupIntegrityKeyByteLength)
        {
            throw new InvalidOperationException("备份完整性 key 长度无效。");
        }

        if (LocalDataFileSecurity.IsReparsePoint(keyPath))
        {
            throw new InvalidOperationException("备份完整性 key 文件不能是链接文件。");
        }

        using (var stream = new FileStream(
            keyPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: BackupIntegrityKeyByteLength,
            FileOptions.WriteThrough))
        {
            stream.Write(key);
            stream.Flush(flushToDisk: true);
        }

        HardenMachineBackupIntegrityKeyFile(keyPath);
    }

    private static void HardenMachineBackupIntegrityKeyFile(string keyPath)
    {
        try
        {
            File.SetAttributes(keyPath, File.GetAttributes(keyPath) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                throw new InvalidOperationException("无法识别当前用户，不能加固备份完整性 key。");
            }

            var fileInfo = new FileInfo(keyPath);
            var security = fileInfo.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleAll(rule);
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SystemException)
        {
            throw new InvalidOperationException("备份完整性 key 文件权限加固失败。", ex);
        }
    }

    private static string ComputeIntegrityTag(BackupManifest manifest, byte[] key)
    {
        var payloadJson = BuildChecksumPayloadJson(manifest);
        using var hmac = new HMACSHA256(key);
        var tag = hmac.ComputeHash(Utf8NoBom.GetBytes(payloadJson));
        return Convert.ToHexString(tag).ToLowerInvariant();
    }

    private static bool FixedTimeEqualsHex(string expected, string actual)
    {
        var expectedValue = expected.Trim().ToLowerInvariant();
        var actualValue = actual.Trim().ToLowerInvariant();
        return expectedValue.Length == actualValue.Length
            && CryptographicOperations.FixedTimeEquals(
                Utf8NoBom.GetBytes(expectedValue),
                Utf8NoBom.GetBytes(actualValue));
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
            ["integrityAlgorithm"] = manifest.IntegrityAlgorithm,
            ["integrityKeyScope"] = manifest.IntegrityKeyScope,
            ["integrityTag"] = manifest.IntegrityTag,
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

    private static string SanitizeBackupErrorSummary(string errorSummary, string? knownPath)
    {
        var sanitized = (errorSummary ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(knownPath))
        {
            try
            {
                var fullPath = Path.GetFullPath(knownPath);
                var fileName = Path.GetFileName(fullPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    sanitized = sanitized.Replace(fullPath, fileName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }
        }

        sanitized = LocalPathRegex.Replace(sanitized, "<local-path>");
        if (sanitized.Length > 240)
        {
            sanitized = sanitized[..240].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "操作失败，未提供错误摘要。" : sanitized;
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
            ["integrityAlgorithm"] = manifest.IntegrityAlgorithm,
            ["integrityKeyScope"] = manifest.IntegrityKeyScope,
            ["integrityTag"] = manifest.IntegrityTag,
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
            ["actualChecksum"] = result.ActualChecksum,
            ["actualIntegrityTag"] = result.ActualIntegrityTag,
            ["isIntegrityValid"] = result.IsIntegrityValid
        };

        if (result.Manifest is not null)
        {
            metadata["checksum"] = result.Manifest.Checksum;
            metadata["integrityAlgorithm"] = result.Manifest.IntegrityAlgorithm;
            metadata["integrityKeyScope"] = result.Manifest.IntegrityKeyScope;
            metadata["integrityTag"] = result.Manifest.IntegrityTag;
            metadata["schemaVersion"] = result.Manifest.SchemaVersion;
            metadata["counts"] = JsonSerializer.SerializeToNode(result.Manifest.Counts);
        }

        if (result.Errors.Count > 0)
        {
            var safeErrors = result.Errors
                .Select(error => SanitizeBackupErrorSummary(error, result.BackupPath))
                .ToArray();
            metadata["errors"] = JsonSerializer.SerializeToNode(safeErrors);
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
            ["integrityAlgorithm"] = manifest.IntegrityAlgorithm,
            ["integrityKeyScope"] = manifest.IntegrityKeyScope,
            ["integrityTag"] = manifest.IntegrityTag,
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
        string? integrityAlgorithm,
        string? integrityKeyScope,
        string? integrityTag,
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

        if (!string.IsNullOrWhiteSpace(integrityAlgorithm))
        {
            metadata["integrityAlgorithm"] = integrityAlgorithm;
        }

        if (!string.IsNullOrWhiteSpace(integrityKeyScope))
        {
            metadata["integrityKeyScope"] = integrityKeyScope;
        }

        if (!string.IsNullOrWhiteSpace(integrityTag))
        {
            metadata["integrityTag"] = integrityTag;
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
            ["integrityAlgorithm"] = manifest.IntegrityAlgorithm,
            ["integrityKeyScope"] = manifest.IntegrityKeyScope,
            ["integrityTag"] = manifest.IntegrityTag,
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
