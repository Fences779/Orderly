using Orderly.Core.Services;

namespace Orderly.Infrastructure.Services;

public sealed class NullAutoReplyService : IAutoReplyService
{
    public Task QueueReplyAsync(string platform, string contactHandle, string content, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
