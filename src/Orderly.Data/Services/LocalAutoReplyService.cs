using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed class LocalAutoReplyService : IAutoReplyService
{
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalAutoReplyService(
        IAiSuggestionRepository suggestionRepository,
        IOrderRepository orderRepository,
        IActivityLogRepository activityLogRepository)
    {
        _suggestionRepository = suggestionRepository;
        _orderRepository = orderRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<AiSuggestion?> PrepareReplyAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null)
        {
            return null;
        }

        if (suggestion.Status == AiSuggestionStatus.Sent || IsRejectedDraft(suggestion))
        {
            throw new InvalidOperationException("Only active AI suggestions can be prepared as a local reply draft.");
        }

        if (suggestion.Status == AiSuggestionStatus.DraftPrepared)
        {
            return suggestion;
        }

        suggestion.Status = AiSuggestionStatus.DraftPrepared;
        suggestion.SuggestionText = EnsureDraftPrefix(suggestion.SuggestionText);
        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, "prepared");
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.AutoReplyDraftPrepared,
            CustomerId = suggestion.CustomerId,
            DealId = await ResolveDealIdAsync(suggestion, cancellationToken),
            OrderId = suggestion.OrderId,
            Title = "准备回复草稿",
            Description = "已基于 AI 建议准备本地回复草稿，仅本地保存，未发送任何外部消息。",
            Operator = "local-user",
            MetadataJson = BuildActivityMetadata(suggestion, "prepared")
        }, cancellationToken);

        return suggestion;
    }

    public async Task MarkReplySentAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null || suggestion.Status == AiSuggestionStatus.Sent)
        {
            return;
        }

        if (suggestion.Status != AiSuggestionStatus.DraftPrepared)
        {
            throw new InvalidOperationException("Only prepared local reply drafts can be marked as sent.");
        }

        suggestion.Status = AiSuggestionStatus.Sent;
        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, "sent");
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.AutoReplySent,
            CustomerId = suggestion.CustomerId,
            DealId = await ResolveDealIdAsync(suggestion, cancellationToken),
            OrderId = suggestion.OrderId,
            Title = "标记回复已发送",
            Description = "该回复草稿已在本地标记为已发送，仅更新本地状态，未执行外部平台发送。",
            Operator = "local-user",
            MetadataJson = BuildActivityMetadata(suggestion, "sent")
        }, cancellationToken);
    }

    public async Task MarkReplyRejectedAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null || suggestion.Status == AiSuggestionStatus.Rejected)
        {
            return;
        }

        if (suggestion.Status != AiSuggestionStatus.DraftPrepared)
        {
            throw new InvalidOperationException("Only prepared local reply drafts can be rejected.");
        }

        suggestion.Status = AiSuggestionStatus.Rejected;
        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, "rejected");
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.AutoReplyDraftRejected,
            CustomerId = suggestion.CustomerId,
            DealId = await ResolveDealIdAsync(suggestion, cancellationToken),
            OrderId = suggestion.OrderId,
            Title = "拒绝回复草稿",
            Description = "该回复草稿已被本地拒绝，仅更新本地状态，保留记录，不删除数据。",
            Operator = "local-user",
            MetadataJson = BuildActivityMetadata(suggestion, "rejected")
        }, cancellationToken);
    }

    private async Task<int?> ResolveDealIdAsync(AiSuggestion suggestion, CancellationToken cancellationToken)
    {
        if (suggestion.OrderId is not int orderId)
        {
            return null;
        }

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        return order?.DealId;
    }

    private static bool IsRejectedDraft(AiSuggestion suggestion)
    {
        return suggestion.Status == AiSuggestionStatus.Rejected && HasAutoReplyMetadata(suggestion.MetadataJson);
    }

    private static bool HasAutoReplyMetadata(string metadataJson)
    {
        var root = ParseMetadata(metadataJson);
        return root["autoReply"] is JsonObject;
    }

    private static string EnsureDraftPrefix(string suggestionText)
    {
        const string prefix = "【本地草稿 / 未发送】";
        if (string.IsNullOrWhiteSpace(suggestionText))
        {
            return prefix;
        }

        return suggestionText.StartsWith(prefix, StringComparison.Ordinal)
            ? suggestionText
            : $"{prefix}{suggestionText}";
    }

    private static string UpdateAutoReplyMetadata(string metadataJson, string state)
    {
        var root = ParseMetadata(metadataJson);
        var autoReply = root["autoReply"] as JsonObject ?? new JsonObject();
        autoReply["mode"] = "local-draft";
        autoReply["state"] = state;
        autoReply["localOnly"] = true;
        autoReply["externalSendExecuted"] = false;
        autoReply["updatedAt"] = DateTime.Now.ToString("O");
        root["autoReply"] = autoReply;
        return root.ToJsonString();
    }

    private static JsonObject ParseMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["legacyMetadata"] = metadataJson
            };
        }
    }

    private static string BuildActivityMetadata(AiSuggestion suggestion, string state)
    {
        return JsonSerializer.Serialize(new
        {
            suggestionId = suggestion.Id,
            suggestionStatus = suggestion.Status.ToString(),
            suggestion.MessageId,
            autoReplyState = state,
            localOnly = true,
            externalSendExecuted = false
        });
    }
}
