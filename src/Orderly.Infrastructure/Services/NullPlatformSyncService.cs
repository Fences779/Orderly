using Orderly.Core.Services;

namespace Orderly.Infrastructure.Services;

public sealed class NullPlatformSyncService : IPlatformSyncService
{
    public Task SyncAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
