using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IConversationService
{
    Task<ConversationMessage> SaveMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationMessage>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationMessage>> ListByOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
