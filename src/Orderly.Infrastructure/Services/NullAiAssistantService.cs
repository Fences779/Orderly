using Orderly.Core.Services;

namespace Orderly.Infrastructure.Services;

public sealed class NullAiAssistantService : IAiAssistantService
{
    public Task<string> DraftReplyAsync(string context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }
}
