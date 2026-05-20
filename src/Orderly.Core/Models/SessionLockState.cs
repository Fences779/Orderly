namespace Orderly.Core.Models;

public enum SessionLockState
{
    Unlocked = 0,
    PendingPinUnlock = 1,
    LoggedOut = 2
}
