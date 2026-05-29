using Microsoft.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class QaDataSeeder
{
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
            $"""
            SELECT Id
            FROM Customers
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR ExternalId = $externalId
                 OR (Name = $name AND {QaDataScope.BuildCustomerSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$name", customer.Name);
                command.Parameters.AddWithValue("$remoteId", customer.RemoteId);
                command.Parameters.AddWithValue("$externalId", customer.ExternalId);
                QaDataScope.AddScopeParameters(command);
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
            $"""
            SELECT Id
            FROM Deals
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (Title = $title AND {QaDataScope.BuildDealSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", deal.Title);
                command.Parameters.AddWithValue("$remoteId", deal.RemoteId);
                QaDataScope.AddScopeParameters(command);
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
            $"""
            SELECT Id
            FROM Orders
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR ExternalId = $externalId
                 OR (Title = $title AND {QaDataScope.BuildOrderSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", order.Title);
                command.Parameters.AddWithValue("$remoteId", order.RemoteId);
                command.Parameters.AddWithValue("$externalId", order.ExternalId);
                QaDataScope.AddScopeParameters(command);
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
            $"""
            SELECT Id
            FROM FollowUps
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (Title = $title AND {QaDataScope.BuildFollowUpSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", followUp.Title);
                command.Parameters.AddWithValue("$remoteId", followUp.RemoteId);
                QaDataScope.AddScopeParameters(command);
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
            $"""
            SELECT Id
            FROM CustomerNotes
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (Content = $content AND {QaDataScope.BuildNoteSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$content", note.Content);
                command.Parameters.AddWithValue("$remoteId", note.RemoteId);
                QaDataScope.AddScopeParameters(command);
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
}
