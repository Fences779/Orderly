using Microsoft.Data.Sqlite;
using Orderly.Core.Models;

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
        var safeTableName = RequireKnownBackupSqlTableName(tableName);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(1) FROM {QuoteSqlIdentifier(safeTableName)} WHERE {BuildTargetAssessmentPredicate(safeTableName, qaOnly)};";
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
