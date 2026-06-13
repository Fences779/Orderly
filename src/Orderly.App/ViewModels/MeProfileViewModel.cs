using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「我的页」专属 ViewModel（设计 §8.1，BC-8）。从 <see cref="MainViewModel"/> 抽出我的页相关状态。
///
/// <para>本类承载身份头图、成员管理、凭证修改与账户安全等状态；命令接线（成员管理 / 凭证 /
/// 头像 / 紧急启用 / 审计筛选）由后续任务 14.2~14.6 完成，本骨架仅聚焦<b>状态</b>迁移并保持可编译。</para>
///
/// <para>构造注入按设计 §8.1 签名给出；当前阶段允许部分服务先以可空形式注入备用，完整 DI
/// 接线与 <c>MeProfileView.DataContext</c> 绑定在集成任务（21.1）统一处理。</para>
///
/// <para>安全约束（P4）：凭证 <c>*Input</c> 仅在内存中即时校验，不记录、不持久化、不写日志；
/// 命令成功后由 VM 置空触发清除。</para>
/// </summary>
public partial class MeProfileViewModel : ObservableObject
{
    private readonly ILocalAccountManagementService? _accountService;
    private readonly ILocalAuthService? _authService;
    private readonly IAvatarStorageService? _avatarService;
    private readonly ISessionContextService? _sessionContext;
    private readonly IToastService? _toast;
    private readonly ICredentialChangeSessionCoordinator? _credentialSession;
    private readonly IAppSettingRepository? _settingRepository;
    private readonly ISecurityAuditService? _securityAudit;
    private readonly IEmergencyEnableService? _emergencyEnable;

    /// <summary>
    /// 构造 <see cref="MeProfileViewModel"/>（设计 §8.1）。
    /// </summary>
    /// <param name="accountService">本地账号管理服务（成员列表 / 创建 / 重置 / 停用 / 删除 / 凭证修改）。</param>
    /// <param name="authService">本地认证服务（登录 / 审计接缝）。</param>
    /// <param name="avatarService">头像存储服务（校验 / 持久化 / 解析）。</param>
    /// <param name="sessionContext">会话上下文服务（身份 / 锁定 / 登出）。</param>
    /// <param name="toast">壳层通用 Toast 服务。</param>
    /// <param name="credentialSession">
    /// 凭证修改后的会话转移协调器（design §9.6 / Req 16）；可空，完整 DI 接线在任务 21.1。
    /// 主密码改成功 → 强制登出；PIN 改成功 → 锁定进入 <c>PendingPinUnlock</c>。
    /// </param>
    /// <param name="settingRepository">
    /// 偏好持久化仓储（design §8.1 / Req 6.3 / 6.4，BC-1~BC-3）；可空，完整 DI 接线在任务 21.1。
    /// 头像命令经其读写 <see cref="AppPreferences.AvatarReference"/> 以持久化 / 恢复头像引用。
    /// </param>
    /// <param name="securityAudit">
    /// 安全审计读取服务（design §6.4 / §10.2，BC-6 / BC-14，Req 9）；可空，完整 DI 接线在任务 21.1。
    /// 注入后 <see cref="IsSecurityAuditAvailable"/> 为 <c>true</c>，账户安全卡经
    /// <see cref="ISecurityAuditService.QueryAsync"/> 按日期范围拉取登录 / 安全记录；为 <c>null</c> 时显示占位文案。
    /// </param>
    /// <param name="emergencyEnable">
    /// Owner 紧急启用服务（design §9.7，BC-13，Req 17）；可空，完整 DI 接线在任务 21.1。
    /// 注入后由「独立的紧急入口弹窗」采集的 6 位 PIN 经 <see cref="IEmergencyEnableService.TryEmergencyEnableAsync"/>
    /// 触发受限权限模式；为 <c>null</c> 时紧急启用命令不可执行。
    /// </param>
    public MeProfileViewModel(
        ILocalAccountManagementService? accountService = null,
        ILocalAuthService? authService = null,
        IAvatarStorageService? avatarService = null,
        ISessionContextService? sessionContext = null,
        IToastService? toast = null,
        ICredentialChangeSessionCoordinator? credentialSession = null,
        IAppSettingRepository? settingRepository = null,
        ISecurityAuditService? securityAudit = null,
        IEmergencyEnableService? emergencyEnable = null)
    {
        _accountService = accountService;
        _authService = authService;
        _avatarService = avatarService;
        _sessionContext = sessionContext;
        _toast = toast;
        _credentialSession = credentialSession;
        _settingRepository = settingRepository;
        _securityAudit = securityAudit;
        _emergencyEnable = emergencyEnable;

        // 受限权限模式只读状态依赖会话权限模式：会话变更时刷新能力门控只读属性（任务 14.6，Req 17.1）。
        if (_sessionContext is not null)
        {
            _sessionContext.SessionChanged += OnSessionContextChangedForRestrictedMode;
        }

        // 成员列表视图：默认视图 + 关键字过滤（搜索状态见 §8.1 / MemberSearchQuery）。
        ManagedAccountsView = CollectionViewSource.GetDefaultView(ManagedAccounts);
        ManagedAccountsView.Filter = FilterManagedAccount;
        ((INotifyCollectionChanged)ManagedAccounts).CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(IsMemberListEmpty));

