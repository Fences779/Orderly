using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed class LocalAutoReplyService : IAutoReplyService
{
    private const int MaxMetadataJsonCharacters = 8192;
    private const int MaxLegacyMetadataCharacters = 1024;

    private static readonly JsonDocumentOptions MetadataJsonDocumentOptions = new()
    {
        MaxDepth = 16
    };

    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IClipboardService _clipboardService;

    public LocalAutoReplyService(
        IAiSuggestionRepository suggestionRepository,
        IOrderRepository orderRepository,
        IActivityLogRepository activityLogRepository,
        IClipboardService clipboardService)
    {
        _suggestionRepository = suggestionRepository;
        _orderRepository = orderRepository;
        _activityLogRepository = activityLogRepository;
        _clipboardService = clipboardService;
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
        suggestion.SuggestionText = AutoReplyDraftText.EnsurePrefix(suggestion.SuggestionText);
        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, AutoReplyState.Prepared);
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
            MetadataJson = BuildActivityMetadata(suggestion, AutoReplyState.Prepared)
        }, cancellationToken);

        return suggestion;
    }

    public async Task CopyReplyDraftAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null)
        {
            return;
        }

        if (suggestion.Status != AiSuggestionStatus.DraftPrepared)
        {
            throw new InvalidOperationException("Only prepared local reply drafts can be copied.");
        }

        _clipboardService.SetText(AutoReplyDraftText.StripPrefix(suggestion.SuggestionText));

        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, AutoReplyState.Copied);
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.AutoReplyDraftCopied,
            CustomerId = suggestion.CustomerId,
            DealId = await ResolveDealIdAsync(suggestion, cancellationToken),
            OrderId = suggestion.OrderId,
            Title = "复制回复草稿",
            Description = "该回复草稿已复制到系统剪贴板，请手动粘贴到微信/闲鱼等目标平台发送；本软件不会自动发送。",
            Operator = "local-user",
            MetadataJson = BuildActivityMetadata(suggestion, AutoReplyState.Copied)
        }, cancellationToken);
    }

    public async Task MarkReplySentAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null || suggestion.Status == AiSuggestionStatus.Sent)
        {
            return;
        }

        if (suggestion.Status != AiSuggestionStatus.DraftPrepared || !IsCopiedDraft(suggestion))
        {
            throw new InvalidOperationException("Only copied local reply drafts can be marked as sent.");
        }

        suggestion.Status = AiSuggestionStatus.Sent;
        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, AutoReplyState.Sent);
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
            MetadataJson = BuildActivityMetadata(suggestion, AutoReplyState.Sent)
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
        suggestion.MetadataJson = UpdateAutoReplyMetadata(suggestion.MetadataJson, AutoReplyState.Rejected);
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
            MetadataJson = BuildActivityMetadata(suggestion, AutoReplyState.Rejected)
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

    private static bool IsCopiedDraft(AiSuggestion suggestion)
    {
        return AutoReplyState.IsCopied(AutoReplyMetadataHelper.ReadState(suggestion.MetadataJson));
    }

    private static bool HasAutoReplyMetadata(string metadataJson)
    {
        var root = ParseMetadata(metadataJson);
        return root["autoReply"] is JsonObject;
    }

    private static string UpdateAutoReplyMetadata(string metadataJson, string state)
    {
        var now = DateTime.Now.ToString("O");
        var root = ParseMetadata(metadataJson);
        var autoReply = root["autoReply"] as JsonObject ?? new JsonObject();
        autoReply["mode"] = "local-draft";
        autoReply["state"] = state;
        autoReply["localOnly"] = true;
        autoReply["externalSendExecuted"] = false;
        autoReply["updatedAt"] = now;

        if (AutoReplyState.IsPrepared(state) && autoReply["preparedAt"] is null)
        {
            autoReply["preparedAt"] = now;
        }

        if (AutoReplyState.IsCopied(state) || AutoReplyState.IsSent(state))
        {
            autoReply["deliveryMode"] = "manual-copy";
            if (autoReply["externalPlatform"] is null)
            {
                autoReply["externalPlatform"] = "manual";
            }
        }

        if (AutoReplyState.IsCopied(state))
        {
            autoReply["copiedAt"] = now;
            autoReply["copiedBy"] = "p2.6";
        }

        if (AutoReplyState.IsSent(state))
        {
            autoReply["sentAt"] = now;
            autoReply["sentBy"] = "manual-confirm";
        }

        root["autoReply"] = autoReply;
        return root.ToJsonString();
    }

    private static JsonObject ParseMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || metadataJson.Length > MaxMetadataJsonCharacters)
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson, documentOptions: MetadataJsonDocumentOptions) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["legacyMetadata"] = metadataJson.Length <= MaxLegacyMetadataCharacters
                    ? metadataJson
                    : metadataJson[..MaxLegacyMetadataCharacters]
            };
        }
    }

    private static string BuildActivityMetadata(AiSuggestion suggestion, string state)
    {
        var autoReply = ParseMetadata(suggestion.MetadataJson)["autoReply"] as JsonObject;
        var metadata = new JsonObject
        {
            ["suggestionId"] = suggestion.Id,
            ["suggestionStatus"] = suggestion.Status.ToString(),
            ["messageId"] = suggestion.MessageId,
            ["autoReplyState"] = state,
            ["localOnly"] = true,
            ["externalSendExecuted"] = false
        };

        CopyJsonProperty(autoReply, metadata, "deliveryMode");
        CopyJsonProperty(autoReply, metadata, "externalPlatform");
        CopyJsonProperty(autoReply, metadata, "copiedAt");
        CopyJsonProperty(autoReply, metadata, "copiedBy");
        CopyJsonProperty(autoReply, metadata, "sentAt");
        CopyJsonProperty(autoReply, metadata, "sentBy");

        return metadata.ToJsonString();
    }

    private static void CopyJsonProperty(JsonObject? source, JsonObject target, string propertyName)
    {
        if (source?[propertyName] is JsonNode value)
        {
            target[propertyName] = value.DeepClone();
        }
    }
}
