using Microsoft.Data.Sqlite;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class DemoDataSeeder
{
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
}
