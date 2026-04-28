using Microsoft.Data.Sqlite;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class QaDataMaintenanceService
{
    private static readonly string CustomerPredicate = QaDataScope.BuildCustomerScopePredicate();
    private static readonly string DealPredicate = QaDataScope.BuildDealScopePredicate();
    private static readonly string OrderPredicate = QaDataScope.BuildOrderScopePredicate();
    private static readonly string FollowUpPredicate = QaDataScope.BuildFollowUpScopePredicate();
    private static readonly string NotePredicate = QaDataScope.BuildNoteScopePredicate();
    private static readonly string PriceAdjustmentPredicate = QaDataScope.BuildPriceAdjustmentScopePredicate();
    private static readonly string ActivityLogPredicate = QaDataScope.BuildActivityLogScopePredicate();
    private static readonly string ConversationMessagePredicate = QaDataScope.BuildConversationMessageScopePredicate();
    private static readonly string AiSuggestionPredicate = QaDataScope.BuildAiSuggestionScopePredicate();
    private static readonly string OcrResultPredicate = QaDataScope.BuildOcrResultScopePredicate();
    private static readonly string SyncRecordPredicate = QaDataScope.BuildSyncRecordScopePredicate();

    private readonly SqliteConnectionFactory _connectionFactory;

    public QaDataMaintenanceService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public static bool TryGetRequestedCommand(IEnumerable<string>? args, out QaDataMaintenanceCommand command)
    {
        command = QaDataMaintenanceCommand.None;
        if (args is null)
        {
            return false;
        }

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--reset-qa-data", StringComparison.OrdinalIgnoreCase))
            {
                command = QaDataMaintenanceCommand.Reset;
                return true;
            }

            if (string.Equals(arg, "--clear-qa-data", StringComparison.OrdinalIgnoreCase))
            {
                command = QaDataMaintenanceCommand.Clear;
                return true;
            }

            if (string.Equals(arg, "--qa-data-status", StringComparison.OrdinalIgnoreCase))
            {
                command = QaDataMaintenanceCommand.Status;
                return true;
            }
        }

        return false;
    }

    public async Task<QaDataStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetStatusAsync(connection, transaction: null, cancellationToken);
    }

    public async Task<QaDataClearResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var result = await ClearAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        result.Status = await GetStatusAsync(connection, transaction: null, cancellationToken);
        return result;
    }

    public async Task<QaDataClearResult> ClearAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var result = new QaDataClearResult
        {
            SyncRecordsDeleted = await DeleteAsync(connection, transaction, "SyncRecords", SyncRecordPredicate, cancellationToken),
            AiSuggestionsDeleted = await DeleteAsync(connection, transaction, "AiSuggestions", AiSuggestionPredicate, cancellationToken),
            OcrResultsDeleted = await DeleteAsync(connection, transaction, "OcrResults", OcrResultPredicate, cancellationToken),
            ConversationMessagesDeleted = await DeleteAsync(connection, transaction, "ConversationMessages", ConversationMessagePredicate, cancellationToken),
            ActivityLogsDeleted = await DeleteAsync(connection, transaction, "ActivityLogs", ActivityLogPredicate, cancellationToken),
            PriceAdjustmentsDeleted = await DeleteAsync(connection, transaction, "PriceAdjustments", PriceAdjustmentPredicate, cancellationToken),
            NotesDeleted = await DeleteAsync(connection, transaction, "CustomerNotes", NotePredicate, cancellationToken),
            FollowUpsDeleted = await DeleteAsync(connection, transaction, "FollowUps", FollowUpPredicate, cancellationToken),
            OrdersDeleted = await DeleteAsync(connection, transaction, "Orders", OrderPredicate, cancellationToken),
            DealsDeleted = await DeleteAsync(connection, transaction, "Deals", DealPredicate, cancellationToken),
            CustomersDeleted = await DeleteAsync(connection, transaction, "Customers", CustomerPredicate, cancellationToken)
        };

        result.Status = await GetStatusAsync(connection, transaction, cancellationToken);
        return result;
    }

    public async Task<QaResetResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        var clearResult = await ClearAsync(cancellationToken);
        var seeder = new QaDataSeeder(_connectionFactory);
        var seedResult = await seeder.SeedIfNeededAsync(cancellationToken);
        var status = await GetStatusAsync(cancellationToken);

        return new QaResetResult(clearResult, seedResult, status);
    }

    private static async Task<QaDataStatus> GetStatusAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return new QaDataStatus(
            await CountAsync(connection, transaction, "Customers", CustomerPredicate, cancellationToken),
            await CountAsync(connection, transaction, "Orders", OrderPredicate, cancellationToken),
            await CountAsync(connection, transaction, "Deals", DealPredicate, cancellationToken),
            await CountAsync(connection, transaction, "FollowUps", FollowUpPredicate, cancellationToken),
            await CountAsync(connection, transaction, "CustomerNotes", NotePredicate, cancellationToken),
            await CountAsync(connection, transaction, "PriceAdjustments", PriceAdjustmentPredicate, cancellationToken),
            await CountAsync(connection, transaction, "ActivityLogs", ActivityLogPredicate, cancellationToken),
            await CountAsync(connection, transaction, "ConversationMessages", ConversationMessagePredicate, cancellationToken),
            await CountAsync(connection, transaction, "AiSuggestions", AiSuggestionPredicate, cancellationToken),
            await CountAsync(connection, transaction, "OcrResults", OcrResultPredicate, cancellationToken),
            await CountAsync(connection, transaction, "SyncRecords", SyncRecordPredicate, cancellationToken));
    }

    private static async Task<int> CountAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        string predicate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(1) FROM {table} WHERE {predicate};";
        QaDataScope.AddScopeParameters(command);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static async Task<int> DeleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string predicate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE {predicate};";
        QaDataScope.AddScopeParameters(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public enum QaDataMaintenanceCommand
    {
        None = 0,
        Status = 1,
        Clear = 2,
        Reset = 3
    }

    public sealed record QaDataStatus(
        int CustomersCount,
        int OrdersCount,
        int DealsCount,
        int FollowUpsCount,
        int NotesCount,
        int PriceAdjustmentsCount,
        int ActivityLogsCount,
        int ConversationMessagesCount,
        int AiSuggestionsCount,
        int OcrResultsCount,
        int SyncRecordsCount)
    {
        public override string ToString()
        {
            return string.Join(
                Environment.NewLine,
                [
                    $"QA Customers count: {CustomersCount}",
                    $"QA Orders count: {OrdersCount}",
                    $"QA Deals count: {DealsCount}",
                    $"QA FollowUps count: {FollowUpsCount}",
                    $"QA Notes count: {NotesCount}",
                    $"QA PriceAdjustments count: {PriceAdjustmentsCount}",
                    $"QA ActivityLogs count: {ActivityLogsCount}",
                    $"QA ConversationMessages count: {ConversationMessagesCount}",
                    $"QA AiSuggestions count: {AiSuggestionsCount}",
                    $"QA OcrResults count: {OcrResultsCount}",
                    $"QA SyncRecords count: {SyncRecordsCount}"
                ]);
        }
    }

    public sealed class QaDataClearResult
    {
        public int CustomersDeleted { get; init; }
        public int OrdersDeleted { get; init; }
        public int DealsDeleted { get; init; }
        public int FollowUpsDeleted { get; init; }
        public int NotesDeleted { get; init; }
        public int PriceAdjustmentsDeleted { get; init; }
        public int ActivityLogsDeleted { get; init; }
        public int ConversationMessagesDeleted { get; init; }
        public int AiSuggestionsDeleted { get; init; }
        public int OcrResultsDeleted { get; init; }
        public int SyncRecordsDeleted { get; init; }
        public QaDataStatus Status { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public override string ToString()
        {
            var lines = new List<string>
            {
                $"QA clear deleted syncRecords: {SyncRecordsDeleted}",
                $"QA clear deleted aiSuggestions: {AiSuggestionsDeleted}",
                $"QA clear deleted ocrResults: {OcrResultsDeleted}",
                $"QA clear deleted conversationMessages: {ConversationMessagesDeleted}",
                $"QA clear deleted customers: {CustomersDeleted}",
                $"QA clear deleted orders: {OrdersDeleted}",
                $"QA clear deleted deals: {DealsDeleted}",
                $"QA clear deleted followUps: {FollowUpsDeleted}",
                $"QA clear deleted notes: {NotesDeleted}",
                $"QA clear deleted priceAdjustments: {PriceAdjustmentsDeleted}",
                $"QA clear deleted activityLogs: {ActivityLogsDeleted}",
                Status.ToString()
            };

            return string.Join(
                Environment.NewLine,
                lines);
        }
    }

    public sealed record QaResetResult(
        QaDataClearResult ClearResult,
        QaDataSeeder.QaSeedResult SeedResult,
        QaDataStatus Status)
    {
        public override string ToString()
        {
            return string.Join(
                Environment.NewLine,
                [
                    "QA reset completed.",
                    ClearResult.ToString(),
                    $"QA seed result: {SeedResult}",
                    Status.ToString()
                ]);
        }
    }
}
