using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ILocalAuthService _localAuthService;
    private readonly ILocalAccountManagementService _localAccountManagementService;
    private readonly List<LocalAccountSummary> _availableSignInAccounts = [];
    private LocalSessionContext? _pendingSession;

    public LoginViewModel(ILocalAuthService localAuthService, ILocalAccountManagementService localAccountManagementService)
    {
        _localAuthService = localAuthService;
        _localAccountManagementService = localAccountManagementService;
    }

    public event Action<LocalSessionContext>? LoginSucceeded;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecoveryStepVisible))]
    private bool isFirstRunMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoticeMessage))]
    private string noticeMessage = string.Empty;

    [ObservableProperty]
    private string successToastMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecoveryStepVisible))]
    private string generatedRecoveryKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecoveryConfirmationReady))]
    private bool isRecoveryKeyConfirmed;

    [ObservableProperty]
    private bool importLegacyDatabase = true;

    [ObservableProperty]
    private bool overwriteLegacyTarget;

    [ObservableProperty]
    private bool isPasswordRecoveryMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryKeyInput))]
    [NotifyPropertyChangedFor(nameof(HasRecoveryOwnerSection))]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryOwnerVerificationInput))]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryNewPassword))]
    [NotifyPropertyChangedFor(nameof(RecoveryResetHintMessage))]
    private bool isRecoveryOwnerAccountDetected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryKeyInput))]
    [NotifyPropertyChangedFor(nameof(HasRecoveryMemberSection))]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryOwnerVerificationInput))]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryNewPassword))]
    [NotifyPropertyChangedFor(nameof(RecoveryResetHintMessage))]
    private bool isRecoveryMemberAccountDetected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditRecoveryNewPassword))]
    [NotifyPropertyChangedFor(nameof(RecoveryPrimaryActionLabel))]
    private bool isRecoveryVerificationConfirmed;

    [ObservableProperty]
    private bool isAccountManagementMode;

    [ObservableProperty]
    private bool isCreateManagedAccountMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCreateManagedAccountFields))]
    [NotifyPropertyChangedFor(nameof(CreateManagedAccountPrimaryActionLabel))]
    private bool isCreateManagedAccountOwnerVerified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDeleteAccount))]
    [NotifyPropertyChangedFor(nameof(PendingDeleteAccountConfirmationTitle))]
    private LocalAccountSummary? pendingDeleteAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountManagementStatus))]
    private string accountManagementStatus = string.Empty;

    [ObservableProperty]
    private bool isAccountManagementNoticeOpen;

    public ObservableCollection<LocalAccountSummary> AccountDirectory { get; } = [];
    public ObservableCollection<LocalAccountSummary> FilteredSignInAccounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSignInAccountErrorMessage))]
    private string signInAccountErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignInAccountConfirmed))]
    [NotifyPropertyChangedFor(nameof(IsSignInPasswordStepVisible))]
    private LocalAccountSummary? confirmedSignInAccount;

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasNoticeMessage => !string.IsNullOrWhiteSpace(NoticeMessage);
    public bool HasPendingDeleteAccount => PendingDeleteAccount is not null;
    public bool HasAccountManagementStatus => !string.IsNullOrWhiteSpace(AccountManagementStatus);
    public bool HasFilteredSignInAccounts => FilteredSignInAccounts.Count > 0;
    public bool HasSignInAccountErrorMessage => !string.IsNullOrWhiteSpace(SignInAccountErrorMessage);
    public bool IsSignInAccountConfirmed => ConfirmedSignInAccount is not null;
    public bool IsSignInPasswordStepVisible => IsSignInAccountConfirmed;
    public bool HasRecoveryOwnerSection => IsRecoveryOwnerAccountDetected;
    public bool HasRecoveryMemberSection => IsRecoveryMemberAccountDetected;
    public bool CanEditRecoveryKeyInput => IsRecoveryOwnerAccountDetected;
    public bool CanEditRecoveryOwnerVerificationInput => IsRecoveryMemberAccountDetected;
    public bool CanEditRecoveryNewPassword => IsRecoveryVerificationConfirmed && (IsRecoveryOwnerAccountDetected || IsRecoveryMemberAccountDetected);
    public bool CanEditCreateManagedAccountFields => IsCreateManagedAccountOwnerVerified;
    public string CreateManagedAccountPrimaryActionLabel => IsCreateManagedAccountOwnerVerified ? "创建账号" : "确认验证";
    public string RecoveryPrimaryActionLabel => IsRecoveryVerificationConfirmed ? "确认重置" : "确认验证";
    public string RecoveryResetHintMessage => IsRecoveryOwnerAccountDetected
        ? "检测到主账号，请先完成 Recovery Key 验证。"
        : IsRecoveryMemberAccountDetected
            ? "检测到成员账号，请先完成管理员验证。"
            : "\n主账号需提供 Recovery Key\n成员账号需管理员验证";
    public string PendingDeleteAccountConfirmationTitle =>
        PendingDeleteAccount is null
            ? "确认删除账户"
            : string.IsNullOrWhiteSpace(PendingDeleteAccount.DisplayName)
                ? $"确认删除账户 @{PendingDeleteAccount.Username}"
                : $"确认删除账户 @{PendingDeleteAccount.Username}（{PendingDeleteAccount.DisplayName}）";

    public bool IsRecoveryStepVisible => !string.IsNullOrWhiteSpace(GeneratedRecoveryKey);

    public bool IsRecoveryConfirmationReady => IsRecoveryStepVisible && IsRecoveryKeyConfirmed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        GeneratedRecoveryKey = string.Empty;
        IsRecoveryKeyConfirmed = false;
        IsPasswordRecoveryMode = false;
        IsRecoveryOwnerAccountDetected = false;
        IsRecoveryMemberAccountDetected = false;
        IsRecoveryVerificationConfirmed = false;
        IsAccountManagementMode = false;
        IsCreateManagedAccountMode = false;
        IsCreateManagedAccountOwnerVerified = false;
        PendingDeleteAccount = null;
        IsAccountManagementNoticeOpen = false;
        AccountManagementStatus = string.Empty;
        SignInAccountErrorMessage = string.Empty;
        ConfirmedSignInAccount = null;
        AccountDirectory.Clear();
        FilteredSignInAccounts.Clear();
        _availableSignInAccounts.Clear();
        _pendingSession = null;

        try
        {
            IsFirstRunMode = !await _localAuthService.HasAnyAccountAsync(cancellationToken);
            if (!IsFirstRunMode)
            {
                var accounts = await _localAccountManagementService.ListAccountDirectoryAsync(cancellationToken);
                LoadSignInAccounts(accounts);
            }
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

    public async Task SignInAsync(string username, string masterPassword, CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        GeneratedRecoveryKey = string.Empty;
        IsRecoveryKeyConfirmed = false;
        IsPasswordRecoveryMode = false;
        IsAccountManagementMode = false;
        IsCreateManagedAccountMode = false;
        IsCreateManagedAccountOwnerVerified = false;
        _pendingSession = null;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(masterPassword))
        {
            ErrorMessage = "请输入用户名和主密码。";
            return;
        }

        if (!IsSignInAccountConfirmed || ConfirmedSignInAccount is null)
        {
            SignInAccountErrorMessage = "请先确认账号。";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _localAuthService.SignInAsync(ConfirmedSignInAccount.Username, masterPassword, cancellationToken);
            if (!result.Succeeded || result.Session is null)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "登录失败，请检查用户名或主密码。"
                    : result.ErrorMessage;
                return;
            }

            LoginSucceeded?.Invoke(result.Session);
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

    public async Task CreateFirstOwnerAsync(
        string username,
        string displayName,
        string masterPassword,
        string pin,
        CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        GeneratedRecoveryKey = string.Empty;
        IsRecoveryKeyConfirmed = false;
        IsAccountManagementMode = false;
        _pendingSession = null;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(masterPassword) || string.IsNullOrWhiteSpace(pin))
        {
            ErrorMessage = "请完整填写用户名、主密码和 PIN。";
            return;
        }

        if (!MasterPasswordPolicy.TryValidate(masterPassword, out var passwordValidationError))
        {
            ErrorMessage = passwordValidationError;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _localAuthService.CreateFirstOwnerAsync(new CreateFirstOwnerRequest
            {
                Username = username.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
                MasterPassword = masterPassword,
                Pin = pin.Trim(),
                ImportLegacyDatabase = ImportLegacyDatabase,
                OverwriteTargetOnLegacyImport = OverwriteLegacyTarget
            }, cancellationToken);

            GeneratedRecoveryKey = result.RecoveryKey;
            _pendingSession = result.Session;

            if (result.LegacyMigrationResult is null)
            {
                Console.WriteLine(result.LegacyMigrationPlan.Message);
            }
            else
            {
                Console.WriteLine(
                    $"已执行 legacy 数据复制：{result.LegacyMigrationResult.Plan.LegacyDatabasePath} -> {result.LegacyMigrationResult.Plan.TargetDatabasePath}");
            }
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

    public void ConfirmRecoveryKeyAndContinue()
    {
        if (!IsRecoveryConfirmationReady || _pendingSession is null)
        {
            ErrorMessage = "请先确认已离线保存 Recovery Key。";
            return;
        }

        ErrorMessage = string.Empty;
        LoginSucceeded?.Invoke(_pendingSession);
    }

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

    public void UpdateSignInUsernameInput(string username)
    {
        var normalizedUsername = username.Trim();
        SignInAccountErrorMessage = string.Empty;

        if (ConfirmedSignInAccount is not null
            && !string.Equals(ConfirmedSignInAccount.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
        {
            ConfirmedSignInAccount = null;
        }

        ApplySignInAccountFilter(normalizedUsername);
    }

    public bool TryConfirmSignInAccount(string username)
    {
        var normalizedUsername = username.Trim();
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        SignInAccountErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            ConfirmedSignInAccount = null;
            ApplySignInAccountFilter(string.Empty);
            return false;
        }

        var account = _availableSignInAccounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            ConfirmedSignInAccount = null;
            SignInAccountErrorMessage = "账号不存在，请检查后重试。";
            ApplySignInAccountFilter(normalizedUsername);
            return false;
        }

        ConfirmedSignInAccount = account;
        ReplaceFilteredSignInAccounts([]);
        return true;
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

    public void ConsumeSuccessToast()
    {
        SuccessToastMessage = string.Empty;
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

    private void LoadSignInAccounts(IEnumerable<LocalAccountSummary> accounts)
    {
        _availableSignInAccounts.Clear();
        _availableSignInAccounts.AddRange(accounts
            .Where(account => account.IsEnabled)
            .OrderByDescending(account => account.LastLoginAt ?? DateTime.MinValue)
            .ThenBy(account => account.CreatedAt));

        ApplySignInAccountFilter(string.Empty);
    }

    private void ApplySignInAccountFilter(string username)
    {
        if (ConfirmedSignInAccount is not null)
        {
            ReplaceFilteredSignInAccounts([]);
            return;
        }

        var candidates = string.IsNullOrWhiteSpace(username)
            ? _availableSignInAccounts
            : _availableSignInAccounts
                .Where(account => account.Username.StartsWith(username, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ReplaceFilteredSignInAccounts(candidates);
    }

    private void ReplaceFilteredSignInAccounts(IEnumerable<LocalAccountSummary> accounts)
    {
        FilteredSignInAccounts.Clear();
        foreach (var account in accounts)
        {
            FilteredSignInAccounts.Add(account);
        }

        OnPropertyChanged(nameof(HasFilteredSignInAccounts));
    }

    private void ResetSignInAccountState()
    {
        SignInAccountErrorMessage = string.Empty;
        ConfirmedSignInAccount = null;
        ApplySignInAccountFilter(string.Empty);
    }

    private void PublishSuccessToast(string message)
    {
        SuccessToastMessage = string.Empty;
        SuccessToastMessage = message;
    }
}
