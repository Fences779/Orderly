using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisableMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPinCommand))]
    private LocalAccountSummary? selectedManagedAccount;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPasswordCommand))]
    private string resetMemberPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetMemberPinCommand))]
    private string resetMemberPinInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetOwnerPasswordWithRecoveryKeyCommand))]
    private string ownerRecoveryUsername = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetOwnerPasswordWithRecoveryKeyCommand))]
    private string ownerRecoveryKeyInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetOwnerPasswordWithRecoveryKeyCommand))]
    private string ownerRecoveryPinInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetOwnerPasswordWithRecoveryKeyCommand))]
    private string ownerNewPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentMasterPasswordCommand))]
    private string currentMasterPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentMasterPasswordCommand))]
    private string newCurrentMasterPasswordInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentPinCommand))]
    private string currentPinInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCurrentPinCommand))]
    private string newCurrentPinInput = string.Empty;

    [ObservableProperty]
    private string accountManagementStatus = string.Empty;

    public bool CanManageAccounts => IsCurrentUserOwner && _localAccountManagementService is not null;

    public bool CanOperateMember => SelectedManagedAccount is { Role: LocalAccountRole.Member };

    partial void OnIsCurrentUserOwnerChanged(bool value)
    {
        OnPropertyChanged(nameof(CanManageAccounts));
        OnPropertyChanged(nameof(CanOperateMember));
        NotifyCommandStateChanged(
            RefreshManagedAccountsCommand,
            CreateMemberCommand,
            DisableMemberCommand,
            ResetMemberPasswordCommand,
            ResetMemberPinCommand,
            ResetOwnerPasswordWithRecoveryKeyCommand,
            ChangeCurrentMasterPasswordCommand,
            ChangeCurrentPinCommand,
            SelectBackupFileCommand,
            ExportBackupCommand,
            ValidateBackupCommand,
            RestoreBackupCommand,
            PreviewCloudImportCommand,
            CommitCloudImportCommand);
        NotifyCloudImportStateChanged();
    }

    partial void OnSelectedManagedAccountChanged(LocalAccountSummary? value)
    {
        OnPropertyChanged(nameof(CanOperateMember));
    }

    private async Task LoadManagedAccountsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManageAccounts)
        {
            ManagedAccounts.Clear();
            return;
        }

        try
        {
            var accounts = await _localAccountManagementService!.ListAccountsAsync(cancellationToken);
            ReplaceCollection(ManagedAccounts, accounts);
            if (SelectedManagedAccount is null || !ManagedAccounts.Any(account => account.AccountId == SelectedManagedAccount.AccountId))
            {
                SelectedManagedAccount = ManagedAccounts.FirstOrDefault();
            }

            AccountManagementStatus = $"账号数：{ManagedAccounts.Count}";
        }
        catch (Exception ex)
        {
            AccountManagementStatus = $"账号列表加载失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshManagedAccountsAsync()
    {
        await LoadManagedAccountsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCreateMember))]
    private async Task CreateMemberAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在创建 Member 账号...",
            successMessage: "Member 账号已创建",
            errorTitle: "创建 Member 失败",
            errorStatusPrefix: "创建 Member 失败",
            action: async () =>
            {
                var member = await _localAccountManagementService!.CreateMemberAsync(new CreateMemberAccountRequest
                {
                    Username = NewMemberUsername.Trim(),
                    DisplayName = NewMemberDisplayName.Trim(),
                    MasterPassword = NewMemberPassword,
                    Pin = NewMemberPin.Trim()
                });

                NewMemberUsername = string.Empty;
                NewMemberDisplayName = string.Empty;
                NewMemberPassword = string.Empty;
                NewMemberPin = string.Empty;
                await LoadManagedAccountsAsync();
                SelectedManagedAccount = ManagedAccounts.FirstOrDefault(account => account.AccountId == member.AccountId);
            });
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnSelectedMember))]
    private async Task DisableMemberAsync()
    {
        var target = SelectedManagedAccount ?? throw new InvalidOperationException("请先选择 Member 账号。");

        await ExecuteSaveActionAsync(
            busyMessage: "正在禁用 Member...",
            successMessage: "Member 已禁用",
            errorTitle: "禁用 Member 失败",
            errorStatusPrefix: "禁用 Member 失败",
            action: async () =>
            {
                await _localAccountManagementService!.DisableMemberAsync(target.AccountId);
                await LoadManagedAccountsAsync();
                SelectedManagedAccount = ManagedAccounts.FirstOrDefault(account => account.AccountId == target.AccountId);
            });
    }

    [RelayCommand(CanExecute = nameof(CanResetSelectedMemberPassword))]
    private async Task ResetMemberPasswordAsync()
    {
        var target = SelectedManagedAccount ?? throw new InvalidOperationException("请先选择 Member 账号。");

        await ExecuteSaveActionAsync(
            busyMessage: "正在重置 Member 主密码...",
            successMessage: "Member 主密码已重置",
            errorTitle: "重置 Member 主密码失败",
            errorStatusPrefix: "重置 Member 主密码失败",
            action: async () =>
            {
                await _localAccountManagementService!.ResetMemberMasterPasswordAsync(target.AccountId, ResetMemberPasswordInput);
                ResetMemberPasswordInput = string.Empty;
            });
    }

    [RelayCommand(CanExecute = nameof(CanResetSelectedMemberPin))]
    private async Task ResetMemberPinAsync()
    {
        var target = SelectedManagedAccount ?? throw new InvalidOperationException("请先选择 Member 账号。");

        await ExecuteSaveActionAsync(
            busyMessage: "正在重置 Member PIN...",
            successMessage: "Member PIN 已重置",
            errorTitle: "重置 Member PIN 失败",
            errorStatusPrefix: "重置 Member PIN 失败",
            action: async () =>
            {
                await _localAccountManagementService!.ResetMemberPinAsync(target.AccountId, ResetMemberPinInput);
                ResetMemberPinInput = string.Empty;
            });
    }

    [RelayCommand(CanExecute = nameof(CanResetOwnerByRecoveryKey))]
    private async Task ResetOwnerPasswordWithRecoveryKeyAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在执行 Owner Recovery Key 重置...",
            successMessage: "Owner 主密码已通过 Recovery Key 重置",
            errorTitle: "Owner Recovery Key 重置失败",
            errorStatusPrefix: "Owner Recovery Key 重置失败",
            action: async () =>
            {
                await _localAccountManagementService!.ResetOwnerMasterPasswordWithRecoveryKeyAsync(
                    OwnerRecoveryUsername.Trim(),
                    OwnerRecoveryPinInput.Trim(),
                    OwnerRecoveryKeyInput.Trim(),
                    OwnerNewPasswordInput);

                OwnerRecoveryPinInput = string.Empty;
                OwnerRecoveryKeyInput = string.Empty;
                OwnerNewPasswordInput = string.Empty;
            });
    }

    [RelayCommand(CanExecute = nameof(CanChangeCurrentMasterPassword))]
    private async Task ChangeCurrentMasterPasswordAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在修改当前账号主密码...",
            successMessage: "当前账号主密码已修改",
            errorTitle: "修改主密码失败",
            errorStatusPrefix: "修改主密码失败",
            action: async () =>
            {
                await _localAccountManagementService!.ChangeCurrentMasterPasswordAsync(CurrentMasterPasswordInput, NewCurrentMasterPasswordInput);
                CurrentMasterPasswordInput = string.Empty;
                NewCurrentMasterPasswordInput = string.Empty;
            });
    }

    [RelayCommand(CanExecute = nameof(CanChangeCurrentPin))]
    private async Task ChangeCurrentPinAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在修改当前账号 PIN...",
            successMessage: "当前账号 PIN 已修改",
            errorTitle: "修改 PIN 失败",
            errorStatusPrefix: "修改 PIN 失败",
            action: async () =>
            {
                await _localAccountManagementService!.ChangeCurrentPinAsync(CurrentPinInput.Trim(), NewCurrentPinInput.Trim());
                CurrentPinInput = string.Empty;
                NewCurrentPinInput = string.Empty;
            });
    }

}
