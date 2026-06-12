using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class SessionLockService : ISessionLockService
{
    private readonly ISessionContextService _sessionContextService;
    private SessionLockState _state = SessionLockState.LoggedOut;

    public SessionLockService(ISessionContextService sessionContextService)
    {
        _sessionContextService = sessionContextService;
    }

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
            _sessionContextService.SuspendDataKey();
            SetState(SessionLockState.PendingPinUnlock);
        }
    }

    public void LockManually()
    {
        if (_state == SessionLockState.Unlocked)
        {
            _sessionContextService.SuspendDataKey();
            SetState(SessionLockState.PendingPinUnlock);
        }
    }

    public void UnlockWithPin(bool verified)
    {
        if (!verified)
        {
            return;
        }

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
