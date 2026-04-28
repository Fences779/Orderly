using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class LocalAiAssistantService : IAiAssistantService
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalAiAssistantService(
        IConversationMessageRepository messageRepository,
        IAiSuggestionRepository suggestionRepository,
        IActivityLogRepository activityLogRepository)
    {
        _messageRepository = messageRepository;
        _suggestionRepository = suggestionRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<AiSuggestion> GenerateReplySuggestionAsync(int customerId, int? orderId = null, int? messageId = null, CancellationToken cancellationToken = default)
    {
        var messages = orderId is int resolvedOrderId
            ? await _messageRepository.ListByOrderIdAsync(resolvedOrderId, cancellationToken)
            : await _messageRepository.ListByCustomerIdAsync(customerId, cancellationToken);

        var latestMessage = messageId is int resolvedMessageId
            ? messages.FirstOrDefault(item => item.Id == resolvedMessageId)
            : messages.FirstOrDefault();

        var summary = latestMessage is null
            ? "当前没有可用的历史消息，本地 Stub 只能返回通用建议。"
            : $"最近消息摘要：{BuildSnippet(latestMessage.Content)}";

        var suggestion = new AiSuggestion
        {
            CustomerId = customerId,
            OrderId = orderId,
            MessageId = latestMessage?.Id,
            SuggestionText = latestMessage is null
                ? "【Local Stub】这是本地模拟回复建议，后续将接入真实 AI。建议先确认客户的尺寸、数量、预算和交付时间。"
                : $"【Local Stub】这是本地模拟回复建议，后续将接入真实 AI。已收到你的消息，我先帮你整理需求，稍后给你更准确的方案。{summary}",
            Reason = "本地 Stub 依据最近消息生成，未调用外部 AI。",
            Confidence = null,
            Status = AiSuggestionStatus.Draft,
            MetadataJson = JsonSerializer.Serialize(new
            {
                provider = "local-stub",
                realApi = false,
                messageId = latestMessage?.Id,
                orderId
            })
        };

        return suggestion;
    }

    public async Task<AiSuggestion> GenerateAndSaveReplySuggestionAsync(
        int customerId,
        int? orderId = null,
        int? dealId = null,
        int? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var suggestion = await GenerateReplySuggestionAsync(customerId, orderId, messageId, cancellationToken);
        var created = await _suggestionRepository.CreateAsync(suggestion, cancellationToken);
        await AddActivityAsync(
            ActivityType.AiSuggestionGenerated,
            created,
            dealId,
            "生成 AI 建议",
            "本地 Stub 已生成回复建议，未调用外部 AI，也未执行自动发送。",
            "local-stub",
            cancellationToken);
        return created;
    }

    public async Task<AiSuggestion> SaveSuggestionAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        return suggestion.Id <= 0
            ? await _suggestionRepository.CreateAsync(suggestion, cancellationToken)
            : await UpdateAsync(suggestion, cancellationToken);
    }

    public async Task<AiSuggestion> UpdateSuggestionStatusAsync(
        int suggestionId,
        AiSuggestionStatus status,
        int? dealId = null,
        CancellationToken cancellationToken = default)
    {
        if (status != AiSuggestionStatus.Accepted && status != AiSuggestionStatus.Rejected)
        {
            throw new InvalidOperationException($"Unsupported AI suggestion status transition target: {status}.");
        }

        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken)
            ?? throw new InvalidOperationException($"AI suggestion not found: {suggestionId}.");

        if (suggestion.Status == status)
        {
            return suggestion;
        }

        if (suggestion.Status != AiSuggestionStatus.Draft)
        {
            throw new InvalidOperationException("Only draft AI suggestions can be accepted or rejected.");
        }

        suggestion.Status = status;
        var updated = await UpdateAsync(suggestion, cancellationToken);

        var activityType = status == AiSuggestionStatus.Accepted
            ? ActivityType.AiSuggestionAccepted
            : ActivityType.AiSuggestionRejected;
        var title = status == AiSuggestionStatus.Accepted
            ? "接受 AI 建议"
            : "拒绝 AI 建议";
        var description = status == AiSuggestionStatus.Accepted
            ? "已接受本地 AI 建议，仅更新本地状态，未发送外部消息。"
            : "已拒绝本地 AI 建议，仅更新本地状态，未发送外部消息。";

        await AddActivityAsync(
            activityType,
            updated,
            dealId,
            title,
            description,
            "local-user",
            cancellationToken);

        return updated;
    }

    public Task<IReadOnlyList<AiSuggestion>> ListSuggestionsAsync(int customerId, int? orderId = null, CancellationToken cancellationToken = default)
    {
        return orderId is int resolvedOrderId
            ? _suggestionRepository.ListByOrderIdAsync(resolvedOrderId, cancellationToken)
            : _suggestionRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    private async Task<AiSuggestion> UpdateAsync(AiSuggestion suggestion, CancellationToken cancellationToken)
    {
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);
        return suggestion;
    }

    private static string BuildSnippet(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "暂无正文";
        }

        var normalized = content.Trim();
        return normalized.Length <= 30 ? normalized : $"{normalized[..30]}...";
    }

    private Task AddActivityAsync(
        ActivityType type,
        AiSuggestion suggestion,
        int? dealId,
        string title,
        string description,
        string @operator,
        CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = suggestion.CustomerId,
            DealId = dealId,
            OrderId = suggestion.OrderId,
            Title = title,
            Description = description,
            Operator = @operator,
            MetadataJson = JsonSerializer.Serialize(new
            {
                suggestionId = suggestion.Id,
                suggestionStatus = suggestion.Status.ToString(),
                suggestion.MessageId
            })
        }, cancellationToken);
    }
}
