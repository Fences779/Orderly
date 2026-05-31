using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class LoginViewModel
{
    public async Task EnterAccountManagementModeAsync(CancellationToken cancellationToken = default)
    {
        if (IsFirstRunMode)
        {
            return;
        }

        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        ResetSignInAccountState();
        IsPasswordRecoveryMode = false;
        IsAccountManagementMode = true;
        IsCreateManagedAccountMode = false;
        IsCreateManagedAccountOwnerVerified = false;
        PendingDeleteAccount = null;
        IsAccountManagementNoticeOpen = true;
        await LoadAccountDirectoryAsync(cancellationToken);
    }

    public void ExitAccountManagementMode()
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        IsAccountManagementMode = false;
        IsCreateManagedAccountMode = false;
        IsCreateManagedAccountOwnerVerified = false;
        PendingDeleteAccount = null;
        IsAccountManagementNoticeOpen = false;
    }

    public void EnterCreateManagedAccountMode()
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        IsAccountManagementNoticeOpen = false;
        PendingDeleteAccount = null;
        AccountManagementStatus = string.Empty;
        IsCreateManagedAccountMode = true;
        IsAccountManagementMode = false;
        IsCreateManagedAccountOwnerVerified = false;
    }

    public void ExitCreateManagedAccountMode()
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        IsAccountManagementMode = true;
        IsCreateManagedAccountMode = false;
        IsCreateManagedAccountOwnerVerified = false;
    }

    public async Task VerifyCreateManagedAccountOwnerAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            ErrorMessage = "请先完整填写管理员验证信息。";
            return;
        }

        IsBusy = true;
        try
        {
            await _localAccountManagementService.VerifyOwnerCredentialsAsync(
                ownerUsername.Trim(),
                ownerMasterPassword,
                ownerPin.Trim(),
                cancellationToken);

            IsCreateManagedAccountOwnerVerified = true;
            PublishSuccessToast("验证成功");
        }
        catch (Exception ex)
        {
            IsCreateManagedAccountOwnerVerified = false;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ResetCreateManagedAccountOwnerVerification()
    {
        if (!IsCreateManagedAccountOwnerVerified)
        {
            return;
        }

        IsCreateManagedAccountOwnerVerified = false;
    }

    public void BeginDeleteAccount(string accountId)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        var target = AccountDirectory.FirstOrDefault(account => string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            AccountManagementStatus = "未找到目标账号。";
            PendingDeleteAccount = null;
            return;
        }

        if (target.Role == LocalAccountRole.Owner)
        {
            AccountManagementStatus = "主账号不允许从这里删除。";
            PendingDeleteAccount = null;
            return;
        }

        PendingDeleteAccount = target;
        AccountManagementStatus = $"待删除账号：{target.DisplayName}（@{target.Username}）";
    }

    public void CancelDeleteAccount()
    {
        PendingDeleteAccount = null;
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        AccountManagementStatus = string.Empty;
    }

    public async Task DeletePendingAccountAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        var target = PendingDeleteAccount;
        if (target is null)
        {
            AccountManagementStatus = "请先选择要删除的账号。";
            return;
        }

        IsBusy = true;
        try
        {
            await _localAccountManagementService.DeleteAccountAsync(
                ownerUsername.Trim(),
                ownerMasterPassword,
                ownerPin.Trim(),
                target.AccountId,
                cancellationToken);

            PendingDeleteAccount = null;
            await LoadAccountDirectoryAsync(cancellationToken);
            AccountManagementStatus = $"账号已删除：{target.DisplayName}（@{target.Username}）";
        }
        catch (Exception ex)
        {
            AccountManagementStatus = $"删除失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CreateManagedAccountAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string memberUsername,
        string memberDisplayName,
        string memberMasterPassword,
        string memberPin,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        if (!IsCreateManagedAccountOwnerVerified)
        {
            ErrorMessage = "请先完成管理员验证。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            ErrorMessage = "请先完整验证管理员账号。";
            return;
        }

        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberMasterPassword)
            || string.IsNullOrWhiteSpace(memberPin))
        {
            ErrorMessage = "请完整填写新账号信息。";
            return;
        }

        if (!MasterPasswordPolicy.TryValidate(memberMasterPassword, out var passwordValidationError))
        {
            ErrorMessage = passwordValidationError;
            return;
        }

        IsBusy = true;
        try
        {
            var member = await _localAccountManagementService.CreateMemberWithOwnerVerificationAsync(
                ownerUsername.Trim(),
                ownerMasterPassword,
                ownerPin.Trim(),
                new CreateMemberAccountRequest
                {
                    Username = memberUsername.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(memberDisplayName) ? memberUsername.Trim() : memberDisplayName.Trim(),
                    MasterPassword = memberMasterPassword,
                    Pin = memberPin.Trim()
                },
                cancellationToken);

            NoticeMessage = $"账号已创建：{member.DisplayName}（@{member.Username}）";
            IsAccountManagementMode = true;
            IsCreateManagedAccountMode = false;
            await LoadAccountDirectoryAsync(cancellationToken);
            PublishSuccessToast("账号创建成功");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AcknowledgeAccountManagementNotice()
    {
        IsAccountManagementNoticeOpen = false;
    }

    private async Task LoadAccountDirectoryAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var accounts = await _localAccountManagementService.ListAccountDirectoryAsync(cancellationToken);
            ReplaceAccountDirectory(accounts);
            if (PendingDeleteAccount is not null
                && !AccountDirectory.Any(account => string.Equals(account.AccountId, PendingDeleteAccount.AccountId, StringComparison.OrdinalIgnoreCase)))
            {
                PendingDeleteAccount = null;
            }

            if (string.IsNullOrWhiteSpace(AccountManagementStatus))
            {
                AccountManagementStatus = string.Empty;
            }
        }
        catch (Exception ex)
        {
            ReplaceAccountDirectory([]);
            PendingDeleteAccount = null;
            AccountManagementStatus = $"账号列表加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceAccountDirectory(IEnumerable<LocalAccountSummary> accounts)
    {
        AccountDirectory.Clear();
        foreach (var account in accounts)
        {
            AccountDirectory.Add(account);
        }
    }
}
