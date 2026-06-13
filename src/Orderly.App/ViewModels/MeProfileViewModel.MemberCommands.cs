using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「我的页」成员管理命令与权限矩阵接线（任务 14.2，design §8.1 / §8.1.1，Req 7.1~7.10）。
///
/// <para>命令清单：刷新成员列表、创建成员、重置密码、重置 PIN、停用成员、删除成员。
/// 各命令 <c>CanExecute</c> 复用 <see cref="MemberManagementPolicy"/> 权限矩阵（§8.1.1）：
/// 创建 ⟺ <see cref="MemberManagementPolicy.CanCreateMember"/>；
/// 删除 ⟺ <see cref="MemberManagementPolicy.CanDeleteMember(LocalAccountRole, string?, string?)"/>(target)；
/// 停用 ⟺ <see cref="MemberManagementPolicy.CanDisableMember(LocalAccountRole, string?, string?)"/>(target)。</para>
///
/// <para>删除入口须二次确认（经可注入的 <see cref="ConfirmDeleteMember"/> 委托抽象，便于测试，
/// 实际对话框 UI 由任务 18.2 接线）。二次确认文案明确「仅移除登录账号，其名下历史业务数据与
/// 来源/创建人归属标签保留」，避免误解为级联删除业务数据。</para>
///
/// <para>被权限矩阵拒绝时给中文提示（经 <see cref="IToastService"/>，无则就地状态）<b>且不调用后端</b>。</para>
/// </summary>
public partial class MeProfileViewModel
{
    /// <summary>
    /// 删除成员二次确认文案（Req 7.4 / 7.10）：明确删除仅移除登录账号、保留名下历史业务数据与
    /// 来源/创建人归属标签，避免误解为级联删除业务数据。
    /// </summary>
    public const string DeleteMemberConfirmationMessage =
        "确认删除该成员账号？此操作仅移除其登录账号，其名下历史业务数据与“来源/创建人”归属标签将完整保留，不会被级联删除或匿名化。删除后该业务数据仍会标注由该（已删除）账号创建。";

    /// <summary>
    /// 删除成员的二次确认委托（可注入，便于测试；UI 对话框接线见任务 18.2）。
    /// 入参为二次确认文案，返回 <c>true</c> 表示用户确认删除、<c>false</c> 表示取消。
    /// 未接线（<c>null</c>）时视为<b>未确认</b>，命令不执行删除、不调用后端（安全默认）。
    /// </summary>
    public Func<string, bool>? ConfirmDeleteMember { get; set; }

    /// <summary>命令执行中的忙碌标志，用于禁用并发操作并参与各命令 <c>CanExecute</c>。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshManagedAccountsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPinCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentMasterPasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentPinCommand))]
    [NotifyCanExecuteChangedFor(nameof(TryEmergencyEnableCommand))]
    private bool isBusy;

    /// <summary>成员管理就地状态文案（成功 / 拒绝 / 失败）。无 Toast 服务时作为兜底反馈。</summary>
    [ObservableProperty]
    private string memberManagementStatus = string.Empty;

    /// <summary>重置 Member 主密码的暂存输入（绑定走 PasswordBox 附加行为, §8.3）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPasswordCommand))]
    private string resetMemberPasswordInput = string.Empty;

    /// <summary>重置 Member PIN 的暂存输入（绑定走 PasswordBox 附加行为, §8.3）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPinCommand))]
    private string resetMemberPinInput = string.Empty;

    // ── 命令 CanExecute 谓词（复用权限矩阵 §8.1.1） ──

    private bool CanRefreshManagedAccounts() => CanManageAccounts && !IsBusy;

    /// <summary>创建成员授权：复用 <see cref="MemberManagementPolicy.CanCreateMember"/>（仅 Owner）。</summary>
    private bool CanExecuteCreateMember()
        => _accountService is not null
            && !IsBusy
            && MemberManagementPolicy.CanCreateMember(CurrentRole)
            && !string.IsNullOrWhiteSpace(NewMemberUsername)
            && !string.IsNullOrWhiteSpace(NewMemberPassword)
            && !string.IsNullOrWhiteSpace(NewMemberPin);

    /// <summary>重置密码/PIN 授权：仅 Owner（与创建同为 Owner 门槛），且目标为 Member 账号。</summary>
    private bool CanExecuteResetMemberPassword()
        => _accountService is not null
            && !IsBusy
            && MemberManagementPolicy.CanCreateMember(CurrentRole)
            && SelectedManagedAccount is { Role: LocalAccountRole.Member }
            && !string.IsNullOrWhiteSpace(ResetMemberPasswordInput);

