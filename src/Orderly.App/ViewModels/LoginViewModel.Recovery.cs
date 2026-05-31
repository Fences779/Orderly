using Orderly.Core.Models;
using Orderly.Core.Security;

namespace Orderly.App.ViewModels;

public partial class LoginViewModel
{
    public async Task ResetOwnerPasswordWithRecoveryKeyAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrWhiteSpace(recoveryKey)
            || string.IsNullOrEmpty(newMasterPassword))
        {
            ErrorMessage = "请完整输入 Owner 用户名、PIN、Recovery Key 和新主密码。";
            return;
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var passwordValidationError))
        {
            ErrorMessage = passwordValidationError;
            return;
        }

        IsBusy = true;
        try
        {
            await _localAccountManagementService.ResetOwnerMasterPasswordWithRecoveryKeyAsync(
                ownerUsername.Trim(),
                ownerPin.Trim(),
                recoveryKey.Trim(),
                newMasterPassword,
                cancellationToken);
            NoticeMessage = "Recovery Key 重置成功，请使用新主密码登录。";
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

    public async Task VerifyOwnerPasswordRecoveryAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrWhiteSpace(recoveryKey))
        {
            ErrorMessage = "请完整输入 Owner 用户名、PIN 和 Recovery Key。";
            return;
        }

        IsBusy = true;
        try
        {
            await _localAccountManagementService.VerifyOwnerPasswordRecoveryAsync(
                ownerUsername.Trim(),
                ownerPin.Trim(),
                recoveryKey.Trim(),
                cancellationToken);
            IsRecoveryVerificationConfirmed = true;
            PublishSuccessToast("验证成功");
        }
        catch (Exception ex)
        {
            IsRecoveryVerificationConfirmed = false;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResetMemberPasswordWithOwnerVerificationAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberPin)
            || string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrEmpty(newMasterPassword))
        {
            ErrorMessage = "请完整输入成员账号、成员 PIN、管理员账号、管理员主密码、管理员 PIN 和新主密码。";
            return;
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var passwordValidationError))
        {
            ErrorMessage = passwordValidationError;
            return;
        }

        IsBusy = true;
        try
        {
            await _localAccountManagementService.ResetMemberMasterPasswordWithOwnerVerificationAsync(
                memberUsername.Trim(),
                memberPin.Trim(),
                ownerUsername.Trim(),
                ownerMasterPassword,
                ownerPin.Trim(),
                newMasterPassword,
                cancellationToken);
            NoticeMessage = "成员账号主密码已重置，请使用新主密码登录。";
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

    public async Task VerifyMemberPasswordResetAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberPin)
            || string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            ErrorMessage = "请完整输入成员账号、成员 PIN、管理员账号、管理员主密码和管理员 PIN。";
            return;
        }

        IsBusy = true;
        try
        {
            await _localAccountManagementService.VerifyMemberPasswordResetAsync(
                memberUsername.Trim(),
                memberPin.Trim(),
                ownerUsername.Trim(),
                ownerMasterPassword,
                ownerPin.Trim(),
                cancellationToken);
            IsRecoveryVerificationConfirmed = true;
            PublishSuccessToast("验证成功");
        }
        catch (Exception ex)
        {
            IsRecoveryVerificationConfirmed = false;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void EnterPasswordRecoveryMode()
    {
        if (IsFirstRunMode)
        {
            return;
        }

        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        ResetSignInAccountState();
        IsRecoveryOwnerAccountDetected = false;
        IsRecoveryMemberAccountDetected = false;
        IsRecoveryVerificationConfirmed = false;
        IsAccountManagementMode = false;
        IsCreateManagedAccountMode = false;
        IsCreateManagedAccountOwnerVerified = false;
        IsPasswordRecoveryMode = true;
    }

    public void ExitPasswordRecoveryMode()
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        IsRecoveryOwnerAccountDetected = false;
        IsRecoveryMemberAccountDetected = false;
        IsRecoveryVerificationConfirmed = false;
        IsPasswordRecoveryMode = false;
    }

    public void UpdatePasswordRecoveryAccountInput(string username)
    {
        var normalizedUsername = username.Trim();
        var matchedAccount = _availableSignInAccounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

        IsRecoveryOwnerAccountDetected = matchedAccount is { Role: LocalAccountRole.Owner, IsEnabled: true };
        IsRecoveryMemberAccountDetected = matchedAccount is { Role: LocalAccountRole.Member, IsEnabled: true };
        IsRecoveryVerificationConfirmed = false;
    }
}
