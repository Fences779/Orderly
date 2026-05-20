using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class SessionLockService : ISessionLockService
{
    private SessionLockState _state = SessionLockState.LoggedOut;

    public event EventHandler<SessionLockState>? LockStateChanged;

    public SessionLockState State => _state;

    public bool IsPinRequired => _state == SessionLockState.PendingPinUnlock;

    public void MarkSignedIn()
    {
        SetState(SessionLockState.Unlocked);
    }

    public void LockBySystemResume()
    {
        if (_state == SessionLockState.Unlocked)
        {
            SetState(SessionLockState.PendingPinUnlock);
        }
    }

    public void LockManually()
    {
        if (_state == SessionLockState.Unlocked)
        {
            SetState(SessionLockState.PendingPinUnlock);
        }
    }

    public void UnlockWithPin()
    {
        if (_state == SessionLockState.PendingPinUnlock)
        {
            SetState(SessionLockState.Unlocked);
        }
    }

    public void Logout()
    {
        SetState(SessionLockState.LoggedOut);
    }

    private void SetState(SessionLockState nextState)
    {
        if (_state == nextState)
        {
            return;
        }

        _state = nextState;
        LockStateChanged?.Invoke(this, _state);
    }
}
