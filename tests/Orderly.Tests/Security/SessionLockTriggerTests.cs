using System;
using Orderly.App.Session;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// 任务 9.9 — 会话锁定触发点单元测试。
///
/// 覆盖验收标准：
/// <list type="bullet">
///   <item>需求 18.1：最小化到托盘时锁定（默认立即锁定）；系统睡眠 / 恢复后锁定。</item>
///   <item>需求 18.2：锁定统一进入 <see cref="SessionLockState.PendingPinUnlock"/>，
///         再次进入应用须输入正确 PIN 方可解锁。</item>
/// </list>
///
/// 分两层验证：
/// <list type="number">
///   <item><see cref="TrayLockTriggerPolicy.Evaluate"/> 的纯决策逻辑（任务 9.8 前置）。</item>
///   <item>既有 <see cref="SessionLockService"/> 的系统恢复锁定与 PIN 解锁状态机。</item>
/// </list>
/// 仅覆盖纯逻辑与内存状态机，不触碰磁盘 / 加密库（复用 <see cref="FakeSessionContextService"/>）。
/// </summary>
public sealed class SessionLockTriggerTests
{
    // ---- (1) 最小化到托盘立即锁定（默认时限 <= 0 → LockImmediately） ----

    [Fact]
    public void Tray_minimize_locks_immediately_with_default_delay()
    {
        TrayLockAction action = TrayLockTriggerPolicy.Evaluate(
            isSignedIn: true,
            currentState: SessionLockState.Unlocked,
            idleLockDelay: TimeSpan.Zero);

        Assert.Equal(TrayLockAction.LockImmediately, action);
    }

    [Fact]
    public void Tray_minimize_locks_immediately_with_negative_delay()
    {
        // 负值与零等价于「默认立即锁定」。
        TrayLockAction action = TrayLockTriggerPolicy.Evaluate(
            isSignedIn: true,
            currentState: SessionLockState.Unlocked,
            idleLockDelay: TimeSpan.FromSeconds(-5));

        Assert.Equal(TrayLockAction.LockImmediately, action);
    }

    // ---- (2) 启用空闲时限策略时延时锁定（时限 > 0 → LockAfterIdleDelay） ----

    [Theory]
    [InlineData(1)]      // 1 秒
    [InlineData(300)]    // 5 分钟
    [InlineData(3600)]   // 1 小时
    public void Tray_minimize_defers_lock_when_idle_delay_enabled(int delaySeconds)
    {
        TrayLockAction action = TrayLockTriggerPolicy.Evaluate(
            isSignedIn: true,
            currentState: SessionLockState.Unlocked,
            idleLockDelay: TimeSpan.FromSeconds(delaySeconds));

        // 启用时限策略时不立即锁定，而是等待空闲时限到达后再锁定。
        Assert.Equal(TrayLockAction.LockAfterIdleDelay, action);
        Assert.NotEqual(TrayLockAction.LockImmediately, action);
    }

    // ---- (3) 未登录或非 Unlocked（已锁定 / 已登出）→ None ----

    [Fact]
    public void No_lock_when_not_signed_in()
    {
        // 即便时限为默认立即，未登录也不应触发锁定。
        TrayLockAction action = TrayLockTriggerPolicy.Evaluate(
            isSignedIn: false,
            currentState: SessionLockState.Unlocked,
            idleLockDelay: TimeSpan.Zero);

        Assert.Equal(TrayLockAction.None, action);
    }

    [Theory]
    [InlineData(SessionLockState.PendingPinUnlock)]
    [InlineData(SessionLockState.LoggedOut)]
    public void No_lock_when_state_is_not_unlocked(SessionLockState currentState)
    {
        // 已处于锁定 / 登出状态时无需重复触发锁定（无论时限是否启用）。
        Assert.Equal(
            TrayLockAction.None,
            TrayLockTriggerPolicy.Evaluate(true, currentState, TimeSpan.Zero));

        Assert.Equal(
            TrayLockAction.None,
            TrayLockTriggerPolicy.Evaluate(true, currentState, TimeSpan.FromMinutes(5)));
    }

    // ---- (4) 边界：时限恰为 Zero → 立即锁定 ----

    [Fact]
    public void Idle_delay_at_zero_boundary_locks_immediately()
    {
        TrayLockAction action = TrayLockTriggerPolicy.Evaluate(
            isSignedIn: true,
            currentState: SessionLockState.Unlocked,
            idleLockDelay: TimeSpan.Zero);

        Assert.Equal(TrayLockAction.LockImmediately, action);
    }

    [Fact]
    public void Smallest_positive_delay_defers_lock()
    {
        // 紧邻 Zero 的最小正时限应落入延时分支（边界刚过零即延时）。
        TrayLockAction action = TrayLockTriggerPolicy.Evaluate(
            isSignedIn: true,
            currentState: SessionLockState.Unlocked,
            idleLockDelay: TimeSpan.FromTicks(1));

        Assert.Equal(TrayLockAction.LockAfterIdleDelay, action);
    }

    // ---- (5) 系统睡眠 / 恢复后锁定（复用既有 LockBySystemResume） ----

    [Fact]
    public void System_resume_locks_signed_in_session()
    {
        SessionLockService service = CreateSignedInLockService();

        service.LockBySystemResume();

        Assert.Equal(SessionLockState.PendingPinUnlock, service.State);
        Assert.True(service.IsPinRequired);
    }

    [Fact]
    public void System_resume_does_not_lock_logged_out_session()
    {
        // 未登录（默认 LoggedOut）时系统恢复不应改变状态。
        var service = new SessionLockService(new FakeSessionContextService());

        service.LockBySystemResume();

        Assert.Equal(SessionLockState.LoggedOut, service.State);
        Assert.False(service.IsPinRequired);
    }

    // ---- (6) 锁定后须正确 PIN 才能解锁 ----

    [Fact]
    public void Locked_session_stays_locked_when_pin_not_verified()
    {
        SessionLockService service = CreateSignedInLockService();
        service.LockManually();
        Assert.Equal(SessionLockState.PendingPinUnlock, service.State);

        // PIN 校验未通过：状态保持锁定。
        service.UnlockWithPin(verified: false);

        Assert.Equal(SessionLockState.PendingPinUnlock, service.State);
        Assert.True(service.IsPinRequired);
    }

    [Fact]
    public void Locked_session_unlocks_only_with_verified_pin()
    {
        SessionLockService service = CreateSignedInLockService();
        service.LockBySystemResume();
        Assert.Equal(SessionLockState.PendingPinUnlock, service.State);

        // 正确 PIN 才解锁回到 Unlocked。
        service.UnlockWithPin(verified: true);

        Assert.Equal(SessionLockState.Unlocked, service.State);
        Assert.False(service.IsPinRequired);
    }

    private static SessionLockService CreateSignedInLockService()
    {
        var service = new SessionLockService(new FakeSessionContextService());
        service.MarkSignedIn();
        Assert.Equal(SessionLockState.Unlocked, service.State);
        return service;
    }
}
