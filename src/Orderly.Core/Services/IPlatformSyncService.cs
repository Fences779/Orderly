namespace Orderly.Core.Services;

public interface IPlatformSyncService
{
    Task SyncAsync(CancellationToken cancellationToken = default);
}
