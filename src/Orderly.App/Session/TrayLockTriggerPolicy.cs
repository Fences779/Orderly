using System;
using Orderly.Core.Models;

namespace Orderly.App.Session;

/// <summary>
/// 应用级会话锁定触发点的纯决策逻辑（任务 9.8，需求 18.1/18.2/13.3）。
///
/// 以「应用级会话锁定」作为 PIN 保护主模型：当主窗口最小化到托盘时，依据可选的
/// 「空闲时限」策略决定立即锁定还是延时锁定。决策不读取任何 UI 控件状态，
/// 仅依赖（是否已登录、当前锁定状态、空闲时限）三项输入，便于单元测试（任务 9.9）。
///
/// 锁定动作本身复用既有 <see cref="ISessionLockService.LockManually"/> 进入
/// <see cref="SessionLockState.PendingPinUnlock"/>，本类型不改动既有解锁交互。
/// </summary>
public static class TrayLockTriggerPolicy
{
    /// <summary>
    /// 评估「最小化到托盘」时应采取的锁定动作。
    /// </summary>
    /// <param name="isSignedIn">当前是否存在已登录会话。</param>
    /// <param name="currentState">既有 <see cref="ISessionLockService"/> 的当前锁定状态。</param>
    /// <param name="idleLockDelay">
    /// 「最小化后经过空闲时限再锁定」的可选时限；<see cref="TimeSpan.Zero"/> 或负值表示默认立即锁定。
    /// </param>
    public static TrayLockAction Evaluate(bool isSignedIn, SessionLockState currentState, TimeSpan idleLockDelay)
    {
        // 仅在已登录且当前处于已解锁状态时才有锁定意义；已锁定/已登出无需重复触发。
        if (!isSignedIn || currentState != SessionLockState.Unlocked)
        {
            return TrayLockAction.None;
        }

        // 默认（时限 <= 0）立即锁定；否则按空闲时限延时锁定。
        return idleLockDelay <= TimeSpan.Zero
            ? TrayLockAction.LockImmediately
            : TrayLockAction.LockAfterIdleDelay;
    }
}

/// <summary>
/// <see cref="TrayLockTriggerPolicy.Evaluate"/> 的决策结果。
/// </summary>
public enum TrayLockAction
{
    /// <summary>无需锁定（未登录或已处于锁定/登出状态）。</summary>
    None = 0,

    /// <summary>立即锁定（默认策略）。</summary>
    LockImmediately = 1,

    /// <summary>最小化后经过空闲时限再锁定（可选时限策略）。</summary>
    LockAfterIdleDelay = 2,
}
