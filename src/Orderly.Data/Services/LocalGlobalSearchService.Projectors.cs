using System.Globalization;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class LocalGlobalSearchService
{
    private static IEnumerable<SearchResultItem> ProjectCustomers(IEnumerable<Customer> customers, string query)
    {
        foreach (var customer in customers)
        {
            var match = EvaluateMatch(query,
                new SearchField("Customer.Name", customer.Name, PrimaryFieldScore),
                new SearchField("Customer.ContactHandle", customer.ContactHandle, PrimaryFieldScore),
                new SearchField("Customer.Phone", customer.Phone, PrimaryFieldScore),
                new SearchField("Customer.ExternalId", customer.ExternalId, PrimaryFieldScore),
                new SearchField("Customer.Remark", customer.Remark, ContentFieldScore),
                new SearchField("Customer.SourcePlatform", customer.SourcePlatform, ContentFieldScore),
                new SearchField("Customer.Channel", customer.Channel, ContentFieldScore),
                new SearchField("Customer.RawPayload", customer.RawPayload, MetadataFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            yield return new SearchResultItem
            {
                Id = $"customer-{customer.Id}",
                Type = SearchResultType.Customer,
                Title = customer.Name,
                Summary = BuildCustomerSummary(customer),
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                OrderId = null,
                RelatedEntityType = nameof(Customer),
                RelatedEntityId = customer.Id,
                OccurredAt = customer.UpdatedAt,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.High,
                TargetSection = ProjectionTargetSections.Customer,
                ActionHint = ProjectionActionHints.OpenCustomer
            };
        }
    }

    private static IEnumerable<SearchResultItem> ProjectOrders(IEnumerable<MerchantOrder> orders, string query)
    {
        foreach (var order in orders)
        {
            var match = EvaluateMatch(query,
                new SearchField("Order.Title", order.Title, PrimaryFieldScore),
                new SearchField("Order.ExternalId", order.ExternalId, PrimaryFieldScore),
                new SearchField("Order.CustomerName", order.Customer?.Name, PrimaryFieldScore),
                new SearchField("Order.Requirement", order.Requirement, ContentFieldScore),
                new SearchField("Order.Status", OrderStatusCatalog.GetLabel(order.Status), ContentFieldScore),
                new SearchField("Order.Amount", order.Amount.ToString("0.##", CultureInfo.InvariantCulture), ContentFieldScore),
                new SearchField("Order.SourcePlatform", order.SourcePlatform, ContentFieldScore),
                new SearchField("Order.Channel", order.Channel, ContentFieldScore),
                new SearchField("Order.RawPayload", order.RawPayload, MetadataFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            yield return new SearchResultItem
            {
                Id = $"order-{order.Id}",
                Type = SearchResultType.Order,
                Title = ProjectionTextHelper.GetTitleOrDefault(order.Title, $"订单 #{order.Id}"),
                Summary = BuildOrderSummary(order),
                CustomerId = order.CustomerId,
                CustomerName = order.Customer?.Name ?? string.Empty,
                OrderId = order.Id,
                RelatedEntityType = nameof(MerchantOrder),
                RelatedEntityId = order.Id,
                OccurredAt = order.UpdatedAt,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.High,
                TargetSection = ProjectionTargetSections.Order,
                ActionHint = ProjectionActionHints.OpenOrder
            };
        }
    }

    private static IEnumerable<SearchResultItem> ProjectConversationMessages(
        IEnumerable<ConversationMessage> messages,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        string query)
    {
        foreach (var message in messages)
        {
            var match = EvaluateMatch(query,
                new SearchField("ConversationMessage.Content", message.Content, ContentFieldScore),
                new SearchField("ConversationMessage.Channel", message.Channel.ToString(), ContentFieldScore),
                new SearchField("ConversationMessage.Direction", message.Direction.ToString(), ContentFieldScore),
                new SearchField("ConversationMessage.SenderName", message.SenderName, ContentFieldScore),
                new SearchField("ConversationMessage.MetadataJson", message.MetadataJson, MetadataFieldScore),
                new SearchField("ConversationMessage.SourceMessageId", message.SourceMessageId, MetadataFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            customerMap.TryGetValue(message.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (message.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return new SearchResultItem
            {
                Id = $"message-{message.Id}",
                Type = SearchResultType.ConversationMessage,
                Title = $"{message.Direction} / {message.Channel}",
                Summary = ProjectionTextHelper.TrimPreview(message.Content),
                CustomerId = message.CustomerId,
                CustomerName = customer?.Name ?? string.Empty,
                OrderId = message.OrderId,
                RelatedEntityType = nameof(ConversationMessage),
                RelatedEntityId = message.Id,
                OccurredAt = message.MessageTime,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.Medium,
                TargetSection = ProjectionTargetSections.Conversation,
                ActionHint = ProjectionActionHints.ReplyToCustomer
            };
        }
    }

    private static IEnumerable<SearchResultItem> ProjectSuggestions(
        IEnumerable<AiSuggestion> suggestions,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        string query)
    {
        foreach (var suggestion in suggestions)
        {
            var match = EvaluateMatch(query,
                new SearchField("AiSuggestion.SuggestionText", suggestion.SuggestionText, ContentFieldScore),
                new SearchField("AiSuggestion.Reason", suggestion.Reason, ContentFieldScore),
                new SearchField("AiSuggestion.Status", suggestion.Status.ToString(), ContentFieldScore),
                new SearchField("AiSuggestion.MetadataJson", suggestion.MetadataJson, MetadataFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            customerMap.TryGetValue(suggestion.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (suggestion.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return new SearchResultItem
            {
                Id = $"suggestion-{suggestion.Id}",
                Type = SearchResultType.AiSuggestion,
                Title = GetSuggestionTitle(suggestion),
                Summary = ProjectionTextHelper.TrimPreview(suggestion.SuggestionText),
                CustomerId = suggestion.CustomerId,
                CustomerName = customer?.Name ?? string.Empty,
                OrderId = suggestion.OrderId,
                RelatedEntityType = nameof(AiSuggestion),
                RelatedEntityId = suggestion.Id,
                OccurredAt = suggestion.UpdatedAt,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.Medium,
                TargetSection = ProjectionTargetSections.AiSuggestion,
                ActionHint = GetSuggestionActionHint(suggestion)
            };
        }
    }

    private static IEnumerable<SearchResultItem> ProjectOcrResults(
        IEnumerable<OcrResult> ocrResults,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        string query)
    {
        foreach (var ocrResult in ocrResults)
        {
            var match = EvaluateMatch(query,
                new SearchField("OcrResult.SourceName", ocrResult.SourceName, ContentFieldScore),
                new SearchField("OcrResult.ExtractedText", ocrResult.ExtractedText, ContentFieldScore),
                new SearchField("OcrResult.Status", ocrResult.Status.ToString(), ContentFieldScore),
                new SearchField("OcrResult.ErrorMessage", ocrResult.ErrorMessage, ContentFieldScore),
                new SearchField("OcrResult.MetadataJson", ocrResult.MetadataJson, MetadataFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            Customer? customer = null;
            if (ocrResult.CustomerId is int customerId)
            {
                customerMap.TryGetValue(customerId, out customer);
            }

            MerchantOrder? order = null;
            if (ocrResult.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return new SearchResultItem
            {
                Id = $"ocr-{ocrResult.Id}",
                Type = SearchResultType.OcrResult,
                Title = ProjectionTextHelper.GetTitleOrDefault(ocrResult.SourceName, $"OCR #{ocrResult.Id}"),
                Summary = string.IsNullOrWhiteSpace(ocrResult.ExtractedText)
                    ? ocrResult.Status.ToString()
                    : ProjectionTextHelper.TrimPreview(ocrResult.ExtractedText),
                CustomerId = ocrResult.CustomerId,
                CustomerName = customer?.Name ?? string.Empty,
                OrderId = ocrResult.OrderId,
                RelatedEntityType = nameof(OcrResult),
                RelatedEntityId = ocrResult.Id,
                OccurredAt = ocrResult.UpdatedAt,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.Medium,
                TargetSection = ProjectionTargetSections.Ocr,
                ActionHint = ProjectionActionHints.ConvertOcrToMessage
            };
        }
    }

    private static IEnumerable<SearchResultItem> ProjectFollowUps(
        IEnumerable<FollowUp> followUps,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        string query)
    {
        foreach (var followUp in followUps)
        {
            var match = EvaluateMatch(query,
                new SearchField("FollowUp.Title", followUp.Title, PrimaryFieldScore),
                new SearchField("FollowUp.Content", followUp.Content, ContentFieldScore),
                new SearchField("FollowUp.Status", followUp.Status.ToString(), ContentFieldScore),
                new SearchField("FollowUp.ScheduledAt", followUp.ScheduledAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ContentFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            customerMap.TryGetValue(followUp.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (followUp.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return new SearchResultItem
            {
                Id = $"followup-{followUp.Id}",
                Type = SearchResultType.FollowUp,
                Title = ProjectionTextHelper.GetTitleOrDefault(followUp.Title, customer?.Name),
                Summary = string.IsNullOrWhiteSpace(followUp.Content)
                    ? $"{followUp.Status} · {followUp.ScheduledAt:yyyy-MM-dd HH:mm}"
                    : $"{ProjectionTextHelper.TrimPreview(followUp.Content)} · {followUp.ScheduledAt:yyyy-MM-dd HH:mm}",
                CustomerId = followUp.CustomerId,
                CustomerName = customer?.Name ?? string.Empty,
                OrderId = followUp.OrderId,
                RelatedEntityType = nameof(FollowUp),
                RelatedEntityId = followUp.Id,
                OccurredAt = followUp.ScheduledAt,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.Medium,
                TargetSection = ProjectionTargetSections.FollowUp,
                ActionHint = ProjectionActionHints.CompleteFollowUp
            };
        }
    }

    private static IEnumerable<SearchResultItem> ProjectActivityLogs(
        IEnumerable<ActivityLog> activities,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        string query)
    {
        foreach (var activity in activities)
        {
            var match = EvaluateMatch(query,
                new SearchField("ActivityLog.Title", activity.Title, ContentFieldScore),
                new SearchField("ActivityLog.Description", activity.Description, ContentFieldScore),
                new SearchField("ActivityLog.Type", activity.TypeLabel, ContentFieldScore),
                new SearchField("ActivityLog.Operator", activity.Operator, ContentFieldScore),
                new SearchField("ActivityLog.MetadataJson", activity.MetadataJson, MetadataFieldScore));
            if (!match.IsMatch)
            {
                continue;
            }

            Customer? customer = null;
            if (activity.CustomerId is int customerId)
            {
                customerMap.TryGetValue(customerId, out customer);
            }

            MerchantOrder? order = null;
            if (activity.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return new SearchResultItem
            {
                Id = $"activity-{activity.Id}",
                Type = SearchResultType.ActivityLog,
                Title = ProjectionTextHelper.GetTitleOrDefault(activity.Title, activity.TypeLabel),
                Summary = ProjectionTextHelper.TrimPreview(ProjectionTextHelper.GetTitleOrDefault(activity.Description, activity.TypeLabel)),
                CustomerId = activity.CustomerId,
                CustomerName = customer?.Name ?? string.Empty,
                OrderId = activity.OrderId,
                RelatedEntityType = nameof(ActivityLog),
                RelatedEntityId = activity.Id,
                OccurredAt = activity.CreatedAt,
                MatchedField = match.FieldName,
                Score = match.Score,
                Priority = SearchResultPriority.Low,
                TargetSection = ProjectionTargetSections.ActivityLog,
                ActionHint = ResolveActivityActionHint(activity)
            };
        }
    }

    private static string BuildCustomerSummary(Customer customer)
    {
        var contact = ProjectionTextHelper.GetTitleOrDefault(customer.ContactHandle, customer.Phone);
        if (string.IsNullOrWhiteSpace(contact))
        {
            return ProjectionTextHelper.TrimPreview(customer.Remark);
        }

        if (string.IsNullOrWhiteSpace(customer.Remark))
        {
            return contact;
        }

        return $"{contact} · {ProjectionTextHelper.TrimPreview(customer.Remark)}";
    }

    private static string BuildOrderSummary(MerchantOrder order)
    {
        var amountText = order.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        var summary = $"{OrderStatusCatalog.GetLabel(order.Status)} · ¥{amountText}";
        if (string.IsNullOrWhiteSpace(order.Requirement))
        {
            return summary;
        }

        return $"{summary} · {ProjectionTextHelper.TrimPreview(order.Requirement)}";
    }

    private static string GetSuggestionTitle(AiSuggestion suggestion)
    {
        var autoReplyState = ProjectionMetadataHelper.ReadAutoReplyState(suggestion.MetadataJson);
        var isPrepared = suggestion.Status == AiSuggestionStatus.DraftPrepared
            || AutoReplyState.IsPreparedDraft(autoReplyState);

        return isPrepared
            ? "AI 草稿"
            : "AI 建议";
    }

    private static string GetSuggestionActionHint(AiSuggestion suggestion)
    {
        var autoReplyState = ProjectionMetadataHelper.ReadAutoReplyState(suggestion.MetadataJson);
        var isPrepared = suggestion.Status == AiSuggestionStatus.DraftPrepared
            || AutoReplyState.IsPreparedDraft(autoReplyState);

        return isPrepared
            ? ProjectionActionHints.ReviewDraft
            : ProjectionActionHints.ReviewSuggestion;
    }

    private static string ResolveActivityActionHint(ActivityLog activity)
    {
        return activity.Type switch
        {
            ActivityType.ConversationMessageAdded => ProjectionActionHints.ReplyToCustomer,
            ActivityType.AiSuggestionGenerated or ActivityType.AiSuggestionAccepted or ActivityType.AiSuggestionRejected => ProjectionActionHints.ReviewSuggestion,
            ActivityType.AutoReplyDraftPrepared or ActivityType.AutoReplyDraftCopied or ActivityType.AutoReplyDraftRejected or ActivityType.AutoReplySent
                => ProjectionActionHints.ReviewDraft,
            ActivityType.OcrTaskCreated or ActivityType.OcrTaskCompleted or ActivityType.OcrTaskFailed => ProjectionActionHints.ConvertOcrToMessage,
            ActivityType.FollowUpCreated or ActivityType.FollowUpCompleted or ActivityType.FollowUpSnoozed or ActivityType.FollowUpCancelled
                => ProjectionActionHints.CompleteFollowUp,
            ActivityType.OrderCreated or ActivityType.OrderStatusChanged or ActivityType.PriceAdjustmentRequested or ActivityType.PriceAdjustmentApproved or ActivityType.PriceAdjustmentRejected
                => ProjectionActionHints.OpenOrder,
            _ => ProjectionActionHints.OpenCustomer
        };
    }
}