        // 从当前会话初始化身份（命令接线前的只读快照）。
        var session = _sessionContext?.Current;
        if (session is not null)
        {
            CurrentAccountId = session.AccountId;
            CurrentAccountDisplayName = session.DisplayName;
            IsCurrentUserOwner = session.Role == LocalAccountRole.Owner;
        }
    }

    // ── 身份头图 ──

    /// <summary>当前账号标识，用于成员权限矩阵的「是否自身」判定（不直接绑定 UI）。</summary>
    [ObservableProperty]
    private string currentAccountId = string.Empty;

    [ObservableProperty]
    private string currentAccountDisplayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleBadgeText))]
    [NotifyPropertyChangedFor(nameof(CanManageAccounts))]
    [NotifyCanExecuteChangedFor(nameof(RefreshManagedAccountsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPinCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMemberCommand))]
    private bool isCurrentUserOwner;

    /// <summary>头像图源；为 <c>null</c> 时由视图渲染「渐变底色 + 用户名首字」默认占位（Req 6.5）。</summary>
    [ObservableProperty]
    private ImageSource? avatarImageSource;

    /// <summary>
    /// 角色徽章文案（Req 5.4 / 5.5）：Owner 显示「系统管理员 Owner」，否则一律回退「系统店员 Member」。
    /// 角色判定失败（非 Owner / 未知）天然落入 Member 分支，满足失败回退要求。
    /// </summary>
    public string RoleBadgeText => IsCurrentUserOwner ? "系统管理员 Owner" : "系统店员 Member";

    // ── 成员管理 ──

    public ObservableCollection<LocalAccountSummary> ManagedAccounts { get; } = new();

    /// <summary>支持搜索 / 筛选的成员列表视图（过滤条件为 <see cref="MemberSearchQuery"/>）。</summary>
    public ICollectionView ManagedAccountsView { get; }

    [ObservableProperty]
    private string memberSearchQuery = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPinCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMemberCommand))]
    private LocalAccountSummary? selectedManagedAccount;

    /// <summary>是否具备成员管理能力（仅 Owner 且账号管理服务可用）。</summary>
    public bool CanManageAccounts => IsCurrentUserOwner && _accountService is not null;

    /// <summary>当前所选目标是否为可操作的 Member 账号。</summary>
    public bool CanOperateMember => SelectedManagedAccount is { Role: LocalAccountRole.Member };

    /// <summary>成员列表是否为空（用于空状态展示）。</summary>
    public bool IsMemberListEmpty => ManagedAccountsView is { IsEmpty: true };

    // 权限授权判定（§8.1.1 权限矩阵，Req 7 / Property 12）：纯函数式，仅依据「当前账号角色 / 是否自身」，
    // 复用 Orderly.Core 的 MemberManagementPolicy，不读取 UI 状态。

    /// <summary>是否允许创建成员（仅 Owner）。</summary>
    public bool CanCreateMember => MemberManagementPolicy.CanCreateMember(CurrentRole);

    /// <summary>是否允许删除目标成员（仅 Owner 且目标非自身）。</summary>
    public bool CanDeleteMember(LocalAccountSummary target)
        => MemberManagementPolicy.CanDeleteMember(CurrentRole, CurrentAccountId, target?.AccountId);

    /// <summary>是否允许停用目标成员（Owner 任意 / 或目标为自身）。</summary>
    public bool CanDisableMember(LocalAccountSummary target)
        => MemberManagementPolicy.CanDisableMember(CurrentRole, CurrentAccountId, target?.AccountId);

    private LocalAccountRole CurrentRole => IsCurrentUserOwner ? LocalAccountRole.Owner : LocalAccountRole.Member;

    // 新建 / 重置成员的暂存输入（绑定走 PasswordBox 附加行为, §8.3）。
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMemberCommand))]
    private string newMemberUsername = string.Empty;

    [ObservableProperty]
    private string newMemberDisplayName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMemberCommand))]
    private string newMemberPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMemberCommand))]
    private string newMemberPin = string.Empty;

    // ── 凭证修改 + 校验状态（§8.5 / §9.3） ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterPasswordValidation))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentMasterPasswordCommand))]
    private string currentMasterPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterPasswordValidation))]
    [NotifyPropertyChangedFor(nameof(NewMasterPasswordStrength))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentMasterPasswordCommand))]
    private string newCurrentMasterPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterPasswordValidation))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentMasterPasswordCommand))]
    private string confirmNewMasterPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinValidation))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentPinCommand))]
    private string currentPinInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinValidation))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentPinCommand))]
    private string newCurrentPinInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinValidation))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentPinCommand))]
    private string confirmNewPinInput = string.Empty;

    /// <summary>主密码修改表单实时校验结果（强度偏弱仅警告，不参与 <c>CanSubmit</c>）。</summary>
    public PasswordValidationState MasterPasswordValidation =>
        CredentialValidator.RecomputeMasterPasswordValidation(
            CurrentMasterPasswordInput,
            NewCurrentMasterPasswordInput,
            ConfirmNewMasterPasswordInput);

    /// <summary>PIN 修改表单实时校验结果。</summary>
    public PinValidationState PinValidation =>
        CredentialValidator.RecomputePinValidation(
            CurrentPinInput,
            NewCurrentPinInput,
            ConfirmNewPinInput);

    /// <summary>新主密码强度计（仅展示用警告，不阻断提交）。</summary>
    public PasswordStrength NewMasterPasswordStrength =>
        PasswordStrengthEvaluator.Evaluate(NewCurrentMasterPasswordInput ?? string.Empty);

    // ── 账户安全 / 登录记录（条件，§10） ──
    // 状态、命令与日期范围筛选见 MeProfileViewModel.Security.cs（任务 14.5）：
    // CurrentAccountLastLoginAt / SecurityAuditEntries / IsSecurityAuditAvailable /
    // AuditRangeStart / AuditRangeEnd / ApplyAuditDateRangeCommand / LoadSecurityAuditAsync。

    // 命令接线（成员管理见任务 14.2；凭证 / 头像 / 锁定登出 / 审计筛选 / 紧急启用见任务 14.3~14.6）。
    // 成员管理命令与权限校验实现见 MeProfileViewModel.MemberCommands.cs。

    partial void OnMemberSearchQueryChanged(string value)
    {
        ManagedAccountsView.Refresh();
        OnPropertyChanged(nameof(IsMemberListEmpty));
    }

    partial void OnSelectedManagedAccountChanged(LocalAccountSummary? value)
    {
        OnPropertyChanged(nameof(CanOperateMember));

        // 选中目标变化会影响停用/删除/重置命令的授权判定（CanExecute 依据当前所选目标），需刷新。
        DisableMemberCommand.NotifyCanExecuteChanged();
        DeleteMemberCommand.NotifyCanExecuteChanged();
        ResetMemberPasswordCommand.NotifyCanExecuteChanged();
        ResetMemberPinCommand.NotifyCanExecuteChanged();
    }

    private bool FilterManagedAccount(object item)
    {
        if (string.IsNullOrWhiteSpace(MemberSearchQuery))
        {
            return true;
        }

        if (item is not LocalAccountSummary account)
        {
            return false;
        }

        var query = MemberSearchQuery.Trim();
        return account.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
            || account.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
