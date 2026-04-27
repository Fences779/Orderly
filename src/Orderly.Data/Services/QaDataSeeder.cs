using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class QaDataSeeder
{
    public const string QaMarker = "[P1.3_QA]";

    private readonly SqliteConnectionFactory _connectionFactory;

    public QaDataSeeder(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public static bool IsRequested(IEnumerable<string>? args = null)
    {
        return args?.Any(IsQaArgument) == true;
    }

    public static bool IsQaMode(IEnumerable<string>? args = null)
    {
        return args?.Any(arg => string.Equals(arg, "--qa-mode", StringComparison.OrdinalIgnoreCase)) == true;
    }

    public async Task<QaSeedResult> SeedIfNeededAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var now = DateTime.Now;
        var result = new QaSeedResult();
        var customerIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var dealIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderIds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var customer in QaCustomers)
        {
            customerIds[customer.Name] = await UpsertCustomerAsync(connection, transaction, customer, now, result, cancellationToken);
        }

        foreach (var deal in QaDeals)
        {
            var customerId = customerIds[deal.CustomerName];
            dealIds[deal.Title] = await UpsertDealAsync(connection, transaction, deal, customerId, now, result, cancellationToken);
        }

        foreach (var order in QaOrders)
        {
            var customerId = customerIds[order.CustomerName];
            int? dealId = string.IsNullOrWhiteSpace(order.DealTitle) ? null : dealIds[order.DealTitle];
            orderIds[order.Title] = await UpsertOrderAsync(connection, transaction, order, customerId, dealId, now, result, cancellationToken);
        }

        foreach (var followUp in QaFollowUps)
        {
            var customerId = customerIds[followUp.CustomerName];
            var orderId = orderIds[followUp.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(followUp.DealTitle) ? null : dealIds[followUp.DealTitle];
            await UpsertFollowUpAsync(connection, transaction, followUp, customerId, orderId, dealId, now, result, cancellationToken);
        }

        foreach (var note in QaNotes)
        {
            var customerId = customerIds[note.CustomerName];
            var orderId = orderIds[note.OrderTitle];
            await UpsertNoteAsync(connection, transaction, note, customerId, orderId, now, result, cancellationToken);
        }

        foreach (var adjustment in QaPriceAdjustments)
        {
            var customerId = customerIds[adjustment.CustomerName];
            var orderId = orderIds[adjustment.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(adjustment.DealTitle) ? null : dealIds[adjustment.DealTitle];
            await UpsertPriceAdjustmentAsync(connection, transaction, adjustment, customerId, orderId, dealId, now, result, cancellationToken);
        }

        foreach (var activity in QaActivityLogs)
        {
            var customerId = customerIds[activity.CustomerName];
            int? orderId = string.IsNullOrWhiteSpace(activity.OrderTitle) ? null : orderIds[activity.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(activity.DealTitle) ? null : dealIds[activity.DealTitle];
            await UpsertActivityLogAsync(connection, transaction, activity, customerId, orderId, dealId, now, result, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static bool IsQaArgument(string arg)
    {
        return string.Equals(arg, "--qa-mode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--seed-qa-data", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> UpsertCustomerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaCustomer customer,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM Customers
            WHERE DeletedAt IS NULL
              AND Name = $name
              AND Name LIKE $marker
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$name", customer.Name);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int customerId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Customers
                SET Status = $status,
                    Priority = $priority,
                    SourcePlatform = $sourcePlatform,
                    Channel = $channel,
                    ContactHandle = $contactHandle,
                    Phone = $phone,
                    Remark = $remark,
                    ExternalId = $externalId,
                    RawPayload = $rawPayload,
                    LastContactAt = $lastContactAt,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddCustomerParameters(update, customer, now);
            update.Parameters.AddWithValue("$id", customerId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.CustomersUpdated++;
            return customerId;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
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
        AddCustomerParameters(insert, customer, now);
        var insertedId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
        result.CustomersInserted++;
        return insertedId;
    }

    private static async Task<int> UpsertDealAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaDeal deal,
        int customerId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM Deals
            WHERE DeletedAt IS NULL
              AND Title = $title
              AND (Title LIKE $marker OR Requirement LIKE $marker)
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", deal.Title);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int dealId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Deals
                SET CustomerId = $customerId,
                    Title = $title,
                    Stage = $stage,
                    EstimatedAmount = $estimatedAmount,
                    Requirement = $requirement,
                    SourcePlatform = $sourcePlatform,
                    Channel = $channel,
                    ExpectedCloseAt = $expectedCloseAt,
                    ClosedAt = $closedAt,
                    LostReason = $lostReason,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddDealParameters(update, deal, customerId, now);
            update.Parameters.AddWithValue("$id", dealId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.DealsUpdated++;
            return dealId;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Deals (
                CustomerId, Title, Stage, EstimatedAmount, Requirement, SourcePlatform, Channel,
                ExpectedCloseAt, ClosedAt, LostReason, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $title, $stage, $estimatedAmount, $requirement, $sourcePlatform, $channel,
                $expectedCloseAt, $closedAt, $lostReason, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            SELECT last_insert_rowid();
            """;
        AddDealParameters(insert, deal, customerId, now);
        var insertedId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
        result.DealsInserted++;
        return insertedId;
    }

    private static async Task<int> UpsertOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaOrder order,
        int customerId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM Orders
            WHERE DeletedAt IS NULL
              AND Title = $title
              AND (Title LIKE $marker OR Requirement LIKE $marker)
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", order.Title);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int orderId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Orders
                SET CustomerId = $customerId,
                    DealId = $dealId,
                    Title = $title,
                    Status = $status,
                    Amount = $amount,
                    Requirement = $requirement,
                    SourcePlatform = $sourcePlatform,
                    Channel = $channel,
                    ExternalId = $externalId,
                    RawPayload = $rawPayload,
                    NextFollowUpAt = $nextFollowUpAt,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddOrderParameters(update, order, customerId, dealId, now);
            update.Parameters.AddWithValue("$id", orderId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.OrdersUpdated++;
            return orderId;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Orders (
                CustomerId, DealId, Title, Status, Amount, Requirement, SourcePlatform, Channel,
                ExternalId, RawPayload, NextFollowUpAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $title, $status, $amount, $requirement, $sourcePlatform, $channel,
                $externalId, $rawPayload, $nextFollowUpAt, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            SELECT last_insert_rowid();
            """;
        AddOrderParameters(insert, order, customerId, dealId, now);
        var insertedId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
        result.OrdersInserted++;
        return insertedId;
    }

    private static async Task UpsertFollowUpAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaFollowUp followUp,
        int customerId,
        int orderId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM FollowUps
            WHERE DeletedAt IS NULL
              AND Title = $title
              AND Title LIKE $marker
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", followUp.Title);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int followUpId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE FollowUps
                SET CustomerId = $customerId,
                    DealId = $dealId,
                    OrderId = $orderId,
                    Title = $title,
                    Content = $content,
                    Status = $status,
                    ScheduledAt = $scheduledAt,
                    CompletedAt = $completedAt,
                    ReminderAt = $reminderAt,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddFollowUpParameters(update, followUp, customerId, orderId, dealId, now);
            update.Parameters.AddWithValue("$id", followUpId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.FollowUpsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO FollowUps (
                CustomerId, DealId, OrderId, Title, Content, Status, ScheduledAt, CompletedAt, ReminderAt,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $title, $content, $status, $scheduledAt, $completedAt, $reminderAt,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddFollowUpParameters(insert, followUp, customerId, orderId, dealId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.FollowUpsInserted++;
    }

    private static async Task UpsertNoteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaNote note,
        int customerId,
        int orderId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM CustomerNotes
            WHERE DeletedAt IS NULL
              AND Content = $content
              AND Content LIKE $marker
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$content", note.Content);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int noteId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE CustomerNotes
                SET CustomerId = $customerId,
                    DealId = NULL,
                    OrderId = $orderId,
                    Type = $type,
                    Content = $content,
                    IsPinned = $isPinned,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddNoteParameters(update, note, customerId, orderId, now);
            update.Parameters.AddWithValue("$id", noteId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.NotesUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO CustomerNotes (
                CustomerId, DealId, OrderId, Type, Content, IsPinned, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, NULL, $orderId, $type, $content, $isPinned, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddNoteParameters(insert, note, customerId, orderId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.NotesInserted++;
    }

    private static async Task UpsertPriceAdjustmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaPriceAdjustment adjustment,
        int customerId,
        int orderId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM PriceAdjustments
            WHERE DeletedAt IS NULL
              AND Reason = $reason
              AND Reason LIKE $marker
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$reason", adjustment.Reason);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int adjustmentId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE PriceAdjustments
                SET CustomerId = $customerId,
                    DealId = $dealId,
                    OrderId = $orderId,
                    OriginalAmount = $originalAmount,
                    AdjustedAmount = $adjustedAmount,
                    Reason = $reason,
                    Status = $status,
                    RequestedBy = $requestedBy,
                    ApprovedBy = $approvedBy,
                    ApprovedAt = $approvedAt,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddPriceAdjustmentParameters(update, adjustment, customerId, orderId, dealId, now);
            update.Parameters.AddWithValue("$id", adjustmentId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.PriceAdjustmentsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO PriceAdjustments (
                CustomerId, DealId, OrderId, OriginalAmount, AdjustedAmount, Reason, Status,
                RequestedBy, ApprovedBy, ApprovedAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $originalAmount, $adjustedAmount, $reason, $status,
                $requestedBy, $approvedBy, $approvedAt, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddPriceAdjustmentParameters(insert, adjustment, customerId, orderId, dealId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.PriceAdjustmentsInserted++;
    }

    private static async Task UpsertActivityLogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaActivityLog activity,
        int customerId,
        int? orderId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            """
            SELECT Id
            FROM ActivityLogs
            WHERE DeletedAt IS NULL
              AND Title = $title
              AND Description = $description
              AND (Title LIKE $marker OR Description LIKE $marker)
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", activity.Title);
                command.Parameters.AddWithValue("$description", activity.Description);
                command.Parameters.AddWithValue("$marker", $"%{QaMarker}%");
            },
            cancellationToken);

        if (existingId is int activityId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ActivityLogs
                SET Type = $type,
                    CustomerId = $customerId,
                    DealId = $dealId,
                    OrderId = $orderId,
                    Title = $title,
                    Description = $description,
                    Operator = $operator,
                    MetadataJson = $metadataJson,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddActivityLogParameters(update, activity, customerId, orderId, dealId, now);
            update.Parameters.AddWithValue("$id", activityId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.ActivityLogsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ActivityLogs (
                Type, CustomerId, DealId, OrderId, Title, Description, Operator, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $type, $customerId, $dealId, $orderId, $title, $description, $operator, $metadataJson,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddActivityLogParameters(insert, activity, customerId, orderId, dealId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.ActivityLogsInserted++;
    }

    private static void AddCustomerParameters(SqliteCommand command, QaCustomer customer, DateTime now)
    {
        command.Parameters.AddWithValue("$name", customer.Name);
        command.Parameters.AddWithValue("$status", (int)customer.Status);
        command.Parameters.AddWithValue("$priority", (int)customer.Priority);
        command.Parameters.AddWithValue("$sourcePlatform", customer.SourcePlatform);
        command.Parameters.AddWithValue("$channel", customer.Channel);
        command.Parameters.AddWithValue("$contactHandle", customer.ContactHandle);
        command.Parameters.AddWithValue("$phone", customer.Phone);
        command.Parameters.AddWithValue("$remark", customer.Remark);
        command.Parameters.AddWithValue("$externalId", customer.ExternalId);
        command.Parameters.AddWithValue("$rawPayload", "{}");
        command.Parameters.AddWithValue("$lastContactAt", BuildDateValue(DateTime.Today.AddHours(customer.LastContactHour)));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(customer.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", customer.RemoteId);
    }

    private static void AddDealParameters(SqliteCommand command, QaDeal deal, int customerId, DateTime now)
    {
        DateTime? closedAt = deal.Stage == DealStage.Won ? DateTime.Today.AddDays(deal.ExpectedCloseOffsetDays).AddHours(18) : null;
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$title", deal.Title);
        command.Parameters.AddWithValue("$stage", (int)deal.Stage);
        command.Parameters.AddWithValue("$estimatedAmount", deal.EstimatedAmount);
        command.Parameters.AddWithValue("$requirement", deal.Requirement);
        command.Parameters.AddWithValue("$sourcePlatform", deal.SourcePlatform);
        command.Parameters.AddWithValue("$channel", deal.Channel);
        command.Parameters.AddWithValue("$expectedCloseAt", BuildDateValue(DateTime.Today.AddDays(deal.ExpectedCloseOffsetDays).AddHours(18)));
        command.Parameters.AddWithValue("$closedAt", BuildDateValue(closedAt));
        command.Parameters.AddWithValue("$lostReason", string.Empty);
        command.Parameters.AddWithValue("$createdAt", now.AddDays(deal.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", deal.RemoteId);
    }

    private static void AddOrderParameters(SqliteCommand command, QaOrder order, int customerId, int? dealId, DateTime now)
    {
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(dealId));
        command.Parameters.AddWithValue("$title", order.Title);
        command.Parameters.AddWithValue("$status", (int)order.Status);
        command.Parameters.AddWithValue("$amount", order.Amount);
        command.Parameters.AddWithValue("$requirement", order.Requirement);
        command.Parameters.AddWithValue("$sourcePlatform", order.SourcePlatform);
        command.Parameters.AddWithValue("$channel", order.Channel);
        command.Parameters.AddWithValue("$externalId", order.ExternalId);
        command.Parameters.AddWithValue("$rawPayload", "{}");
        command.Parameters.AddWithValue("$nextFollowUpAt", BuildDateValue(order.NextFollowUpDayOffset is null
            ? null
            : DateTime.Today.AddDays(order.NextFollowUpDayOffset.Value).AddHours(order.NextFollowUpHour)));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(order.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", order.RemoteId);
    }

    private static void AddFollowUpParameters(SqliteCommand command, QaFollowUp followUp, int customerId, int orderId, int? dealId, DateTime now)
    {
        var scheduledAt = DateTime.Today.AddDays(followUp.DayOffset).AddHours(followUp.Hour);
        var reminderAt = scheduledAt.AddHours(-1);
        DateTime? completedAt = followUp.Status == FollowUpStatus.Completed || followUp.Status == FollowUpStatus.Cancelled
            ? scheduledAt.AddHours(1)
            : null;

        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(dealId));
        command.Parameters.AddWithValue("$orderId", orderId);
        command.Parameters.AddWithValue("$title", followUp.Title);
        command.Parameters.AddWithValue("$content", followUp.Content);
        command.Parameters.AddWithValue("$status", (int)followUp.Status);
        command.Parameters.AddWithValue("$scheduledAt", scheduledAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", BuildDateValue(completedAt));
        command.Parameters.AddWithValue("$reminderAt", BuildDateValue(reminderAt));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(followUp.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", followUp.RemoteId);
    }

    private static void AddNoteParameters(SqliteCommand command, QaNote note, int customerId, int orderId, DateTime now)
    {
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$orderId", orderId);
        command.Parameters.AddWithValue("$type", (int)note.Type);
        command.Parameters.AddWithValue("$content", note.Content);
        command.Parameters.AddWithValue("$isPinned", note.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", now.AddDays(note.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", note.RemoteId);
    }

    private static void AddPriceAdjustmentParameters(SqliteCommand command, QaPriceAdjustment adjustment, int customerId, int orderId, int? dealId, DateTime now)
    {
        var approvedAt = adjustment.Status is PriceAdjustmentStatus.Approved or PriceAdjustmentStatus.Applied
            ? now.AddHours(-2)
            : (DateTime?)null;

        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(dealId));
        command.Parameters.AddWithValue("$orderId", orderId);
        command.Parameters.AddWithValue("$originalAmount", adjustment.OriginalAmount);
        command.Parameters.AddWithValue("$adjustedAmount", adjustment.AdjustedAmount);
        command.Parameters.AddWithValue("$reason", adjustment.Reason);
        command.Parameters.AddWithValue("$status", (int)adjustment.Status);
        command.Parameters.AddWithValue("$requestedBy", "qa-seed");
        command.Parameters.AddWithValue("$approvedBy", adjustment.Status is PriceAdjustmentStatus.Approved or PriceAdjustmentStatus.Applied ? "qa-manager" : string.Empty);
        command.Parameters.AddWithValue("$approvedAt", BuildDateValue(approvedAt));
        command.Parameters.AddWithValue("$createdAt", now.AddDays(adjustment.CreatedOffsetDays).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", adjustment.RemoteId);
    }

    private static void AddActivityLogParameters(SqliteCommand command, QaActivityLog activity, int customerId, int? orderId, int? dealId, DateTime now)
    {
        command.Parameters.AddWithValue("$type", (int)activity.Type);
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(dealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$title", activity.Title);
        command.Parameters.AddWithValue("$description", activity.Description);
        command.Parameters.AddWithValue("$operator", "qa-seed");
        command.Parameters.AddWithValue("$metadataJson", $$"""{"source":"{{QaMarker}} seed","key":"{{activity.RemoteId}}"}""");
        command.Parameters.AddWithValue("$createdAt", now.AddHours(activity.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", activity.RemoteId);
    }

    private static async Task<int?> GetIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        configure(command);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private static object ToDbInt(int? value)
    {
        return value is null ? DBNull.Value : value.Value;
    }

    private static object BuildDateValue(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }

    public sealed class QaSeedResult
    {
        public int CustomersInserted { get; internal set; }
        public int CustomersUpdated { get; internal set; }
        public int DealsInserted { get; internal set; }
        public int DealsUpdated { get; internal set; }
        public int OrdersInserted { get; internal set; }
        public int OrdersUpdated { get; internal set; }
        public int FollowUpsInserted { get; internal set; }
        public int FollowUpsUpdated { get; internal set; }
        public int NotesInserted { get; internal set; }
        public int NotesUpdated { get; internal set; }
        public int PriceAdjustmentsInserted { get; internal set; }
        public int PriceAdjustmentsUpdated { get; internal set; }
        public int ActivityLogsInserted { get; internal set; }
        public int ActivityLogsUpdated { get; internal set; }

        public override string ToString()
        {
            return $"customers +{CustomersInserted}/~{CustomersUpdated}, deals +{DealsInserted}/~{DealsUpdated}, orders +{OrdersInserted}/~{OrdersUpdated}, followUps +{FollowUpsInserted}/~{FollowUpsUpdated}, notes +{NotesInserted}/~{NotesUpdated}, priceAdjustments +{PriceAdjustmentsInserted}/~{PriceAdjustmentsUpdated}, activityLogs +{ActivityLogsInserted}/~{ActivityLogsUpdated}";
        }
    }

    private sealed record QaCustomer(
        string ExternalId,
        string RemoteId,
        string Name,
        CustomerStatus Status,
        CustomerPriority Priority,
        string SourcePlatform,
        string Channel,
        string ContactHandle,
        string Phone,
        string Remark,
        int CreatedOffsetDays,
        int LastContactHour);

    private sealed record QaDeal(
        string RemoteId,
        string CustomerName,
        string Title,
        DealStage Stage,
        decimal EstimatedAmount,
        string Requirement,
        string SourcePlatform,
        string Channel,
        int CreatedOffsetDays,
        int ExpectedCloseOffsetDays);

    private sealed record QaOrder(
        string ExternalId,
        string RemoteId,
        string CustomerName,
        string? DealTitle,
        string Title,
        OrderStatus Status,
        decimal Amount,
        string Requirement,
        string SourcePlatform,
        string Channel,
        int CreatedOffsetDays,
        int? NextFollowUpDayOffset,
        int NextFollowUpHour);

    private sealed record QaFollowUp(
        string RemoteId,
        string CustomerName,
        string OrderTitle,
        string? DealTitle,
        string Title,
        string Content,
        FollowUpStatus Status,
        int CreatedOffsetDays,
        int DayOffset,
        int Hour);

    private sealed record QaNote(
        string RemoteId,
        string CustomerName,
        string OrderTitle,
        NoteType Type,
        string Content,
        bool IsPinned,
        int CreatedOffsetDays);

    private sealed record QaPriceAdjustment(
        string RemoteId,
        string CustomerName,
        string OrderTitle,
        string? DealTitle,
        decimal OriginalAmount,
        decimal AdjustedAmount,
        string Reason,
        PriceAdjustmentStatus Status,
        int CreatedOffsetDays);

    private sealed record QaActivityLog(
        string RemoteId,
        string CustomerName,
        string? OrderTitle,
        string? DealTitle,
        ActivityType Type,
        string Title,
        string Description,
        int CreatedOffsetHours);

    private static readonly QaCustomer[] QaCustomers =
    [
        new("p13qa-customer-a", "p13qa-customer-a", $"{QaMarker} 客户-A", CustomerStatus.Active, CustomerPriority.High, "微信", "私域咨询", "p13qa_customer_a", "13800130001", $"{QaMarker} 用于验证 FollowUp 完成/延期/取消、状态切换和备注模板。", -7, 10),
        new("p13qa-customer-b", "p13qa-customer-b", $"{QaMarker} 客户-B", CustomerStatus.Dormant, CustomerPriority.Normal, "闲鱼", "店铺咨询", "p13qa_customer_b", "13800130002", $"{QaMarker} 用于验证已成交订单、成交阶段推进和改价终态。", -5, 15)
    ];

    private static readonly QaDeal[] QaDeals =
    [
        new("p13qa-deal-001", $"{QaMarker} 客户-A", $"{QaMarker} Deal-当前推进", DealStage.Negotiating, 1880m, $"{QaMarker} 用于验证 ChangeDealStageCommand 的可推进成交机会。", "微信", "私域咨询", -4, 2),
        new("p13qa-deal-002", $"{QaMarker} 客户-B", $"{QaMarker} Deal-已成交", DealStage.Won, 5200m, $"{QaMarker} 用于验证高阶段/已成交数据展示。", "闲鱼", "店铺咨询", -3, -1)
    ];

    private static readonly QaOrder[] QaOrders =
    [
        new("p13qa-order-001", "p13qa-order-001", $"{QaMarker} 客户-A", $"{QaMarker} Deal-当前推进", $"{QaMarker} 订单-待处理", OrderStatus.PendingFollowUp, 1680m, $"{QaMarker} 用于验证订单状态切换、跟进按钮和 AddOrder 保存回读。", "微信", "私域咨询", -4, 0, 11),
        new("p13qa-order-002", "p13qa-order-002", $"{QaMarker} 客户-B", $"{QaMarker} Deal-已成交", $"{QaMarker} 订单-已成交", OrderStatus.Won, 5200m, $"{QaMarker} 用于验证已成交订单、成交阶段和终态跟进。", "闲鱼", "店铺咨询", -3, 1, 15),
        new("p13qa-order-003", "p13qa-order-003", $"{QaMarker} 客户-A", $"{QaMarker} Deal-当前推进", $"{QaMarker} 订单-需跟进", OrderStatus.PendingQuote, 980m, $"{QaMarker} 用于验证待处理搜索、逾期跟进和改价待审批。", "微信", "私域咨询", -2, -1, 16)
    ];

    private static readonly QaFollowUp[] QaFollowUps =
    [
        new("p13qa-followup-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", $"{QaMarker} Deal-当前推进", $"{QaMarker} 今日跟进", $"{QaMarker} 今日 Pending 跟进，用于验证完成/延期/取消按钮可见。", FollowUpStatus.Pending, -1, 0, 10),
        new("p13qa-followup-002", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", $"{QaMarker} 逾期跟进", $"{QaMarker} 逾期 Pending 跟进，用于验证 overdue quick filter。", FollowUpStatus.Pending, -2, -1, 14),
        new("p13qa-followup-003", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", $"{QaMarker} 明日跟进", $"{QaMarker} 明日 Pending 跟进，用于验证 tomorrow quick filter。", FollowUpStatus.Pending, -1, 1, 9),
        new("p13qa-followup-004", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", $"{QaMarker} 已完成跟进", $"{QaMarker} 已完成终态跟进，用于验证终态隐藏操作按钮。", FollowUpStatus.Completed, -3, -2, 16)
    ];

    private static readonly QaNote[] QaNotes =
    [
        new("p13qa-note-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", NoteType.Internal, $"{QaMarker} 模板备注验证：已插入标准报价模板。p13qa-note-keyword", true, -2),
        new("p13qa-note-002", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", NoteType.Requirement, $"{QaMarker} 需确认材质、数量和最终交付日期。", false, -1),
        new("p13qa-note-003", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", NoteType.Preference, $"{QaMarker} 成交后保持每周一次回访节奏。", false, -1)
    ];

    private static readonly QaPriceAdjustment[] QaPriceAdjustments =
    [
        new("p13qa-price-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", 1180m, 980m, $"{QaMarker} UIA 改价待审批验证", PriceAdjustmentStatus.PendingApproval, -1),
        new("p13qa-price-002", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", 5600m, 5200m, $"{QaMarker} UIA 改价已通过验证", PriceAdjustmentStatus.Approved, -1)
    ];

    private static readonly QaActivityLog[] QaActivityLogs =
    [
        new("p13qa-activity-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.CustomerCreated, $"{QaMarker} 新增客户", $"{QaMarker} 新增客户-A，用于 QA 演示。", -30),
        new("p13qa-activity-002", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.OrderCreated, $"{QaMarker} 创建订单", $"{QaMarker} 创建订单-待处理。", -28),
        new("p13qa-activity-003", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.NoteCreated, $"{QaMarker} 新增备注", $"{QaMarker} 新增模板备注验证记录。", -26),
        new("p13qa-activity-004", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.FollowUpCreated, $"{QaMarker} 新增跟进", $"{QaMarker} 新增今日 Pending 跟进。", -24),
        new("p13qa-activity-005", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", ActivityType.PriceAdjustmentRequested, $"{QaMarker} 新增改价", $"{QaMarker} 发起待审批改价申请。", -22),
        new("p13qa-activity-006", $"{QaMarker} 客户-B", null, null, ActivityType.CustomerStatusChanged, $"{QaMarker} 客户状态变化", $"{QaMarker} 客户-B 状态切换到 Dormant。", -20),
        new("p13qa-activity-007", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", null, ActivityType.OrderStatusChanged, $"{QaMarker} 订单状态变化", $"{QaMarker} 订单-需跟进状态切换到 PendingQuote。", -18),
        new("p13qa-activity-008", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", $"{QaMarker} Deal-当前推进", ActivityType.DealStageChanged, $"{QaMarker} Deal 阶段变化", $"{QaMarker} Deal-当前推进已切换到 Negotiating。", -16),
        new("p13qa-activity-009", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", ActivityType.FollowUpSnoozed, $"{QaMarker} 跟进延期", $"{QaMarker} 逾期跟进已从昨天延后到今日。", -14),
        new("p13qa-activity-010", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", ActivityType.FollowUpCompleted, $"{QaMarker} 跟进完成", $"{QaMarker} 已完成跟进进入终态。", -12)
    ];
}
