using Microsoft.Data.Sqlite;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class QaDataMaintenanceService
{
    private const string MarkerPatternParameterName = "$marker";
    private static readonly string MarkerPattern = $"%{QaDataSeeder.QaMarker}%";

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

        var result = new QaDataClearResult
        {
            ActivityLogsDeleted = await DeleteAsync(connection, transaction, "ActivityLogs", ActivityLogPredicate, cancellationToken),
            PriceAdjustmentsDeleted = await DeleteAsync(connection, transaction, "PriceAdjustments", PriceAdjustmentPredicate, cancellationToken),
            NotesDeleted = await DeleteAsync(connection, transaction, "CustomerNotes", NotePredicate, cancellationToken),
            FollowUpsDeleted = await DeleteAsync(connection, transaction, "FollowUps", FollowUpPredicate, cancellationToken),
            OrdersDeleted = await DeleteAsync(connection, transaction, "Orders", SafeOrderDeletePredicate, cancellationToken),
            DealsDeleted = await DeleteAsync(connection, transaction, "Deals", SafeDealDeletePredicate, cancellationToken),
            CustomersDeleted = await DeleteAsync(connection, transaction, "Customers", SafeCustomerDeletePredicate, cancellationToken)
        };

        await transaction.CommitAsync(cancellationToken);
        result.Status = await GetStatusAsync(cancellationToken);
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
            await CountAsync(connection, transaction, "ActivityLogs", ActivityLogPredicate, cancellationToken));
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
        command.Parameters.AddWithValue(MarkerPatternParameterName, MarkerPattern);
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
        command.Parameters.AddWithValue(MarkerPatternParameterName, MarkerPattern);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string CustomerPredicate = """
        Name LIKE $marker OR Remark LIKE $marker
        """;

    private const string DealPredicate = """
        Title LIKE $marker OR Requirement LIKE $marker
        """;

    private const string OrderPredicate = """
        Title LIKE $marker OR Requirement LIKE $marker
        """;

    private const string FollowUpPredicate = """
        Title LIKE $marker OR Content LIKE $marker
        """;

    private const string NotePredicate = """
        Content LIKE $marker
        """;

    private const string PriceAdjustmentPredicate = """
        Reason LIKE $marker
        """;

    private const string ActivityLogPredicate = """
        Title LIKE $marker OR Description LIKE $marker OR MetadataJson LIKE $marker
        """;

    private const string SafeOrderDeletePredicate = """
        (Title LIKE $marker OR Requirement LIKE $marker)
        AND Id NOT IN (
            SELECT OrderId FROM FollowUps WHERE OrderId IS NOT NULL
            UNION
            SELECT OrderId FROM CustomerNotes WHERE OrderId IS NOT NULL
            UNION
            SELECT OrderId FROM PriceAdjustments WHERE OrderId IS NOT NULL
            UNION
            SELECT OrderId FROM ActivityLogs WHERE OrderId IS NOT NULL
        )
        """;

    private const string SafeDealDeletePredicate = """
        (Title LIKE $marker OR Requirement LIKE $marker)
        AND Id NOT IN (
            SELECT DealId FROM Orders WHERE DealId IS NOT NULL
            UNION
            SELECT DealId FROM FollowUps WHERE DealId IS NOT NULL
            UNION
            SELECT DealId FROM CustomerNotes WHERE DealId IS NOT NULL
            UNION
            SELECT DealId FROM PriceAdjustments WHERE DealId IS NOT NULL
            UNION
            SELECT DealId FROM ActivityLogs WHERE DealId IS NOT NULL
        )
        """;

    private const string SafeCustomerDeletePredicate = """
        (Name LIKE $marker OR Remark LIKE $marker)
        AND Id NOT IN (
            SELECT CustomerId FROM Orders WHERE CustomerId IS NOT NULL
            UNION
            SELECT CustomerId FROM Deals WHERE CustomerId IS NOT NULL
            UNION
            SELECT CustomerId FROM FollowUps WHERE CustomerId IS NOT NULL
            UNION
            SELECT CustomerId FROM CustomerNotes WHERE CustomerId IS NOT NULL
            UNION
            SELECT CustomerId FROM PriceAdjustments WHERE CustomerId IS NOT NULL
            UNION
            SELECT CustomerId FROM ActivityLogs WHERE CustomerId IS NOT NULL
        )
        """;

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
        int ActivityLogsCount)
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
                    $"QA ActivityLogs count: {ActivityLogsCount}"
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
        public QaDataStatus Status { get; set; } = new(0, 0, 0, 0, 0, 0, 0);

        public override string ToString()
        {
            var lines = new List<string>
            {
                $"QA clear deleted customers: {CustomersDeleted}",
                $"QA clear deleted orders: {OrdersDeleted}",
                $"QA clear deleted deals: {DealsDeleted}",
                $"QA clear deleted followUps: {FollowUpsDeleted}",
                $"QA clear deleted notes: {NotesDeleted}",
                $"QA clear deleted priceAdjustments: {PriceAdjustmentsDeleted}",
                $"QA clear deleted activityLogs: {ActivityLogsDeleted}",
                Status.ToString()
            };

            if (Status.CustomersCount > 0 || Status.OrdersCount > 0 || Status.DealsCount > 0)
            {
                lines.Add("QA clear note: remaining QA parent records are still referenced by non-QA history/order data, so they were preserved to avoid touching non-QA records.");
            }

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
