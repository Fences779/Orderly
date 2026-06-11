using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class ConversationService : IConversationService
{
    private const int MaxSenderNameCharacters = 80;
    private const int MaxContentCharacters = 8000;
    private const int MaxSourceMessageIdCharacters = 160;
    private const int MaxMetadataJsonCharacters = 4096;
    private const int ActivityPreviewCharacters = 36;

    private readonly IConversationMessageRepository _messageRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public ConversationService(IConversationMessageRepository messageRepository, IActivityLogRepository activityLogRepository)
    {
        _messageRepository = messageRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<ConversationMessage> SaveMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        NormalizeMessage(message);
        if (message.Id <= 0)
        {
            var created = await _messageRepository.CreateAsync(message, cancellationToken);
            await AddActivityAsync(created, cancellationToken);
            return created;
        }

        await _messageRepository.UpdateAsync(message, cancellationToken);
        return message;
    }

    public Task<IReadOnlyList<ConversationMessage>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _messageRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public Task<IReadOnlyList<ConversationMessage>> ListByOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _messageRepository.ListByOrderIdAsync(orderId, cancellationToken);
    }

    private Task AddActivityAsync(ConversationMessage message, CancellationToken cancellationToken)
    {
        var preview = BuildActivityPreview(message.Content);

        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.ConversationMessageAdded,
            CustomerId = message.CustomerId,
            DealId = message.DealId,
            OrderId = message.OrderId,
            Title = "新增会话消息",
            Description = $"{message.Direction} / {message.Channel} / {preview}",
            Operator = "local-stub"
        }, cancellationToken);
    }

    private static void NormalizeMessage(ConversationMessage message)
    {
        if (message.CustomerId <= 0)
        {
            throw new InvalidOperationException("会话消息缺少有效客户。");
        }

        if (!Enum.IsDefined(message.Direction))
        {
            throw new InvalidOperationException("会话消息方向无效。");
        }

        if (!Enum.IsDefined(message.Channel))
        {
            throw new InvalidOperationException("会话消息渠道无效。");
        }

        message.SenderName = NormalizeConversationText(message.SenderName, MaxSenderNameCharacters, "发送人", allowLineBreaks: false);
        message.Content = NormalizeConversationText(message.Content, MaxContentCharacters, "消息内容", allowLineBreaks: true);
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            throw new InvalidOperationException("消息内容不能为空。");
        }

        message.SourceMessageId = NormalizeOptionalConversationText(
            message.SourceMessageId,
            MaxSourceMessageIdCharacters,
            "消息来源标识",
            allowLineBreaks: false);
        message.MetadataJson = NormalizeOptionalConversationText(
            message.MetadataJson,
            MaxMetadataJsonCharacters,
            "消息元数据",
            allowLineBreaks: false);

        if (message.MessageTime == default)
        {
            message.MessageTime = DateTime.Now;
        }
    }

    private static string NormalizeOptionalConversationText(
        string? value,
        int maxCharacters,
        string fieldName,
        bool allowLineBreaks)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : NormalizeConversationText(value, maxCharacters, fieldName, allowLineBreaks);
    }

    private static string NormalizeConversationText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(ch => IsRejectedControlCharacter(ch, allowLineBreaks)))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static bool IsRejectedControlCharacter(char ch, bool allowLineBreaks)
    {
        return char.IsControl(ch)
            && !(allowLineBreaks && ch is '\r' or '\n' or '\t');
    }

    private static string BuildActivityPreview(string content)
    {
        var singleLine = new string(content
            .Select(static ch => ch is '\r' or '\n' or '\t' ? ' ' : ch)
            .ToArray())
            .Trim();

        return singleLine.Length <= ActivityPreviewCharacters
            ? singleLine
            : $"{singleLine[..ActivityPreviewCharacters]}...";
    }
}
