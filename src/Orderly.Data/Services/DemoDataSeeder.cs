using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class DemoDataSeeder
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

    private static async Task<int> InsertCustomerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DemoCustomer customer,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Customers (
                Name, Status, Priority, SourcePlatform, Channel, ContactHandle, Phone, Remark, ExternalId, RawPayload,
                LastContactAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $name, $status, $priority, $sourcePlatform, $channel, $contactHandle, $phone, $remark, $externalId, $rawPayload,
                $lastContactAt, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", customer.Name);
        command.Parameters.AddWithValue("$status", (int)CustomerStatus.Active);
        command.Parameters.AddWithValue("$priority", (int)customer.Priority);
        command.Parameters.AddWithValue("$sourcePlatform", customer.SourcePlatform);
        command.Parameters.AddWithValue("$channel", customer.Channel);
        command.Parameters.AddWithValue("$contactHandle", customer.ContactHandle);
        command.Parameters.AddWithValue("$phone", customer.Phone);
        command.Parameters.AddWithValue("$remark", customer.Remark);
        command.Parameters.AddWithValue("$externalId", customer.ExternalId);
        command.Parameters.AddWithValue("$rawPayload", "{}");
        command.Parameters.AddWithValue("$lastContactAt", now.AddHours(customer.LastContactOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(customer.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", customer.RemoteId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DemoOrder order,
        int customerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Orders (
                CustomerId, DealId, Title, Status, Amount, Requirement, SourcePlatform, Channel,
                ExternalId, RawPayload, NextFollowUpAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, NULL, $title, $status, $amount, $requirement, $sourcePlatform, $channel,
                $externalId, '{}', $nextFollowUpAt, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$title", order.Title);
        command.Parameters.AddWithValue("$status", (int)order.Status);
        command.Parameters.AddWithValue("$amount", order.Amount);
        command.Parameters.AddWithValue("$requirement", order.Requirement);
        command.Parameters.AddWithValue("$sourcePlatform", order.SourcePlatform);
        command.Parameters.AddWithValue("$channel", order.Channel);
        command.Parameters.AddWithValue("$externalId", order.ExternalId);
        command.Parameters.AddWithValue("$nextFollowUpAt", now.AddHours(order.NextFollowUpOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(order.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", order.RemoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFollowUpAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DemoFollowUp followUp,
        int customerId,
        int? orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO FollowUps (
                CustomerId, DealId, OrderId, Title, Content, Status, ScheduledAt, CompletedAt, ReminderAt,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, NULL, $orderId, $title, $content, $status, $scheduledAt, NULL, $reminderAt,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$title", followUp.Title);
        command.Parameters.AddWithValue("$content", followUp.Content);
        command.Parameters.AddWithValue("$status", (int)followUp.Status);
        command.Parameters.AddWithValue("$scheduledAt", now.AddHours(followUp.ScheduledOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$reminderAt", now.AddHours(followUp.ReminderOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(followUp.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", followUp.RemoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertNoteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DemoNote note,
        int customerId,
        int? orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CustomerNotes (
                CustomerId, DealId, OrderId, Type, Content, IsPinned,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, NULL, $orderId, $type, $content, $isPinned,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$type", (int)note.Type);
        command.Parameters.AddWithValue("$content", note.Content);
        command.Parameters.AddWithValue("$isPinned", note.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", now.AddDays(note.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", note.RemoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPriceAdjustmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DemoPriceAdjustment adjustment,
        int customerId,
        int? orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PriceAdjustments (
                CustomerId, DealId, OrderId, OriginalAmount, AdjustedAmount, Reason, Status,
                RequestedBy, ApprovedBy, ApprovedAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, NULL, $orderId, $originalAmount, $adjustedAmount, $reason, $status,
                $requestedBy, $approvedBy, $approvedAt, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$originalAmount", adjustment.OriginalAmount);
        command.Parameters.AddWithValue("$adjustedAmount", adjustment.AdjustedAmount);
        command.Parameters.AddWithValue("$reason", adjustment.Reason);
        command.Parameters.AddWithValue("$status", (int)adjustment.Status);
        command.Parameters.AddWithValue("$requestedBy", "demo");
        command.Parameters.AddWithValue("$approvedBy", adjustment.Status == PriceAdjustmentStatus.Approved ? "demo-manager" : string.Empty);
        command.Parameters.AddWithValue("$approvedAt", adjustment.Status == PriceAdjustmentStatus.Approved ? now.AddHours(-2).ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", now.AddDays(adjustment.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", adjustment.RemoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertActivityLogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DemoActivityLog activity,
        int? customerId,
        int? orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ActivityLogs (
                Type, CustomerId, DealId, OrderId, Title, Description, Operator, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $type, $customerId, NULL, $orderId, $title, $description, 'demo', $metadataJson,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        command.Parameters.AddWithValue("$type", (int)activity.Type);
        command.Parameters.AddWithValue("$customerId", ToDbInt(customerId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$title", activity.Title);
        command.Parameters.AddWithValue("$description", activity.Description);
        command.Parameters.AddWithValue("$metadataJson", $$"""{"source":"{{DemoMarker}} seed","key":"{{activity.RemoteId}}"}""");
        command.Parameters.AddWithValue("$createdAt", now.AddHours(activity.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", activity.RemoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ToDbInt(int? value)
    {
        return value is null ? DBNull.Value : value.Value;
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

    private sealed record DemoCustomer(
        string ExternalId,
        string RemoteId,
        string Name,
        CustomerPriority Priority,
        string SourcePlatform,
        string Channel,
        string ContactHandle,
        string Phone,
        string Remark,
        int CreatedOffsetDays,
        int LastContactOffsetHours);

    private sealed record DemoOrder(
        string ExternalId,
        string RemoteId,
        string CustomerExternalId,
        string Title,
        OrderStatus Status,
        decimal Amount,
        string Requirement,
        string SourcePlatform,
        string Channel,
        int CreatedOffsetDays,
        int NextFollowUpOffsetHours);

    private sealed record DemoFollowUp(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        string Title,
        string Content,
        FollowUpStatus Status,
        int CreatedOffsetDays,
        int ScheduledOffsetHours,
        int ReminderOffsetHours);

    private sealed record DemoNote(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        NoteType Type,
        string Content,
        bool IsPinned,
        int CreatedOffsetDays);

    private sealed record DemoPriceAdjustment(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        decimal OriginalAmount,
        decimal AdjustedAmount,
        string Reason,
        PriceAdjustmentStatus Status,
        int CreatedOffsetDays);

    private sealed record DemoActivityLog(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        ActivityType Type,
        string Title,
        string Description,
        int CreatedOffsetHours);

    private static readonly DemoCustomer[] DemoCustomers =
    [
        new("demo-customer-001", "demo-customer-001", $"{DemoMarker} 林小姐", CustomerPriority.High, "微信", "私域咨询", "demo_lin", "13800001001", $"{DemoMarker} 偏好自然风格，预算明确，适合演示客户详情。", -12, -3),
        new("demo-customer-002", "demo-customer-002", $"{DemoMarker} 陈先生", CustomerPriority.Normal, "闲鱼", "二手平台", "demo_chen", "13800001002", $"{DemoMarker} 关注交付周期和售后说明，适合演示跟进。", -9, -8),
        new("demo-customer-003", "demo-customer-003", $"{DemoMarker} 周老板", CustomerPriority.Critical, "微信", "老客复购", "demo_zhou", "13800001003", $"{DemoMarker} 企业礼品复购客户，适合演示大额订单和改价。", -6, -1)
    ];

    private static readonly DemoOrder[] DemoOrders =
    [
        new("demo-order-001", "demo-order-001", "demo-customer-001", $"{DemoMarker} 婚礼伴手礼定制", OrderStatus.PendingCommunication, 0m, $"{DemoMarker} 需要确认数量、包装和交付日期。", "微信", "私域咨询", -10, 4),
        new("demo-order-002", "demo-order-002", "demo-customer-001", $"{DemoMarker} 家庭纪念照相框", OrderStatus.PendingQuote, 0m, $"{DemoMarker} 客户已发尺寸，待整理基础版和升级版报价。", "微信", "私域咨询", -7, 22),
        new("demo-order-003", "demo-order-003", "demo-customer-002", $"{DemoMarker} 闲鱼摆件修复", OrderStatus.Quoted, 680m, $"{DemoMarker} 已发报价，等待客户确认是否加急。", "闲鱼", "二手平台", -5, 30),
        new("demo-order-004", "demo-order-004", "demo-customer-002", $"{DemoMarker} 旧物翻新加急单", OrderStatus.PendingFollowUp, 1280m, $"{DemoMarker} 客户担心周期，需要补充工期说明。", "闲鱼", "二手平台", -3, 2),
        new("demo-order-005", "demo-order-005", "demo-customer-003", $"{DemoMarker} 企业年会礼盒", OrderStatus.Won, 9600m, $"{DemoMarker} 已收定金，准备排产并确认发票信息。", "微信", "老客复购", -2, 48)
    ];

    private static readonly DemoFollowUp[] DemoFollowUps =
    [
        new("demo-followup-001", "demo-customer-001", "demo-order-001", $"{DemoMarker} 确认婚礼伴手礼数量", $"{DemoMarker} 询问最终数量、包装色系和期望交付日期。", FollowUpStatus.Pending, -2, 3, 2),
        new("demo-followup-002", "demo-customer-002", "demo-order-004", $"{DemoMarker} 补充加急工期说明", $"{DemoMarker} 发送加急排期和额外费用说明。", FollowUpStatus.InProgress, -2, -30, -31),
        new("demo-followup-003", "demo-customer-003", "demo-order-005", $"{DemoMarker} 企业礼盒排产同步", $"{DemoMarker} 同步打样进度，提醒客户确认发票抬头。", FollowUpStatus.Pending, -1, 24, 23)
    ];

    private static readonly DemoNote[] DemoNotes =
    [
        new("demo-note-001", "demo-customer-001", "demo-order-001", NoteType.Preference, $"{DemoMarker} 喜欢低饱和米白色包装，不接受过度花哨设计。", true, -8),
        new("demo-note-002", "demo-customer-002", "demo-order-004", NoteType.Risk, $"{DemoMarker} 对交付时间敏感，报价时必须写清楚加急风险。", false, -4),
        new("demo-note-003", "demo-customer-003", "demo-order-005", NoteType.Requirement, $"{DemoMarker} 企业礼盒需要统一 logo、发票和批量物流单号。", true, -2)
    ];

    private static readonly DemoPriceAdjustment[] DemoPriceAdjustments =
    [
        new("demo-price-001", "demo-customer-002", "demo-order-004", 1480m, 1280m, $"{DemoMarker} 老客户转介绍，申请减免部分加急费用。", PriceAdjustmentStatus.PendingApproval, -2),
        new("demo-price-002", "demo-customer-003", "demo-order-005", 10200m, 9600m, $"{DemoMarker} 企业批量采购，已按阶梯价审批。", PriceAdjustmentStatus.Approved, -1)
    ];

    private static readonly DemoActivityLog[] DemoActivityLogs =
    [
        new("demo-activity-001", "demo-customer-001", "demo-order-001", ActivityType.CustomerCreated, $"{DemoMarker} 新增客户", $"{DemoMarker} 从微信私域新增林小姐。", -36),
        new("demo-activity-002", "demo-customer-001", "demo-order-001", ActivityType.OrderCreated, $"{DemoMarker} 创建订单", $"{DemoMarker} 婚礼伴手礼定制需求已建单。", -30),
        new("demo-activity-003", "demo-customer-001", "demo-order-002", ActivityType.NoteCreated, $"{DemoMarker} 新增备注", $"{DemoMarker} 记录包装偏好和报价范围。", -28),
        new("demo-activity-004", "demo-customer-002", "demo-order-003", ActivityType.OrderStatusChanged, $"{DemoMarker} 订单状态变更", $"{DemoMarker} 闲鱼摆件修复已报价。", -24),
        new("demo-activity-005", "demo-customer-002", "demo-order-004", ActivityType.FollowUpCreated, $"{DemoMarker} 新增跟进", $"{DemoMarker} 安排加急工期说明。", -18),
        new("demo-activity-006", "demo-customer-002", "demo-order-004", ActivityType.PriceAdjustmentRequested, $"{DemoMarker} 发起改价", $"{DemoMarker} 申请加急费用减免。", -12),
        new("demo-activity-007", "demo-customer-003", "demo-order-005", ActivityType.PriceAdjustmentApproved, $"{DemoMarker} 改价通过", $"{DemoMarker} 企业批量价审批通过。", -8),
        new("demo-activity-008", "demo-customer-003", "demo-order-005", ActivityType.FollowUpCreated, $"{DemoMarker} 新增跟进", $"{DemoMarker} 安排企业礼盒排产同步。", -3)
    ];
}
