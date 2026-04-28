using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class ConversationService : IConversationService
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public ConversationService(IConversationMessageRepository messageRepository, IActivityLogRepository activityLogRepository)
    {
        _messageRepository = messageRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<ConversationMessage> SaveMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
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
        var preview = message.Content.Length <= 36
            ? message.Content
            : $"{message.Content[..36]}...";

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
}
