using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

public sealed class QuickLoginServiceTests
{
    [Fact]
    public async Task Pin_quick_login_restores_the_current_boot_session_and_credential_change_invalidates_it()
    {
        var account = CreateAccount();
        var ticketPath = GetTicketPath(account.AccountId);
        var repository = new InMemoryLocalAccountRepository([account]);
        var sessionContext = new SessionContextService();
        var auth = new AcceptingPinAuthService();
        var service = new QuickLoginService(repository, sessionContext, auth);
        var dataKey = RandomNumberGenerator.GetBytes(32);

        try
        {
            sessionContext.SetCurrent(CreateSession(account, dataKey));
            await service.SetEnabledForCurrentAccountAsync(true);

            var enabled = await service.GetStatusAsync(account.Username);
            Assert.True(enabled.IsEnabled);
            Assert.True(enabled.IsAvailableThisBoot);

            await service.CaptureCurrentPasswordSessionAsync(account.Username, enableQuickLogin: false);
            var preserved = await service.GetStatusAsync(account.Username);
            Assert.True(preserved.IsEnabled);
            Assert.True(preserved.IsAvailableThisBoot);

            sessionContext.Clear();
            var result = await service.SignInWithPinAsync(account.Username, "123456");
            Assert.True(result.Succeeded);
            Assert.True(sessionContext.IsSignedIn);
            Assert.Equal(account.AccountId, sessionContext.Current?.AccountId);
            Assert.Equal(dataKey, sessionContext.Current?.DataKey);

            account.PasswordHash = RandomNumberGenerator.GetBytes(32);
            var invalidated = await service.GetStatusAsync(account.Username);
            Assert.True(invalidated.IsEnabled);
            Assert.False(invalidated.IsAvailableThisBoot);
            Assert.False(File.Exists(ticketPath));
        }
        finally
        {
            sessionContext.Clear();
            CryptographicOperations.ZeroMemory(dataKey);
            TryDelete(ticketPath);
        }
    }

    [Fact]
    public void Login_and_settings_views_expose_one_shared_quick_login_choice()
    {
        var root = ResolveRepositoryRoot();
        var login = File.ReadAllText(Path.Combine(root, "src", "Orderly.App", "Views", "LoginSignInPanel.xaml"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "Orderly.App", "Views", "Sections", "SettingsTabDataSecurity.xaml"));

        Assert.Contains("开机后允许快速登录（PIN / Windows Hello）", login);
        Assert.Contains("Visibility=\"{Binding ShouldShowQuickLoginOptIn", login);
        Assert.Contains("Visibility=\"{Binding IsWindowsHelloQuickLoginMode", login);
        Assert.Contains("Visibility=\"{Binding IsPinQuickLoginMode", login);
        Assert.Contains("使用 PIN", login);
        Assert.Contains("使用主密码", login);
        Assert.Contains("开机后允许快速登录（PIN / Windows Hello）", settings);
        Assert.Contains("IsChecked=\"{Binding Settings.QuickLoginEnabledInput}\"", settings);
        Assert.Contains("Command=\"{Binding RunDatabaseHealthCheckCommand}\"", settings);
    }

    [Fact]
    public void Pin_lock_view_exposes_windows_hello_unlock_and_restores_the_suspended_session_key()
    {
        var root = ResolveRepositoryRoot();
        var pinUnlockView = File.ReadAllText(Path.Combine(root, "src", "Orderly.App", "Views", "PinUnlockView.xaml"));
        var pinUnlockCode = File.ReadAllText(Path.Combine(root, "src", "Orderly.App", "Views", "PinUnlockView.xaml.cs"));
        var sessionLockCode = File.ReadAllText(Path.Combine(root, "src", "Orderly.App", "App.SessionLock.cs"));

        Assert.Contains("使用 Windows Hello 解锁", pinUnlockView);
        Assert.Contains("PinUnlockMethod.WindowsHello", pinUnlockCode);
        Assert.Contains("windowsHelloService.VerifyAsync", sessionLockCode);
        Assert.Contains("TryRestoreDataKey(session.AccountId)", sessionLockCode);
    }

    private static LocalAccount CreateAccount()
    {
        var accountId = Guid.NewGuid().ToString("N");
        return new LocalAccount
        {
            AccountId = accountId,
            Username = $"quick-{accountId}",
            DisplayName = "Quick Login Test",
            PasswordHash = RandomNumberGenerator.GetBytes(32),
            PinHash = RandomNumberGenerator.GetBytes(32),
            DatabasePath = DatabasePaths.GetExpectedAccountDatabasePath(accountId),
            IsEnabled = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private static LocalSessionContext CreateSession(LocalAccount account, byte[] dataKey) => new()
    {
        AccountId = account.AccountId,
        Username = account.Username,
        DisplayName = account.DisplayName,
        Role = account.Role,
        DatabasePath = account.DatabasePath,
        DataKey = dataKey,
        SignedInAt = DateTime.Now
    };

    private static string GetTicketPath(string accountId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountId));
        return Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), $"quick-login-{Convert.ToHexString(hash).ToLowerInvariant()}.dpapi");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orderly.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private sealed class AcceptingPinAuthService : ILocalAuthService
    {
        public Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
            => Task.FromResult(pin == "123456");

        public Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LegacyDatabaseMigrationPlan> BuildLegacyMigrationPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CreateFirstOwnerResult> CreateFirstOwnerAsync(CreateFirstOwnerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocalSignInResult> SignInAsync(string username, string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
