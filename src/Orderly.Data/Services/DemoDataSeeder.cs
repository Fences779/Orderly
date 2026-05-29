using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class DemoDataSeeder
{
    private const string DemoMarker = "[DEMO]";
    private const string DemoKeyPattern = "demo-%";
    private const int MinimumCustomers = 3;
    private const int MinimumOrders = 5;
    private const int MinimumFollowUps = 3;
    private const int MinimumNotes = 3;
    private const int MinimumPriceAdjustments = 2;
    private const int MinimumActivityLogs = 8;

    private readonly SqliteConnectionFactory _connectionFactory;

    public DemoDataSeeder(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public static bool IsRequested(IEnumerable<string>? args = null)
    {
        if (args?.Any(IsDemoArgument) == true)
        {
            return true;
        }

        return IsEnabled(Environment.GetEnvironmentVariable("ORDERLY_DEMO_MODE"))
            || IsEnabled(Environment.GetEnvironmentVariable("ORDERLY_OFFLINE_DEMO_MODE"))
            || IsEnabled(Environment.GetEnvironmentVariable("ORDERLY_SEED_DEMO_DATA"));
    }

    public async Task SeedIfNeededAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var counts = await CountDemoDataAsync(connection, transaction, cancellationToken);
        if (counts.IsComplete)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = DateTime.Now;
        var customerCount = counts.Customers;

        if (customerCount < MinimumCustomers)
        {
            await EnsureMinimumCustomersAsync(connection, transaction, now, customerCount, cancellationToken);
        }

        if (counts.Orders < MinimumOrders)
        {
            await EnsureMinimumOrdersAsync(connection, transaction, now, counts.Orders, cancellationToken);
        }

        if (counts.FollowUps < MinimumFollowUps)
        {
            await EnsureMinimumFollowUpsAsync(connection, transaction, now, counts.FollowUps, cancellationToken);
        }

        if (counts.Notes < MinimumNotes)
        {
            await EnsureMinimumNotesAsync(connection, transaction, now, counts.Notes, cancellationToken);
        }

        if (counts.PriceAdjustments < MinimumPriceAdjustments)
        {
            await EnsureMinimumPriceAdjustmentsAsync(connection, transaction, now, counts.PriceAdjustments, cancellationToken);
        }

        if (counts.ActivityLogs < MinimumActivityLogs)
        {
            await EnsureMinimumActivityLogsAsync(connection, transaction, now, counts.ActivityLogs, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsDemoArgument(string arg)
    {
        return string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--offline-demo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--seed-demo-data", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<DemoCounts> CountDemoDataAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        return new DemoCounts(
            await CountAsync(connection, transaction, "Customers", "Name LIKE $marker OR Remark LIKE $marker OR ExternalId LIKE $demoKey", cancellationToken),
            await CountAsync(connection, transaction, "Orders", "Title LIKE $marker OR Requirement LIKE $marker OR ExternalId LIKE $demoKey", cancellationToken),
            await CountAsync(connection, transaction, "FollowUps", "Title LIKE $marker OR Content LIKE $marker OR RemoteId LIKE $demoKey", cancellationToken),
            await CountAsync(connection, transaction, "CustomerNotes", "Content LIKE $marker OR RemoteId LIKE $demoKey", cancellationToken),
            await CountAsync(connection, transaction, "PriceAdjustments", "Reason LIKE $marker OR RemoteId LIKE $demoKey", cancellationToken),
            await CountAsync(connection, transaction, "ActivityLogs", "Title LIKE $marker OR Description LIKE $marker OR MetadataJson LIKE $marker OR RemoteId LIKE $demoKey", cancellationToken));
    }

    private static async Task<int> CountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string demoPredicate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(1) FROM {table} WHERE DeletedAt IS NULL AND ({demoPredicate});";
        command.Parameters.AddWithValue("$marker", $"%{DemoMarker}%");
        command.Parameters.AddWithValue("$demoKey", DemoKeyPattern);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureMinimumCustomersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime now,
        int currentCount,
        CancellationToken cancellationToken)
    {
        foreach (var customer in DemoCustomers)
        {
            if (currentCount >= MinimumCustomers)
            {
                return;
            }

            if (await GetIdByTextKeyAsync(connection, transaction, "Customers", "ExternalId", customer.ExternalId, cancellationToken) is not null)
            {
                continue;
            }

            await InsertCustomerAsync(connection, transaction, customer, now, cancellationToken);
            currentCount++;
        }
    }

    private static async Task EnsureMinimumOrdersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime now,
        int currentCount,
        CancellationToken cancellationToken)
    {
        foreach (var order in DemoOrders)
        {
            if (currentCount >= MinimumOrders)
            {
                return;
            }

            if (await GetIdByTextKeyAsync(connection, transaction, "Orders", "ExternalId", order.ExternalId, cancellationToken) is not null)
            {
                continue;
            }

            var customerId = await EnsureCustomerAsync(connection, transaction, order.CustomerExternalId, now, cancellationToken);
            await InsertOrderAsync(connection, transaction, order, customerId, now, cancellationToken);
            currentCount++;
        }
    }

    private static async Task EnsureMinimumFollowUpsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime now,
        int currentCount,
        CancellationToken cancellationToken)
    {
        foreach (var followUp in DemoFollowUps)
        {
            if (currentCount >= MinimumFollowUps)
            {
                return;
            }

            if (await GetIdByTextKeyAsync(connection, transaction, "FollowUps", "RemoteId", followUp.RemoteId, cancellationToken) is not null)
            {
                continue;
            }

            var customerId = await EnsureCustomerAsync(connection, transaction, followUp.CustomerExternalId, now, cancellationToken);
            var orderId = await GetIdByTextKeyAsync(connection, transaction, "Orders", "ExternalId", followUp.OrderExternalId, cancellationToken);
            await InsertFollowUpAsync(connection, transaction, followUp, customerId, orderId, now, cancellationToken);
            currentCount++;
        }
    }

    private static async Task EnsureMinimumNotesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime now,
        int currentCount,
        CancellationToken cancellationToken)
    {
        foreach (var note in DemoNotes)
        {
            if (currentCount >= MinimumNotes)
            {
                return;
            }

            if (await GetIdByTextKeyAsync(connection, transaction, "CustomerNotes", "RemoteId", note.RemoteId, cancellationToken) is not null)
            {
                continue;
            }

            var customerId = await EnsureCustomerAsync(connection, transaction, note.CustomerExternalId, now, cancellationToken);
            var orderId = await GetIdByTextKeyAsync(connection, transaction, "Orders", "ExternalId", note.OrderExternalId, cancellationToken);
            await InsertNoteAsync(connection, transaction, note, customerId, orderId, now, cancellationToken);
            currentCount++;
        }
    }

    private static async Task EnsureMinimumPriceAdjustmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime now,
        int currentCount,
        CancellationToken cancellationToken)
    {
        foreach (var adjustment in DemoPriceAdjustments)
        {
            if (currentCount >= MinimumPriceAdjustments)
            {
                return;
            }

            if (await GetIdByTextKeyAsync(connection, transaction, "PriceAdjustments", "RemoteId", adjustment.RemoteId, cancellationToken) is not null)
            {
                continue;
            }

            var customerId = await EnsureCustomerAsync(connection, transaction, adjustment.CustomerExternalId, now, cancellationToken);
            var orderId = await GetIdByTextKeyAsync(connection, transaction, "Orders", "ExternalId", adjustment.OrderExternalId, cancellationToken);
            await InsertPriceAdjustmentAsync(connection, transaction, adjustment, customerId, orderId, now, cancellationToken);
            currentCount++;
        }
    }

    private static async Task EnsureMinimumActivityLogsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime now,
        int currentCount,
        CancellationToken cancellationToken)
    {
        foreach (var activity in DemoActivityLogs)
        {
            if (currentCount >= MinimumActivityLogs)
            {
                return;
            }

            if (await GetIdByTextKeyAsync(connection, transaction, "ActivityLogs", "RemoteId", activity.RemoteId, cancellationToken) is not null)
            {
                continue;
            }

            var customerId = await GetIdByTextKeyAsync(connection, transaction, "Customers", "ExternalId", activity.CustomerExternalId, cancellationToken);
            var orderId = await GetIdByTextKeyAsync(connection, transaction, "Orders", "ExternalId", activity.OrderExternalId, cancellationToken);
            await InsertActivityLogAsync(connection, transaction, activity, customerId, orderId, now, cancellationToken);
            currentCount++;
        }
    }

    private static async Task<int> EnsureCustomerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string externalId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdByTextKeyAsync(connection, transaction, "Customers", "ExternalId", externalId, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var customer = DemoCustomers.First(item => item.ExternalId == externalId);
        return await InsertCustomerAsync(connection, transaction, customer, now, cancellationToken);
    }

    private static async Task<int?> GetIdByTextKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string keyColumn,
        string? key,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT Id FROM {table} WHERE {keyColumn} = $key AND DeletedAt IS NULL LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private sealed record DemoCounts(
        int Customers,
        int Orders,
        int FollowUps,
        int Notes,
        int PriceAdjustments,
        int ActivityLogs)
    {
        public bool IsComplete =>
            Customers >= MinimumCustomers
            && Orders >= MinimumOrders
            && FollowUps >= MinimumFollowUps
            && Notes >= MinimumNotes
            && PriceAdjustments >= MinimumPriceAdjustments
            && ActivityLogs >= MinimumActivityLogs;
    }
}
