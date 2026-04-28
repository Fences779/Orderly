using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IAutoReplyService
{
    Task<AiSuggestion?> PrepareReplyAsync(int suggestionId, CancellationToken cancellationToken = default);
    Task MarkReplySentAsync(int suggestionId, CancellationToken cancellationToken = default);
    Task MarkReplyRejectedAsync(int suggestionId, CancellationToken cancellationToken = default);
}
