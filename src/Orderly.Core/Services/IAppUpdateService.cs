using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IAppUpdateService
{
    AppUpdateSupportInfo GetSupportInfo();
    Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<AppUpdateDownloadResult> DownloadPendingUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default);
    void ApplyPendingUpdateAndRestart(string[]? restartArgs = null);
}
