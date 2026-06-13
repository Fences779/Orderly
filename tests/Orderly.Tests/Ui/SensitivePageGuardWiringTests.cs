using System;
using System.Threading;
using System.Threading.Tasks;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// Task 19.3: 敏感页面 PIN 门禁接线集成测试（BC-12 / design §9.8 / Req 18.1、18.3、18.4、13.3）。
///
/// <para>验证 <see cref="SensitivePageGuardViewModel"/> 与后端 <see cref="ISensitivePageGuard"/>
/// （此处用真实 <see cref="SensitivePageGuard"/> + fake <see cref="ISessionContextService"/> /
/// <see cref="ILocalAuthService"/>）接线后的端到端门控行为：</para>
/// <list type="bullet">
///   <item>切到现金流 / 经营建议 → 遮罩可见、机密内容不渲染（Req 18.3）。</item>
///   <item>提交正确 PIN（guard 返回 <see cref="SensitiveAccessResult.Granted"/>）→ 放行渲染、遮罩消失（Req 18.1）。</item>
///   <item>提交错误 PIN（<see cref="SensitiveAccessResult.PinRejected"/>）→ 保持遮罩、机密不渲染、给中文错误提示。</item>
///   <item>受限模式（<see cref="ISessionContextService.IsRestrictedPermissionMode"/> = true，guard 返回
///     <see cref="SensitiveAccessResult.BlockedByRestricted"/>）→ 受限拦截态、机密不渲染（Req 18.4）。</item>
///   <item>切到库存管理 / 商品等非机密页面 → 不触发门禁、遮罩不可见。</item>
///   <item>guard 未注入（null）→ 安全降级、不阻断、机密照常渲染。</item>
/// </list>
///
/// <para><b>架构约束断言（Req 13.3 / 会话锁定先解锁）</b>：门禁仅作为访问控制层控制「能否渲染机密内容」，
/// 遮罩可见与内容渲染互斥（门禁不改动页面进入后自身的 UI 结构 / 布局）；会话锁定（<c>PendingPinUnlock</c>）
/// 的「先解锁」由既有 <c>App.SessionLock</c> 协同——锁定期间机密内容恒不渲染（等价于遮罩可见 ⟺ 内容折叠），
/// 本测试聚焦门禁的渲染门控逻辑，断言「未通过门禁 ⟺ 机密内容不渲染」。</para>
///
/// <para>VM 仅依赖 <c>CommunityToolkit.Mvvm</c>（<see cref="CommunityToolkit.Mvvm.ComponentModel.ObservableObject"/>），
/// 无 WPF 控件依赖，故用普通 <c>[Fact]</c> 无需 STA。协作依赖均以可配置 fake 替身实现。</para>
/// </summary>
public sealed class SensitivePageGuardWiringTests
{
    private const string FixedAccountId = "acct-fixed-id";
    private const string CorrectPin = "246813";

    private static SensitivePageGuardViewModel CreateVm(
        SessionPermissionMode mode,
        out ConfigurableSessionContextService session)
    {
        session = new ConfigurableSessionContextService(FixedAccountId, mode);
        var auth = new StubAuthService(FixedAccountId, CorrectPin);
        var guard = new SensitivePageGuard(session, auth);
        return new SensitivePageGuardViewModel(guard, session);
    }

    // ── 切到机密页面：遮罩可见、机密内容不渲染（Req 18.3） ─────────────────────────────
    [Theory]
    [InlineData(MainViewModel.SectionCashflow)]
    [InlineData(MainViewModel.SectionBusinessAdvice)]
    public void EnteringSensitivePage_ShowsGate_AndHidesConfidentialContent(string section)
    {
        var vm = CreateVm(SessionPermissionMode.Normal, out _);

        vm.OnSectionChanged(section);

        Assert.True(vm.IsGateVisible);
        Assert.True(vm.IsPinVerificationVisible);
        Assert.False(vm.IsBlockedByRestricted);
        Assert.False(vm.ShowCashflowContent);
        Assert.False(vm.ShowBusinessAdviceContent);

        // Req 13.3：门禁仅作为渲染门控层——遮罩可见 ⟺ 机密内容不渲染（不改动页面内部 UI）。
        Assert.True(vm.IsGateVisible && !ContentRendered(vm, section));
    }

