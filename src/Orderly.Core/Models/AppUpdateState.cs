namespace Orderly.Core.Models;

public enum AppUpdateState
{
    Unsupported = 0,
    UpToDate = 1,
    UpdateAvailable = 2,
    PendingRestart = 3,
    Failed = 4
}
