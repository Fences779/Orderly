using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IConversationMessageRepository
{
    Task<ConversationMessage> CreateAsync(ConversationMessage message, CancellationToken cancellationToken = default);
    Task UpdateAsync(ConversationMessage message, CancellationToken cancellationToken = default);
    Task<ConversationMessage?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ConversationMessage?> GetBySourceMessageIdAsync(string sourceMessageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationMessage>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationMessage>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
}