    // ── 提交正确 PIN → 放行渲染、遮罩消失（Req 18.1） ─────────────────────────────────
    [Theory]
    [InlineData(MainViewModel.SectionCashflow)]
    [InlineData(MainViewModel.SectionBusinessAdvice)]
    public async Task SubmitCorrectPin_GrantsAccess_RendersContent_AndHidesGate(string section)
    {
        var vm = CreateVm(SessionPermissionMode.Normal, out _);
        vm.OnSectionChanged(section);
        vm.EnteredPin = CorrectPin;

        await vm.SubmitPinCommand.ExecuteAsync(null);

        Assert.False(vm.IsGateVisible);
        Assert.True(ContentRendered(vm, section));
        Assert.False(vm.HasError);
        // Req 18.5：明文 PIN 校验后即清。
        Assert.Equal(string.Empty, vm.EnteredPin);
    }

    // ── 提交错误 PIN → 保持遮罩、机密不渲染、有中文错误提示（Req 18.2 / 18.3） ──────────
    [Theory]
    [InlineData(MainViewModel.SectionCashflow)]
    [InlineData(MainViewModel.SectionBusinessAdvice)]
    public async Task SubmitWrongPin_KeepsGate_HidesContent_AndShowsError(string section)
    {
        var vm = CreateVm(SessionPermissionMode.Normal, out _);
        vm.OnSectionChanged(section);
        vm.EnteredPin = CorrectPin + "9"; // 与正确 PIN 不一致

        await vm.SubmitPinCommand.ExecuteAsync(null);

        Assert.True(vm.IsGateVisible);
        Assert.False(ContentRendered(vm, section));
        Assert.True(vm.HasError);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        // Req 18.5：错误路径同样即清明文 PIN。
        Assert.Equal(string.Empty, vm.EnteredPin);
    }

    // ── 受限模式 → 受限拦截态、机密不渲染、先于 PIN 校验（Req 18.4 / 17.4） ─────────────
    [Theory]
    [InlineData(MainViewModel.SectionCashflow)]
    [InlineData(MainViewModel.SectionBusinessAdvice)]
    public async Task RestrictedMode_BlocksSensitivePage_AndNeverRendersContent(string section)
    {
        var vm = CreateVm(SessionPermissionMode.Restricted_Permission, out _);
        vm.OnSectionChanged(section);

        // 受限模式下遮罩切为只读拦截态：无 PIN 验证输入、机密内容不渲染。
        Assert.True(vm.IsGateVisible);
        Assert.True(vm.IsBlockedByRestricted);
        Assert.False(vm.IsPinVerificationVisible);
        Assert.False(ContentRendered(vm, section));

        // 即便意外提交 PIN，受限模式恒拒绝、机密仍不渲染。
        vm.EnteredPin = CorrectPin;
        await vm.SubmitPinCommand.ExecuteAsync(null);

        Assert.True(vm.IsBlockedByRestricted);
        Assert.False(ContentRendered(vm, section));
        Assert.Equal(string.Empty, vm.EnteredPin);
    }

    // ── 非机密页面（库存 / 商品）→ 不触发门禁、遮罩不可见 ───────────────────────────────
    [Theory]
    [InlineData(MainViewModel.SectionInventory)]
    [InlineData(MainViewModel.SectionProducts)]
    [InlineData(MainViewModel.SectionOrders)]
    [InlineData(MainViewModel.SectionWorkbench)]
    public void NonFinancialPage_DoesNotTriggerGate(string section)
    {
        var vm = CreateVm(SessionPermissionMode.Normal, out _);

        vm.OnSectionChanged(section);

        Assert.False(vm.IsGateVisible);
        Assert.False(vm.IsBlockedByRestricted);
        Assert.False(vm.IsPinVerificationVisible);
    }

