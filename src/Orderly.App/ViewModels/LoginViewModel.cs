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
    private readonly IQuickLoginService _quickLoginService;
    private readonly IWindowsHelloService _windowsHelloService;
    private readonly List<LocalAccountSummary> _availableSignInAccounts = [];
    private LocalSessionContext? _pendingSession;

    public LoginViewModel(
        ILocalAuthService localAuthService,
        ILocalAccountManagementService localAccountManagementService,
        IQuickLoginService quickLoginService,
        IWindowsHelloService windowsHelloService)
    {
        _localAuthService = localAuthService;
        _localAccountManagementService = localAccountManagementService;
        _quickLoginService = quickLoginService;
        _windowsHelloService = windowsHelloService;
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
    [NotifyPropertyChangedFor(nameof(ShouldShowQuickLoginOptIn))]
    private LocalAccountSummary? confirmedSignInAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowQuickLoginOptIn))]
    private bool quickLoginPreferenceEnabled;

    [ObservableProperty]
    private bool quickLoginAvailableThisBoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowQuickLoginOptIn))]
    private bool enableQuickLoginThisBoot;

    [ObservableProperty]
    private bool isQuickLoginMode;

    [ObservableProperty]
    private bool isWindowsHelloAvailable;

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasNoticeMessage => !string.IsNullOrWhiteSpace(NoticeMessage);
    public bool HasPendingDeleteAccount => PendingDeleteAccount is not null;
    public bool HasAccountManagementStatus => !string.IsNullOrWhiteSpace(AccountManagementStatus);
    public bool HasFilteredSignInAccounts => FilteredSignInAccounts.Count > 0;
    public bool HasSignInAccountErrorMessage => !string.IsNullOrWhiteSpace(SignInAccountErrorMessage);
    public bool IsSignInAccountConfirmed => ConfirmedSignInAccount is not null;
    public bool IsSignInPasswordStepVisible => IsSignInAccountConfirmed;
    public bool ShouldShowQuickLoginOptIn => IsSignInAccountConfirmed && !QuickLoginPreferenceEnabled;
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
        ResetQuickLoginState();
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

            try
            {
                await _quickLoginService.CaptureCurrentPasswordSessionAsync(
                    ConfirmedSignInAccount.Username,
                    QuickLoginPreferenceEnabled || EnableQuickLoginThisBoot,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                NoticeMessage = $"已使用主密码登录，但快速登录未能启用：{ex.Message}";
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

    public async Task SignInWithPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (ConfirmedSignInAccount is null || string.IsNullOrWhiteSpace(pin))
        {
            ErrorMessage = "请输入 6 位 PIN。";
            return;
        }

        await CompleteQuickSignInAsync(
            () => _quickLoginService.SignInWithPinAsync(ConfirmedSignInAccount.Username, pin, cancellationToken));
    }

    public async Task SignInWithWindowsHelloAsync(CancellationToken cancellationToken = default)
    {
        if (ConfirmedSignInAccount is null)
        {
            ErrorMessage = "请先确认账号。";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            if (!await _windowsHelloService.VerifyAsync($"验证后登录 Orderly 账号 {ConfirmedSignInAccount.Username}"))
            {
                ErrorMessage = "Windows Hello 验证未通过或已取消。";
                return;
            }

            var result = await _quickLoginService.SignInWithWindowsHelloAsync(
                ConfirmedSignInAccount.Username,
                cancellationToken);
            PublishQuickLoginResult(result);
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

    public void UsePasswordLogin() => IsQuickLoginMode = false;

    public void UseQuickLogin()
    {
        if (QuickLoginAvailableThisBoot)
        {
            IsQuickLoginMode = true;
        }
    }

    private async Task CompleteQuickSignInAsync(Func<Task<LocalSignInResult>> signIn)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            PublishQuickLoginResult(await signIn());
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

    private void PublishQuickLoginResult(LocalSignInResult result)
    {
        if (!result.Succeeded || result.Session is null)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "快速登录失败，请改用主密码。"
                : result.ErrorMessage;
            return;
        }

        LoginSucceeded?.Invoke(result.Session);
    }

    internal async Task RefreshQuickLoginStateAsync(CancellationToken cancellationToken = default)
    {
        if (ConfirmedSignInAccount is null)
        {
            ResetQuickLoginState();
            return;
        }

        var username = ConfirmedSignInAccount.Username;
        try
        {
            var status = await _quickLoginService.GetStatusAsync(username, cancellationToken);
            if (ConfirmedSignInAccount is null
                || !string.Equals(ConfirmedSignInAccount.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            QuickLoginPreferenceEnabled = status.IsEnabled;
            QuickLoginAvailableThisBoot = status.IsAvailableThisBoot;
            IsWindowsHelloAvailable = status.IsAvailableThisBoot && await _windowsHelloService.IsAvailableAsync();
            IsQuickLoginMode = status.IsAvailableThisBoot;
            EnableQuickLoginThisBoot = false;
        }
        catch
        {
            ResetQuickLoginState();
        }
    }

    private void ResetQuickLoginState()
    {
        QuickLoginPreferenceEnabled = false;
        QuickLoginAvailableThisBoot = false;
        EnableQuickLoginThisBoot = false;
        IsQuickLoginMode = false;
        IsWindowsHelloAvailable = false;
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
                var migration = result.LegacyMigrationResult;
                Console.WriteLine(
                    $"已执行 legacy 数据复制：state={migration.Plan.State}; copied={migration.Copied}; " +
                    $"overwritten={migration.Overwritten}; sourceBytes={migration.Plan.LegacyDatabaseSizeBytes}; " +
                    $"executedAt={migration.ExecutedAt:O}");
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

    public void ConsumeSuccessToast()
    {
        SuccessToastMessage = string.Empty;
    }

    private void PublishSuccessToast(string message)
    {
        SuccessToastMessage = string.Empty;
        SuccessToastMessage = message;
    }
}
