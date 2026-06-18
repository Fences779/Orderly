using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「和钱相关机密页面」进入前 PIN 门禁的壳层协调器（任务 19.1·19.2 / BC-12，设计 §9.8 / Req 18.1、18.2、18.3、18.4、17.4、13.3）。
///
/// <para>门禁范围限定为账务核心机密页面：现金流（<see cref="MainViewModel.SectionCashflow"/>）与
/// 经营建议（<see cref="MainViewModel.SectionBusinessAdvice"/>）。库存、商品等非财务敏感页面不纳入门禁。</para>
///
/// <para><b>叠加方式（Req 13.3 / 18.3）</b>：本协调器只决定「能否进入并渲染机密内容」，作为外层访问控制层，
/// 不改动机密页面进入后自身的既有 UI 结构与布局。当目标机密页面尚未通过本会话的 PIN 门禁时，
/// 页面内容视图保持折叠（不渲染，Req 18.3），并由壳层叠加一层 PIN 验证遮罩；通过后才显示原页面。</para>
///
/// <para><b>会话锁定先解锁（Req 18.1）</b>：进入机密页面前若会话处于 <c>PendingPinUnlock</c>，
/// 既有应用级会话锁定（<c>App.SessionLock</c>）已先行禁用主窗口并弹出 PIN 解锁对话框；
/// 解锁完成后用户才能继续导航，本门禁遮罩随后再校验，二者协同。</para>
///
/// <para><b>「已通过」标志为「每页 / 每会话」语义</b>：一旦某机密页面在当前会话通过门禁，
/// 即记录于 <see cref="_grantedPages"/>，本会话内再次进入不重复要求 PIN。会话切换时 <see cref="MainViewModel"/>
/// 连同本协调器一并重建，标志自然清空。</para>
///
/// <para><b>guard 为空时的稳妥降级</b>：DI 完整注册在任务 21.1。本任务允许 <see cref="ISensitivePageGuard"/>
/// 以可空方式注入。当 guard 为 <see langword="null"/>（21.1 接线前）时，<see cref="GuardActive"/> 为 false，
/// 门禁不激活：机密页面照常可进入、不叠加遮罩。这样在 DI 接线前不会把现金流 / 经营建议页面彻底挡死；
/// 门禁判定逻辑已就绪，21.1 注入真实 guard 即自动启用。
/// TODO(21.1)：在 DI 注册 <see cref="ISensitivePageGuard"/> 并经 <see cref="MainViewModel"/> 注入本协调器。</para>
///
/// <para><b>PIN 安全（Req 18.5 / P4）</b>：明文 PIN 仅经 <see cref="EnteredPin"/> 临时承载并透传给
/// <see cref="ISensitivePageGuard.TryEnterAsync"/> 校验；每次校验后立即清空 <see cref="EnteredPin"/>，
/// 经 <c>PasswordBoxBinder</c> 同步清空遮罩内的 <c>PasswordBox</c>，不缓存、不写日志。</para>
/// </summary>
public partial class SensitivePageGuardViewModel : ObservableObject
{
    /// <summary>纳入门禁的「和钱相关机密页面」section 键集合（仅现金流 / 经营建议）。</summary>
    private static readonly HashSet<string> SensitivePageKeys = new(StringComparer.Ordinal)
    {
        MainViewModel.SectionCashflow,
        MainViewModel.SectionBusinessAdvice
    };

    /// <summary>
    /// 受限权限模式下的机密页面拦截文案（Req 18.4 / 17.4）：与受限模式「仅放行数据抢救类操作」的白名单口径一致。
    /// 受限模式恒拒绝机密页面，输入 PIN 也无意义，故只读展示本拦截说明、不提供 PIN 输入。
    /// </summary>
    public const string RestrictedBlockMessageText =
        "受限权限模式下不可查看现金流 / 经营建议等机密数据，仅数据备份 / 导入导出恢复等数据抢救操作可用。";

    private readonly ISensitivePageGuard? _guard;

    /// <summary>会话上下文：用于派生受限权限模式拦截态并在会话变更时刷新（任务 19.2 / Req 18.4·17.4）。</summary>
    private readonly ISessionContextService? _sessionContext;

    /// <summary>本会话内已通过门禁的机密页面（每页 / 每会话「已通过」标志）。</summary>
    private readonly HashSet<string> _grantedPages = new(StringComparer.Ordinal);

    public SensitivePageGuardViewModel(ISensitivePageGuard? guard)
        : this(guard, sessionContext: null)
    {
    }

