using Orderly.Core.Models;

namespace Orderly.App.Session;

/// <summary>
/// 决定 PendingPinUnlock 进入后，是立即弹出 PIN，还是延后到用户下次重新打开主窗口时再弹。
/// 仅负责提示时机，不改动既有锁定状态机和 PIN 校验链路。
/// </summary>
public static class PinUnlockPromptPolicy
{
    public static PinUnlockPromptAction EvaluateOnLock(SessionLockState state, bool deferUntilMainWindowOpen)
    {
        if (state != SessionLockState.PendingPinUnlock)
        {
            return PinUnlockPromptAction.None;
        }

        return deferUntilMainWindowOpen
            ? PinUnlockPromptAction.DeferUntilMainWindowOpen
            : PinUnlockPromptAction.PromptImmediately;
    }

    public static bool ShouldRequirePinBeforeShowingMainWindow(
        SessionLockState state,
        bool deferUntilMainWindowOpen)
        => state == SessionLockState.PendingPinUnlock && deferUntilMainWindowOpen;
}

public enum PinUnlockPromptAction
{
    None = 0,
    PromptImmediately = 1,
    DeferUntilMainWindowOpen = 2,
}
