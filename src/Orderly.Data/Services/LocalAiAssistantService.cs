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

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.AiSuggestionGenerated,
            CustomerId = customerId,
            OrderId = orderId,
            Title = "生成 AI 建议",
            Description = "本地 Stub 已生成回复建议，未调用外部 AI。",
            Operator = "local-stub"
        }, cancellationToken);

        return suggestion;
    }

    public async Task<AiSuggestion> SaveSuggestionAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        return suggestion.Id <= 0
            ? await _suggestionRepository.CreateAsync(suggestion, cancellationToken)
            : await UpdateAsync(suggestion, cancellationToken);
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
}
