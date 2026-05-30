namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private bool CanCreateMember()
    {
        return CanManageAccounts
            && !IsBusy
            && !string.IsNullOrWhiteSpace(NewMemberUsername)
            && !string.IsNullOrWhiteSpace(NewMemberPassword)
            && !string.IsNullOrWhiteSpace(NewMemberPin);
    }

    private bool CanOperateOnSelectedMember()
    {
        return CanManageAccounts && !IsBusy && CanOperateMember;
    }

    private bool CanResetSelectedMemberPassword()
    {
        return CanOperateOnSelectedMember() && !string.IsNullOrWhiteSpace(ResetMemberPasswordInput);
    }

    private bool CanResetSelectedMemberPin()
    {
        return CanOperateOnSelectedMember() && !string.IsNullOrWhiteSpace(ResetMemberPinInput);
    }

    private bool CanResetOwnerByRecoveryKey()
    {
        return CanManageAccounts
            && !IsBusy
            && !string.IsNullOrWhiteSpace(OwnerRecoveryUsername)
            && !string.IsNullOrWhiteSpace(OwnerRecoveryPinInput)
            && !string.IsNullOrWhiteSpace(OwnerRecoveryKeyInput)
            && !string.IsNullOrWhiteSpace(OwnerNewPasswordInput);
    }

    private bool CanChangeCurrentMasterPassword()
    {
        return _localAccountManagementService is not null
            && !IsBusy
            && !string.IsNullOrWhiteSpace(CurrentMasterPasswordInput)
            && !string.IsNullOrWhiteSpace(NewCurrentMasterPasswordInput);
    }

    private bool CanChangeCurrentPin()
    {
        return _localAccountManagementService is not null
            && !IsBusy
            && !string.IsNullOrWhiteSpace(CurrentPinInput)
            && !string.IsNullOrWhiteSpace(NewCurrentPinInput);
    }
}
