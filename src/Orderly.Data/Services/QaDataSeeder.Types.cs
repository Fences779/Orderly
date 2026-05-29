using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class QaDataSeeder
{
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
        public int ConversationMessagesInserted { get; internal set; }
        public int ConversationMessagesUpdated { get; internal set; }
        public int AiSuggestionsInserted { get; internal set; }
        public int AiSuggestionsUpdated { get; internal set; }
        public int OcrResultsInserted { get; internal set; }
        public int OcrResultsUpdated { get; internal set; }
        public int SyncRecordsInserted { get; internal set; }
        public int SyncRecordsUpdated { get; internal set; }

        public override string ToString()
        {
            return $"customers +{CustomersInserted}/~{CustomersUpdated}, deals +{DealsInserted}/~{DealsUpdated}, orders +{OrdersInserted}/~{OrdersUpdated}, followUps +{FollowUpsInserted}/~{FollowUpsUpdated}, notes +{NotesInserted}/~{NotesUpdated}, priceAdjustments +{PriceAdjustmentsInserted}/~{PriceAdjustmentsUpdated}, activityLogs +{ActivityLogsInserted}/~{ActivityLogsUpdated}, conversationMessages +{ConversationMessagesInserted}/~{ConversationMessagesUpdated}, aiSuggestions +{AiSuggestionsInserted}/~{AiSuggestionsUpdated}, ocrResults +{OcrResultsInserted}/~{OcrResultsUpdated}, syncRecords +{SyncRecordsInserted}/~{SyncRecordsUpdated}";
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

    private sealed record QaConversationMessage(
        string RemoteId,
        string SourceMessageId,
        string CustomerName,
        string? OrderTitle,
        string? DealTitle,
        MessageDirection Direction,
        MessageChannel Channel,
        string SenderName,
        string Content,
        int CreatedOffsetHours,
        int MessageOffsetHours);

    private sealed record QaAiSuggestion(
        string RemoteId,
        string CustomerName,
        string? OrderTitle,
        string MessageRemoteId,
        string SuggestionText,
        string Reason,
        AiSuggestionStatus Status,
        int CreatedOffsetHours);

    private sealed record QaOcrResult(
        string RemoteId,
        string? CustomerName,
        string? OrderTitle,
        string SourcePath,
        string SourceName,
        string ExtractedText,
        OcrStatus Status,
        string ErrorMessage,
        int CreatedOffsetHours);

    private sealed record QaSyncRecord(
        string RemoteId,
        string EntityType,
        string EntityRemoteId,
        SyncStatus Status,
        string ErrorMessage,
        int CreatedOffsetHours);
}