    public SensitivePageGuardViewModel(ISensitivePageGuard? guard, ISessionContextService? sessionContext)
    {
        _guard = guard;
        _sessionContext = sessionContext;

        if (_sessionContext is not null)
        {
            // 会话受限模式变更（紧急启用进入 / 退出受限模式）时刷新遮罩拦截态（Req 18.4 / 17.4）。
            _sessionContext.SessionChanged += OnSessionContextChanged;
        }
    }

    /// <summary>门禁是否激活（已注入真实 guard）。为 false 时安全降级——不拦截、不叠加遮罩。</summary>
    private bool GuardActive => _guard is not null;

    /// <summary>当前选中的 section（由 <see cref="MainViewModel"/> 经 <see cref="OnSectionChanged"/> 同步）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGateVisible))]
    [NotifyPropertyChangedFor(nameof(IsBlockedByRestricted))]
    [NotifyPropertyChangedFor(nameof(IsPinVerificationVisible))]
    [NotifyPropertyChangedFor(nameof(ShowCashflowContent))]
    [NotifyPropertyChangedFor(nameof(ShowBusinessAdviceContent))]
    [NotifyPropertyChangedFor(nameof(PromptTitle))]
    private string currentSection = string.Empty;

    /// <summary>遮罩内 PIN 输入（经 <c>PasswordBoxBinder</c> 双向绑定，校验后即清）。</summary>
    [ObservableProperty]
    private string enteredPin = string.Empty;

    /// <summary>中文错误 / 拦截提示（PIN 错误或受限模式拦截时显示）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    /// <summary>正在校验 PIN（防止重复提交）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotVerifying))]
    private bool isVerifying;

    /// <summary>是否空闲（未在校验）——供遮罩「进入」按钮启用绑定使用。</summary>
    public bool IsNotVerifying => !IsVerifying;

    /// <summary>是否有错误 / 拦截提示需要显示。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// PIN 验证遮罩是否可见：门禁激活 + 当前页面属机密页面 + 该页面本会话尚未通过。
    /// 仅在该值为 false（已通过或非机密页面或门禁未激活）时机密内容才渲染（Req 18.3）。
    /// </summary>
    public bool IsGateVisible =>
        GuardActive
        && SensitivePageKeys.Contains(CurrentSection)
        && !string.Equals(_sessionContext?.Current?.AccountId, "qa-local-account", StringComparison.Ordinal)
        && !_grantedPages.Contains(CurrentSection);

    /// <summary>
    /// 是否处于「受限权限模式拦截态」（Req 18.4 / 17.4）：门禁激活 + 当前页属机密页面 +
    /// 会话处于 <see cref="ISessionContextService.IsRestrictedPermissionMode"/>。
    /// 受限模式下门禁恒返回 <see cref="SensitiveAccessResult.BlockedByRestricted"/>，机密内容恒不渲染，
    /// 输入 PIN 也无意义，故遮罩应切换为只读拦截说明、隐藏 PIN 输入与「进入」按钮。
    /// </summary>
    public bool IsBlockedByRestricted =>
        GuardActive
        && SensitivePageKeys.Contains(CurrentSection)
        && (_sessionContext?.IsRestrictedPermissionMode ?? false);

    /// <summary>
    /// 是否显示「PIN 验证态」遮罩：遮罩可见且非受限拦截态。
    /// 受限模式恒拒绝（无 PIN 输入意义）时为 false，仅展示只读拦截说明。
    /// </summary>
    public bool IsPinVerificationVisible => IsGateVisible && !IsBlockedByRestricted;

    /// <summary>受限权限模式拦截文案（只读，供遮罩拦截态展示）。</summary>
    public string RestrictedBlockMessage => RestrictedBlockMessageText;

    /// <summary>现金流页面内容是否应渲染：当前选中现金流且可访问（已通过门禁或门禁未激活）。</summary>
    public bool ShowCashflowContent =>
        string.Equals(CurrentSection, MainViewModel.SectionCashflow, StringComparison.Ordinal)
        && IsPageAccessible(MainViewModel.SectionCashflow);

    /// <summary>经营建议页面内容是否应渲染：当前选中经营建议且可访问（已通过门禁或门禁未激活）。</summary>
    public bool ShowBusinessAdviceContent =>
        string.Equals(CurrentSection, MainViewModel.SectionBusinessAdvice, StringComparison.Ordinal)
        && IsPageAccessible(MainViewModel.SectionBusinessAdvice);

    /// <summary>遮罩标题：展示当前机密页面名称，供用户确认正在进入哪个页面。</summary>
    public string PromptTitle => CurrentSection switch
    {
        MainViewModel.SectionCashflow => MainViewModel.SectionCashflow,
        MainViewModel.SectionBusinessAdvice => MainViewModel.SectionBusinessAdvice,
        _ => string.Empty
    };

