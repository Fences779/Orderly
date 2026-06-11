using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ISessionLockService
{
    event EventHandler<SessionLockState>? LockStateChanged;

    SessionLockState State { get; }
    bool IsPinRequired { get; }

    void MarkSignedIn();
    void LockBySystemResume();
    void LockManually();
    void UnlockWithPin(bool verified);
    void Logout();
}