    private bool CanExecuteResetMemberPin()
        => _accountService is not null
            && !IsBusy
            && MemberManagementPolicy.CanCreateMember(CurrentRole)
            && SelectedManagedAccount is { Role: LocalAccountRole.Member }
            && !string.IsNullOrWhiteSpace(ResetMemberPinInput);

    /// <summary>停用授权：复用 <see cref="MemberManagementPolicy.CanDisableMember(LocalAccountRole, string?, string?)"/>（Owner 任意 / 或目标为自身）。</summary>
    private bool CanExecuteDisableMember()
        => _accountService is not null
            && !IsBusy
            && SelectedManagedAccount is not null
            && MemberManagementPolicy.CanDisableMember(CurrentRole, CurrentAccountId, SelectedManagedAccount.AccountId);

    /// <summary>删除授权：复用 <see cref="MemberManagementPolicy.CanDeleteMember(LocalAccountRole, string?, string?)"/>（仅 Owner 且目标非自身）。</summary>
    private bool CanExecuteDeleteMember()
        => _accountService is not null
            && !IsBusy
            && SelectedManagedAccount is not null
            && MemberManagementPolicy.CanDeleteMember(CurrentRole, CurrentAccountId, SelectedManagedAccount.AccountId);

    // ── 命令 ──

    /// <summary>刷新成员列表（Req 7.1 / 7.2 / 7.3：列表为搜索过滤 / 状态徽章 / 空状态提供数据源）。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshManagedAccounts))]
    private async Task RefreshManagedAccountsAsync()
    {
        await LoadManagedAccountsAsync();
    }

    /// <summary>创建成员（Req 7.4 / 7.5）：仅 Owner 可执行，被拒绝给中文提示且不调后端。</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteCreateMember))]
    private async Task CreateMemberAsync()
    {
        if (!MemberManagementPolicy.CanCreateMember(CurrentRole))
        {
            RejectOperation("当前账号无权创建成员（仅系统管理员 Owner 可操作）。");
            return;
        }

        await RunMemberActionAsync(
            successMessage: "Member 账号已创建",
            errorPrefix: "创建 Member 失败",
            action: async ct =>
            {
                var member = await _accountService!.CreateMemberAsync(
                    new CreateMemberAccountRequest
                    {
                        Username = NewMemberUsername.Trim(),
                        DisplayName = NewMemberDisplayName.Trim(),
                        MasterPassword = NewMemberPassword,
                        Pin = NewMemberPin.Trim(),
                    },
                    ct);

                NewMemberUsername = string.Empty;
                NewMemberDisplayName = string.Empty;
                NewMemberPassword = string.Empty;
                NewMemberPin = string.Empty;

                await LoadManagedAccountsAsync(ct);
                SelectedManagedAccount = ManagedAccounts.FirstOrDefault(a => a.AccountId == member.AccountId);
            });
    }

    /// <summary>重置 Member 主密码（Req 7.4）：仅 Owner 可执行。</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteResetMemberPassword))]
    private async Task ResetMemberPasswordAsync()
    {
        var target = SelectedManagedAccount;
        if (target is null || !MemberManagementPolicy.CanCreateMember(CurrentRole))
        {
            RejectOperation("当前账号无权重置成员密码（仅系统管理员 Owner 可操作）。");
            return;
        }

        await RunMemberActionAsync(
            successMessage: "Member 主密码已重置",
            errorPrefix: "重置 Member 主密码失败",
            action: async ct =>
            {
                await _accountService!.ResetMemberMasterPasswordAsync(target.AccountId, ResetMemberPasswordInput, ct);
                ResetMemberPasswordInput = string.Empty;
            });
    }

    /// <summary>重置 Member PIN（Req 7.4）：仅 Owner 可执行。</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteResetMemberPin))]
    private async Task ResetMemberPinAsync()
    {
        var target = SelectedManagedAccount;
        if (target is null || !MemberManagementPolicy.CanCreateMember(CurrentRole))
        {
            RejectOperation("当前账号无权重置成员 PIN（仅系统管理员 Owner 可操作）。");
            return;
        }

        await RunMemberActionAsync(
            successMessage: "Member PIN 已重置",
            errorPrefix: "重置 Member PIN 失败",
            action: async ct =>
            {
                await _accountService!.ResetMemberPinAsync(target.AccountId, ResetMemberPinInput.Trim(), ct);
                ResetMemberPinInput = string.Empty;
            });
    }