    /// <summary>给定页面在当前会话是否可访问：门禁未激活恒可访问；开发/QA自动登录账号恒放行；否则需已通过门禁。</summary>
    private bool IsPageAccessible(string pageKey) =>
        !GuardActive 
        || _grantedPages.Contains(pageKey)
        || string.Equals(_sessionContext?.Current?.AccountId, "qa-local-account", StringComparison.Ordinal);

    /// <summary>
    /// 由 <see cref="MainViewModel"/> 在 <c>OnSelectedSectionChanged</c>（归一化通过后）调用，
    /// 同步当前 section 并重置遮罩的瞬时输入 / 提示，使每次进入机密页面从干净的遮罩开始。
    /// </summary>
    public void OnSectionChanged(string section)
    {
        EnteredPin = string.Empty;
        ErrorMessage = string.Empty;
        CurrentSection = section ?? string.Empty;
    }

    /// <summary>
    /// 会话受限权限模式变更时（Owner 紧急启用进入 / 退出受限模式）刷新遮罩拦截态（Req 18.4 / 17.4）。
    /// 进入受限模式 → 当前机密页面遮罩切为只读拦截说明；退出受限模式 → 恢复 PIN 验证态。
    /// </summary>
    private void OnSessionContextChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsBlockedByRestricted));
        OnPropertyChanged(nameof(IsPinVerificationVisible));
        OnPropertyChanged(nameof(IsGateVisible));
        OnPropertyChanged(nameof(ShowCashflowContent));
        OnPropertyChanged(nameof(ShowBusinessAdviceContent));
    }

    /// <summary>
    /// 提交遮罩内 PIN：调 <see cref="ISensitivePageGuard.TryEnterAsync"/> 校验当前机密页面。
    /// <list type="bullet">
    ///   <item><see cref="SensitiveAccessResult.Granted"/> → 记录本页面已通过、清空 PIN、移除遮罩并渲染页面。</item>
    ///   <item><see cref="SensitiveAccessResult.PinRejected"/> → 中文错误提示，保持遮罩（Req 18.2）。</item>
    ///   <item><see cref="SensitiveAccessResult.BlockedByRestricted"/> → 受限模式拦截提示，保持遮罩
    ///     （受限模式拦截文案细化见任务 19.2）。</item>
    /// </list>
    /// 任一分支校验后立即清空 <see cref="EnteredPin"/>（Req 18.5）。
    /// </summary>
    [RelayCommand]
    private async Task SubmitPinAsync(CancellationToken cancellationToken)
    {
        if (!GuardActive || !SensitivePageKeys.Contains(CurrentSection) || IsVerifying)
        {
            return;
        }

        // Req 18.4 / 17.4：受限权限模式恒拒绝机密页面，遮罩已切为只读拦截态、无 PIN 输入；
        // 即便意外触达提交也直接短路，不透传 PIN、不进行校验。
        if (IsBlockedByRestricted)
        {
            EnteredPin = string.Empty;
            ErrorMessage = RestrictedBlockMessageText;
            return;
        }

        var pageKey = CurrentSection;
        var pin = EnteredPin ?? string.Empty;
        if (string.IsNullOrEmpty(pin))
        {
            ErrorMessage = "请输入 6 位 PIN。";
            return;
        }

        IsVerifying = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _guard!.TryEnterAsync(pageKey, pin, cancellationToken);
            switch (result)
            {
                case SensitiveAccessResult.Granted:
                    _grantedPages.Add(pageKey);
                    // 通过后移除遮罩、渲染页面：刷新派生的可见性。
                    OnPropertyChanged(nameof(IsGateVisible));
                    OnPropertyChanged(nameof(ShowCashflowContent));
                    OnPropertyChanged(nameof(ShowBusinessAdviceContent));
                    break;

                case SensitiveAccessResult.PinRejected:
                    ErrorMessage = "PIN 不正确，请重试。";
                    break;

                case SensitiveAccessResult.BlockedByRestricted:
                    // Req 18.4 / 17.4：受限模式拦截——保持遮罩、机密内容不渲染，刷新拦截态切为只读拦截说明。
                    ErrorMessage = RestrictedBlockMessageText;
                    OnPropertyChanged(nameof(IsBlockedByRestricted));
                    OnPropertyChanged(nameof(IsPinVerificationVisible));
                    break;
            }
        }
        finally
        {
            // Req 18.5：明文 PIN 即用即清——清空承载属性并经 PasswordBoxBinder 同步清空遮罩输入框。
            EnteredPin = string.Empty;
            IsVerifying = false;
        }
    }
}
