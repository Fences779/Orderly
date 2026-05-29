using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
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

        await AppendLauncherSnapshotAsync(manifest, cancellationToken);

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
        command.CommandText = $"SELECT * FROM {tableName} WHERE {BuildExportPredicate(tableName)} ORDER BY Id ASC;";

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

    private async Task AppendLauncherSnapshotAsync(BackupManifest manifest, CancellationToken cancellationToken)
    {
        if (_launcherConnectionFactory is null)
        {
            return;
        }

        var accountId = await ResolveCurrentAccountIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        await using var connection = _launcherConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
            FROM LocalAccounts
            WHERE AccountId = $accountId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        var snapshotRows = new List<LauncherAccountBackupRow>
        {
            new()
            {
                AccountId = reader.GetString(0),
                Username = reader.GetString(1),
                DisplayName = reader.GetString(2),
                PasswordHash = Convert.ToBase64String((byte[])reader[3]),
                PasswordSalt = Convert.ToBase64String((byte[])reader[4]),
                PasswordIterations = reader.GetInt32(5),
                PinHash = Convert.ToBase64String((byte[])reader[6]),
                PinSalt = Convert.ToBase64String((byte[])reader[7]),
                PinIterations = reader.GetInt32(8),
                RecoveryKeyHash = ToBase64Nullable(reader, 9),
                RecoveryKeySalt = ToBase64Nullable(reader, 10),
                RecoveryKeyIterations = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                RecoveryEncryptedDataKey = ToBase64Nullable(reader, 12),
                RecoveryDataKeyNonce = ToBase64Nullable(reader, 13),
                RecoveryDataKeyTag = ToBase64Nullable(reader, 14),
                EncryptedDataKey = Convert.ToBase64String((byte[])reader[15]),
                DataKeyNonce = Convert.ToBase64String((byte[])reader[16]),
                DataKeyTag = Convert.ToBase64String((byte[])reader[17]),
                AdminOwnerAccountId = reader.IsDBNull(18) ? null : reader.GetString(18),
                AdminEncryptedDataKey = ToBase64Nullable(reader, 19),
                AdminDataKeyNonce = ToBase64Nullable(reader, 20),
                AdminDataKeyTag = ToBase64Nullable(reader, 21),
                DatabasePath = reader.GetString(22),
                Role = reader.GetInt32(23),
                IsEnabled = reader.GetInt32(24) == 1,
                CreatedAt = reader.GetString(25),
                UpdatedAt = reader.GetString(26),
                LastLoginAt = reader.IsDBNull(27) ? null : reader.GetString(27)
            }
        };

        manifest.Tables[LauncherLocalAccountsTableName] = JsonSerializer.SerializeToElement(snapshotRows, SerializerOptions);
        manifest.Counts[LauncherLocalAccountsTableName] = snapshotRows.Count;
    }

    private async Task<string?> ResolveCurrentAccountIdAsync(CancellationToken cancellationToken)
    {
        var sessionAccountId = _sessionContextService?.Current?.AccountId;
        if (!string.IsNullOrWhiteSpace(sessionAccountId))
        {
            return sessionAccountId;
        }

        if (_launcherConnectionFactory is null)
        {
            return null;
        }

        await using var connection = _launcherConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AccountId
            FROM LocalAccounts
            WHERE DatabasePath = $databasePath
            ORDER BY UpdatedAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$databasePath", _connectionFactory.DatabasePath);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }
}
