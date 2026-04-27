using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IReplyTemplateRepository
{
    Task<IReadOnlyList<ReplyTemplate>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReplyTemplate>> GetFavoritesAsync(CancellationToken cancellationToken = default);
}