    /// <summary>
    /// 停用成员（Req 7.7 / 7.8 / 7.9 / 7.10）：复用权限矩阵 <see cref="MemberManagementPolicy.CanDisableMember(LocalAccountRole, string?, string?)"/>。
    /// 停用仅置 <c>IsEnabled=false</c> 并保留账号（与删除区分）。被拒绝给中文提示且不调后端。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDisableMember))]
    private async Task DisableMemberAsync()
    {
        var target = SelectedManagedAccount;
        if (target is null
            || !MemberManagementPolicy.CanDisableMember(CurrentRole, CurrentAccountId, target.AccountId))
        {
            RejectOperation("当前账号无权停用该成员。");
            return;
        }

        await RunMemberActionAsync(
            successMessage: "Member 已停用",
            errorPrefix: "停用 Member 失败",
            action: async ct =>
            {
                await _accountService!.DisableMemberAsync(target.AccountId, ct);
                await LoadManagedAccountsAsync(ct);
                SelectedManagedAccount = ManagedAccounts.FirstOrDefault(a => a.AccountId == target.AccountId);
            });
    }

    /// <summary>
    /// 删除成员（Req 7.4 / 7.6 / 7.9 / 7.10）：复用权限矩阵 <see cref="MemberManagementPolicy.CanDeleteMember(LocalAccountRole, string?, string?)"/>
    /// （仅 Owner 且目标非自身）。删除=移除登录账号本身，名下历史业务数据与来源/创建人归属标签保留。
    /// <para>删除须二次确认（经 <see cref="ConfirmDeleteMember"/> 委托）；未确认或被权限拒绝时<b>不调用后端</b>。</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDeleteMember))]
    private async Task DeleteMemberAsync()
    {
        var target = SelectedManagedAccount;
        if (target is null
            || !MemberManagementPolicy.CanDeleteMember(CurrentRole, CurrentAccountId, target.AccountId))
        {
            RejectOperation("当前账号无权删除该成员（仅系统管理员 Owner 可删除非自身账号）。");
            return;
        }

        // 二次确认（Req 7.4 / 7.10）：明确仅移除登录账号、保留历史业务数据与归属标签。
        var confirmed = ConfirmDeleteMember?.Invoke(DeleteMemberConfirmationMessage) ?? false;
        if (!confirmed)
        {
            MemberManagementStatus = "已取消删除成员。";
            return;
        }

        await RunMemberActionAsync(
            successMessage: "Member 账号已删除（其名下历史业务数据与来源/创建人归属标签已保留）",
            errorPrefix: "删除 Member 失败",
            action: async ct =>
            {
                await _accountService!.DeleteMemberAsync(target.AccountId, ct);
                await LoadManagedAccountsAsync(ct);
                SelectedManagedAccount = ManagedAccounts.FirstOrDefault();
            });
    }

    // ── 内部帮助 ──

    /// <summary>从账号管理服务加载成员列表，并维持选中项稳定（无服务 / 非 Owner 时清空）。</summary>
    private async Task LoadManagedAccountsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManageAccounts)
        {
            ManagedAccounts.Clear();
            SelectedManagedAccount = null;
            OnPropertyChanged(nameof(IsMemberListEmpty));
            return;
        }

        try
        {
            var accounts = await _accountService!.ListAccountsAsync(cancellationToken);
            var previousId = SelectedManagedAccount?.AccountId;

            ManagedAccounts.Clear();
            foreach (var account in accounts)
            {
                ManagedAccounts.Add(account);
            }

            SelectedManagedAccount = ManagedAccounts.FirstOrDefault(a => a.AccountId == previousId)
                ?? ManagedAccounts.FirstOrDefault();

            MemberManagementStatus = $"账号数：{ManagedAccounts.Count}";
            OnPropertyChanged(nameof(IsMemberListEmpty));
        }
        catch (Exception ex)
        {
            MemberManagementStatus = $"账号列表加载失败：{ex.Message}";
        }
    }

    /// <summary>统一执行成员管理后端动作：忙碌门控 + 成功/失败状态与 Toast 反馈。</summary>
    private async Task RunMemberActionAsync(string successMessage, string errorPrefix, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action(CancellationToken.None);
            MemberManagementStatus = successMessage;
            _toast?.Show(successMessage, ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            var message = $"{errorPrefix}：{ex.Message}";
            MemberManagementStatus = message;
            _toast?.Show(message, ToastSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>权限矩阵拒绝时的统一处理：给中文提示且不调用后端。</summary>
    private void RejectOperation(string message)
    {
        MemberManagementStatus = message;
        _toast?.Show(message, ToastSeverity.Warning);
    }
}
