using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed class LocalAiAssistantService : IAiAssistantService
{
    private const int ContextMessageLimit = 5;

    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IAiSuggestionProvider _primaryProvider;
    private readonly IAiSuggestionProvider _fallbackProvider;
    private readonly AiProviderOptions _providerOptions;

    public LocalAiAssistantService(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        IConversationMessageRepository messageRepository,
        IAiSuggestionRepository suggestionRepository,
        IActivityLogRepository activityLogRepository,
        IAiSuggestionProvider primaryProvider,
        IAiSuggestionProvider fallbackProvider,
        AiProviderOptions providerOptions)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _messageRepository = messageRepository;
        _suggestionRepository = suggestionRepository;
        _activityLogRepository = activityLogRepository;
        _primaryProvider = primaryProvider;
        _fallbackProvider = fallbackProvider;
        _providerOptions = providerOptions;
    }

    public async Task<AiSuggestion> GenerateReplySuggestionAsync(int customerId, int? orderId = null, int? messageId = null, CancellationToken cancellationToken = default)
    {
        var request = await BuildRequestAsync(customerId, orderId, messageId, cancellationToken);
        var execution = await ExecuteProviderAsync(request, cancellationToken);

        var suggestion = new AiSuggestion
        {
            CustomerId = customerId,
            OrderId = orderId,
            MessageId = request.MessageId,
            SuggestionText = execution.Result.SuggestionText,
            Reason = BuildSuggestionReason(execution),
            Confidence = null,
            Status = AiSuggestionStatus.Draft,
            MetadataJson = BuildSuggestionMetadata(request, execution)
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
            BuildGeneratedDescription(created.MetadataJson),
            ReadProviderName(created.MetadataJson),
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
            ? "已接受 AI 建议，仅更新本地状态，未发送外部消息。"
            : "已拒绝 AI 建议，仅更新本地状态，未发送外部消息。";

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

    private async Task<AiSuggestionRequest> BuildRequestAsync(int customerId, int? orderId, int? messageId, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
        var order = orderId is int resolvedOrderId
            ? await _orderRepository.GetByIdAsync(resolvedOrderId, cancellationToken)
            : null;
        var messages = orderId is int scopedOrderId
            ? await _messageRepository.ListByOrderIdAsync(scopedOrderId, cancellationToken)
            : await _messageRepository.ListByCustomerIdAsync(customerId, cancellationToken);

        var focusMessage = ResolveFocusMessage(messages, messageId);
        var contextMessages = ResolveContextMessages(messages, focusMessage);

        return new AiSuggestionRequest
        {
            CustomerId = customerId,
            OrderId = orderId,
            MessageId = focusMessage?.Id,
            CustomerName = TrimAndLimit(customer?.Name, 40),
            CustomerNickname = TrimAndLimit(customer?.ContactHandle, 40),
            CustomerRemark = TrimAndLimit(customer?.Remark, 120),
            OrderTitle = TrimAndLimit(order?.Title, 60),
            OrderBudgetText = BuildBudgetText(order),
            OrderStatusText = BuildOrderStatusText(order?.Status),
            OrderRemark = TrimAndLimit(order?.Requirement, 120),
            FocusMessage = TrimAndLimit(focusMessage?.Content, 160),
            RecentMessages = contextMessages
        };
    }

    private async Task<ProviderExecution> ExecuteProviderAsync(AiSuggestionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var primaryResult = await _primaryProvider.GenerateAsync(request, cancellationToken);
            EnsureSuggestionText(primaryResult);
            return new ProviderExecution(primaryResult, false, null);
        }
        catch (Exception ex) when (ShouldFallback())
        {
            var fallbackResult = await _fallbackProvider.GenerateAsync(request, cancellationToken);
            EnsureSuggestionText(fallbackResult);
            return new ProviderExecution(fallbackResult, true, SummarizeProviderError(ex));
        }
    }

    private async Task<AiSuggestion> UpdateAsync(AiSuggestion suggestion, CancellationToken cancellationToken)
    {
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);
        return suggestion;
    }

    private static ConversationMessage? ResolveFocusMessage(IReadOnlyList<ConversationMessage> messages, int? messageId)
    {
        if (messageId is int resolvedMessageId)
        {
            var matched = messages.FirstOrDefault(item => item.Id == resolvedMessageId);
            if (matched is not null)
            {
                return matched;
            }
        }

        return messages.FirstOrDefault(item => item.Direction == MessageDirection.Incoming)
            ?? messages.FirstOrDefault();
    }

    private static IReadOnlyList<AiSuggestionContextMessage> ResolveContextMessages(
        IReadOnlyList<ConversationMessage> messages,
        ConversationMessage? focusMessage)
    {
        var selected = messages
            .Take(ContextMessageLimit)
            .ToList();

        if (focusMessage is not null && selected.All(item => item.Id != focusMessage.Id))
        {
            selected = selected
                .Take(ContextMessageLimit - 1)
                .Append(focusMessage)
                .ToList();
        }

        return selected
            .OrderBy(item => item.MessageTime)
            .ThenBy(item => item.Id)
            .Select(item => new AiSuggestionContextMessage
            {
                RoleLabel = BuildRoleLabel(item.Direction),
                SenderName = TrimAndLimit(item.SenderName, 24),
                Content = TrimAndLimit(item.Content, 120),
                MessageTime = item.MessageTime
            })
            .ToList();
    }

    private static string BuildRoleLabel(MessageDirection direction)
    {
        return direction switch
        {
            MessageDirection.Incoming => "客户",
            MessageDirection.Outgoing => "我",
            _ => "系统"
        };
    }

    private static string BuildBudgetText(MerchantOrder? order)
    {
        if (order is null)
        {
            return string.Empty;
        }

        return order.Amount <= 0 ? "待确认" : $"¥{order.Amount:N0}";
    }

    private static string BuildOrderStatusText(OrderStatus? status)
    {
        return status switch
        {
            OrderStatus.PendingCommunication => "待沟通",
            OrderStatus.PendingQuote => "待报价",
            OrderStatus.Quoted => "已报价",
            OrderStatus.PendingFollowUp => "待跟进",
            OrderStatus.Won => "已成交",
            OrderStatus.Closed => "已关闭",
            _ => string.Empty
        };
    }

    private static string TrimAndLimit(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length <= limit ? normalized : $"{normalized[..limit]}...";
    }

    private static void EnsureSuggestionText(AiSuggestionProviderResult result)
    {
        if (string.IsNullOrWhiteSpace(result.SuggestionText))
        {
            throw new InvalidOperationException("AI suggestion provider returned empty suggestion text.");
        }
    }

    private bool ShouldFallback()
    {
        return !string.Equals(_primaryProvider.Name, _fallbackProvider.Name, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildSuggestionMetadata(AiSuggestionRequest request, ProviderExecution execution)
    {
        var root = new JsonObject
        {
            ["provider"] = execution.Result.Provider,
            ["model"] = execution.Result.Model,
            ["usedFallback"] = execution.UsedFallback,
            ["createdBy"] = "p2.4",
            ["contextMessageCount"] = request.RecentMessages.Count,
            ["timeoutSeconds"] = _providerOptions.TimeoutSeconds,
            ["requestedProvider"] = _providerOptions.RequestedProvider
        };

        if (!string.IsNullOrWhiteSpace(execution.ErrorSummary))
        {
            root["errorSummary"] = execution.ErrorSummary;
        }

        if (request.OrderId is int resolvedOrderId)
        {
            root["orderId"] = resolvedOrderId;
        }

        if (request.MessageId is int resolvedMessageId)
        {
            root["messageId"] = resolvedMessageId;
        }

        var providerMetadata = ParseMetadata(execution.Result.MetadataJson);
        if (providerMetadata is not null)
        {
            root["providerResult"] = providerMetadata;
        }

        return root.ToJsonString();
    }

    private static string BuildSuggestionReason(ProviderExecution execution)
    {
        if (execution.UsedFallback)
        {
            return "远程 Provider 不可用，已自动回退到 Local Stub，程序继续保持离线可运行。";
        }

        return string.Equals(execution.Result.Provider, "local-stub", StringComparison.OrdinalIgnoreCase)
            ? "本地 Stub 依据最近沟通上下文生成，未调用外部 AI。"
            : $"已通过 {GetProviderDisplayName(execution.Result.Provider)} 生成回复建议；当前只生成建议，不自动发送。";
    }

    private static string BuildGeneratedDescription(string metadataJson)
    {
        if (ReadUsedFallback(metadataJson))
        {
            return "远程 AI Provider 不可用，已自动回退到 Local Stub 生成建议，未执行自动发送。";
        }

        var provider = ReadProviderName(metadataJson);
        return string.Equals(provider, "local-stub", StringComparison.OrdinalIgnoreCase)
            ? "本地 Stub 已生成回复建议，未调用外部 AI，也未执行自动发送。"
            : $"已通过 {GetProviderDisplayName(provider)} 生成回复建议，未执行自动发送。";
    }

    private static string ReadProviderName(string metadataJson)
    {
        var root = ParseMetadata(metadataJson);
        var provider = root?["provider"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(provider) ? "local-stub" : provider;
    }

    private static bool ReadUsedFallback(string metadataJson)
    {
        var root = ParseMetadata(metadataJson);
        return root?["usedFallback"]?.GetValue<bool>() ?? false;
    }

    private static string? SummarizeProviderError(Exception exception)
    {
        var message = exception.Message.Trim();
        if (message.Length <= 160)
        {
            return message;
        }

        return $"{message[..160]}...";
    }

    private static JsonObject? ParseMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
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
        var suggestionMetadata = ParseMetadata(suggestion.MetadataJson);

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
                suggestion.MessageId,
                provider = suggestionMetadata?["provider"]?.GetValue<string>(),
                usedFallback = suggestionMetadata?["usedFallback"]?.GetValue<bool>()
            })
        }, cancellationToken);
    }

    private static string GetProviderDisplayName(string provider)
    {
        return provider switch
        {
            "openai-compatible" => "OpenAI-compatible Provider",
            "deepseek" => "DeepSeek Provider",
            "local-stub" => "Local Stub",
            _ => provider
        };
    }

    private sealed record ProviderExecution(
        AiSuggestionProviderResult Result,
        bool UsedFallback,
        string? ErrorSummary);
}
