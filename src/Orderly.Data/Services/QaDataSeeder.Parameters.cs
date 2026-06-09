using Microsoft.Data.Sqlite;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class QaDataSeeder
{
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
        command.Parameters.AddWithValue("$metadataJson", QaDataScope.BuildSeedActivityMetadataJson(activity.RemoteId));
        command.Parameters.AddWithValue("$createdAt", now.AddHours(activity.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", activity.RemoteId);
    }

    private static void AddConversationMessageParameters(SqliteCommand command, QaConversationMessage message, int customerId, int? orderId, int? dealId, DateTime now)
    {
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$dealId", ToDbInt(dealId));
        command.Parameters.AddWithValue("$direction", (int)message.Direction);
        command.Parameters.AddWithValue("$channel", (int)message.Channel);
        command.Parameters.AddWithValue("$senderName", message.SenderName);
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$messageTime", now.AddHours(message.MessageOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$sourceMessageId", message.SourceMessageId);
        command.Parameters.AddWithValue("$metadataJson", BuildQaMetadata(message.RemoteId));
        command.Parameters.AddWithValue("$createdAt", now.AddHours(message.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", message.RemoteId);
    }

    private static void AddAiSuggestionParameters(SqliteCommand command, QaAiSuggestion suggestion, int customerId, int? orderId, int? messageId, DateTime now)
    {
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$messageId", ToDbInt(messageId));
        command.Parameters.AddWithValue("$suggestionText", suggestion.SuggestionText);
        command.Parameters.AddWithValue("$reason", suggestion.Reason);
        command.Parameters.AddWithValue("$confidence", DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)suggestion.Status);
        command.Parameters.AddWithValue("$metadataJson", BuildQaMetadata(suggestion.RemoteId));
        command.Parameters.AddWithValue("$createdAt", now.AddHours(suggestion.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", suggestion.RemoteId);
    }

    private static void AddOcrResultParameters(SqliteCommand command, QaOcrResult ocrResult, int? customerId, int? orderId, DateTime now)
    {
        command.Parameters.AddWithValue("$customerId", ToDbInt(customerId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(orderId));
        command.Parameters.AddWithValue("$sourcePath", ocrResult.SourcePath);
        command.Parameters.AddWithValue("$sourceName", ocrResult.SourceName);
        command.Parameters.AddWithValue("$extractedText", ocrResult.ExtractedText);
        command.Parameters.AddWithValue("$status", (int)ocrResult.Status);
        command.Parameters.AddWithValue("$errorMessage", ocrResult.ErrorMessage);
        command.Parameters.AddWithValue("$metadataJson", BuildQaMetadata(ocrResult.RemoteId));
        command.Parameters.AddWithValue("$createdAt", now.AddHours(ocrResult.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$remoteId", ocrResult.RemoteId);
    }

    private static void AddSyncRecordParameters(SqliteCommand command, QaSyncRecord syncRecord, int entityId, DateTime now)
    {
        command.Parameters.AddWithValue("$entityType", syncRecord.EntityType);
        command.Parameters.AddWithValue("$entityId", entityId);
        command.Parameters.AddWithValue("$remoteId", syncRecord.RemoteId);
        command.Parameters.AddWithValue("$syncStatus", (int)syncRecord.Status);
        command.Parameters.AddWithValue("$lastSyncedAt", syncRecord.Status == SyncStatus.Synced ? now.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", syncRecord.ErrorMessage);
        command.Parameters.AddWithValue("$metadataJson", BuildQaMetadata(syncRecord.RemoteId));
        command.Parameters.AddWithValue("$createdAt", now.AddHours(syncRecord.CreatedOffsetHours).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$isSynced", syncRecord.Status == SyncStatus.Synced ? 1 : 0);
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

    private static string BuildQaMetadata(string key)
    {
        return QaDataScope.EnsureActivityMetadataTagged(string.Empty, "seed", key);
    }
}
