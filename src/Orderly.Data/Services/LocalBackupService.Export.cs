using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;
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

        var safeOutputPath = Path.GetFullPath(outputPath);
        EnsureBackupFileExtensionIsSafe(safeOutputPath);
        EnsureBackupPathIsNotLinked(safeOutputPath);
        var entityId = GenerateBackupEntityId();

        try
        {
            var directory = Path.GetDirectoryName(safeOutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                EnsureBackupDirectoryPathIsNotLinked(directory);
                Directory.CreateDirectory(directory);
                EnsureBackupDirectoryPathIsNotLinked(directory);
            }

            var manifest = await BuildManifestAsync(cancellationToken);
            manifest.Checksum = ComputeChecksum(manifest);
            StampIntegrityTag(manifest);

            var json = JsonSerializer.Serialize(manifest, SerializerOptions);
            await WriteBackupJsonAtomicallyAsync(safeOutputPath, json, cancellationToken);

            var syncMetadata = BuildExportMetadataJson(safeOutputPath, manifest, createdBy, tagForQaScope);
            var syncRecord = await _syncService.MarkSyncedAsync(
                BackupEntityType,
                entityId,
                metadataJson: syncMetadata,
                cancellationToken: cancellationToken);

            await _activityLogRepository.CreateAsync(new ActivityLog
            {
                Type = ActivityType.BackupExported,
                Title = "导出本地备份",
                Description = $"已导出 {Path.GetFileName(safeOutputPath)}",
                Operator = "local-backup",
                MetadataJson = BuildActivityMetadataJson(
                    safeOutputPath,
                    manifest,
                    createdBy,
                    tagForQaScope,
                    operation: "export")
            }, cancellationToken);

            return new BackupResult
            {
                SyncRecordId = syncRecord.Id,
                SyncStatus = syncRecord.SyncStatus,
                BackupPath = safeOutputPath,
                Manifest = manifest
            };
        }
        catch (Exception ex)
        {
            var errorSummary = SanitizeBackupErrorSummary(ex.Message, outputPath);
            await _syncService.MarkFailedAsync(
                BackupEntityType,
                entityId,
                errorSummary,
                BuildFailureMetadataJson(outputPath, createdBy, errorSummary, tagForQaScope),
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
            Checksum = metadata["checksum"]?.GetValue<string>() ?? string.Empty,
            IntegrityAlgorithm = metadata["integrityAlgorithm"]?.GetValue<string>() ?? string.Empty,
            IntegrityKeyScope = metadata["integrityKeyScope"]?.GetValue<string>() ?? string.Empty,
            IntegrityTag = metadata["integrityTag"]?.GetValue<string>() ?? string.Empty
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
        var safeTableName = RequireKnownBackupSqlTableName(tableName);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM {QuoteSqlIdentifier(safeTableName)} WHERE {BuildExportPredicate(safeTableName)} ORDER BY Id ASC;";

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var columnName = reader.GetName(index);
                row[columnName] = SanitizeValue(safeTableName, columnName, reader.GetValue(index));
            }

            rows.Add(row);
        }

        return rows;
    }

}