    [Fact]
    public void NonFinancialPage_NotGated_EvenInRestrictedMode()
    {
        var vm = CreateVm(SessionPermissionMode.Restricted_Permission, out _);

        // 库存等非机密页面不纳入门禁集合，受限模式也不应对其拦截。
        vm.OnSectionChanged(MainViewModel.SectionInventory);

        Assert.False(vm.IsGateVisible);
        Assert.False(vm.IsBlockedByRestricted);
    }

    // ── guard 未注入（null）→ 安全降级、不阻断、机密照常渲染 ────────────────────────────
    [Theory]
    [InlineData(MainViewModel.SectionCashflow)]
    [InlineData(MainViewModel.SectionBusinessAdvice)]
    public void NullGuard_SafelyDegrades_DoesNotBlockSensitivePage(string section)
    {
        // guard 为 null（DI 接线前，21.1 之前）→ 门禁不激活。
        var vm = new SensitivePageGuardViewModel(guard: null, sessionContext: null);

        vm.OnSectionChanged(section);

        Assert.False(vm.IsGateVisible);
        Assert.False(vm.IsBlockedByRestricted);
        // 不阻断：机密内容照常渲染。
        Assert.True(ContentRendered(vm, section));
    }

    /// <summary>当前 section 的机密内容是否渲染（按页面键取对应渲染标志）。</summary>
    private static bool ContentRendered(SensitivePageGuardViewModel vm, string section) =>
        section switch
        {
            MainViewModel.SectionCashflow => vm.ShowCashflowContent,
            MainViewModel.SectionBusinessAdvice => vm.ShowBusinessAdviceContent,
            _ => false
        };

    // ── 可配置依赖替身 ────────────────────────────────────────────────────────────────

    /// <summary>可配置权限模式与当前账号的会话上下文替身；变更权限模式时触发 SessionChanged。</summary>
    private sealed class ConfigurableSessionContextService : ISessionContextService
    {
        private readonly LocalSessionContext _current;

        public ConfigurableSessionContextService(string accountId, SessionPermissionMode mode)
        {
            _current = new LocalSessionContext { AccountId = accountId };
            CurrentPermissionMode = mode;
        }

        public event EventHandler? SessionChanged;

        public LocalSessionContext? Current => _current;
        public bool IsSignedIn => true;
        public bool IsDataKeyAvailable => false;
        public SessionPermissionMode CurrentPermissionMode { get; private set; }

        public bool IsRestrictedPermissionMode =>
            CurrentPermissionMode == SessionPermissionMode.Restricted_Permission;

        public void SetCurrent(LocalSessionContext session) => SessionChanged?.Invoke(this, EventArgs.Empty);
        public void SuspendDataKey() { }
        public bool TryRestoreDataKey(string accountId) => false;
        public void Clear() { }

        public void SetPermissionMode(SessionPermissionMode mode)
        {
            CurrentPermissionMode = mode;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>以相等比较模拟 PIN 校验的认证替身；明文仅透传比较、不留存。</summary>
    private sealed class StubAuthService : ILocalAuthService
    {
        private readonly string _accountId;
        private readonly string _correctPin;

        public StubAuthService(string accountId, string correctPin)
        {
            _accountId = accountId;
            _correctPin = correctPin;
        }

        public Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
        {
            bool ok = string.Equals(accountId, _accountId, StringComparison.Ordinal)
                && string.Equals(pin, _correctPin, StringComparison.Ordinal);
            return Task.FromResult(ok);
        }

        public Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<LegacyDatabaseMigrationPlan> BuildLegacyMigrationPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CreateFirstOwnerResult> CreateFirstOwnerAsync(CreateFirstOwnerRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LocalSignInResult> SignInAsync(string username, string masterPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
