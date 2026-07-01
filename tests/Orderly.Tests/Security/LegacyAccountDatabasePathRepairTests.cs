using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

public sealed class LegacyAccountDatabasePathRepairTests
{
    private const string MasterPassword = "RepairPass123!";
    private const string Pin = "123456";

    [Fact]
    public async Task Master_password_sign_in_repairs_the_current_windows_legacy_database_path()
    {
        var account = CreateAccount(DatabasePaths.GetLegacyDatabasePath());
        var expectedPath = PrepareExpectedAccountDatabase(account.AccountId);
        var repository = new InMemoryLocalAccountRepository([account]);
        var sessionContext = new SessionContextService();
        var service = new LocalAuthService(repository, new NoOpLegacyDatabaseMigrationService(), sessionContext);

        try
        {
            var result = await service.SignInAsync(account.Username, MasterPassword);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Session);
            Assert.Equal(expectedPath, account.DatabasePath);
            Assert.Equal(expectedPath, result.Session!.DatabasePath);
            Assert.Equal(account.AccountId, sessionContext.Current?.AccountId);
        }
        finally
        {
            sessionContext.Clear();
            DeleteAccountWorkspace(account.AccountId);
        }
    }

    [Fact]
    public async Task Master_password_sign_in_repairs_the_current_windows_legacy_account_workspace_path()
    {
        var accountId = Guid.NewGuid().ToString("N");
        var account = CreateAccount(accountId, GetCurrentWindowsLegacyAccountDatabasePath(accountId));
        var expectedPath = PrepareExpectedAccountDatabase(account.AccountId);
        var repository = new InMemoryLocalAccountRepository([account]);
        var sessionContext = new SessionContextService();
        var service = new LocalAuthService(repository, new NoOpLegacyDatabaseMigrationService(), sessionContext);

        try
        {
            var result = await service.SignInAsync(account.Username, MasterPassword);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Session);
            Assert.Equal(expectedPath, account.DatabasePath);
            Assert.Equal(expectedPath, result.Session!.DatabasePath);
            Assert.Equal(account.AccountId, sessionContext.Current?.AccountId);
        }
        finally
        {
            sessionContext.Clear();
            DeleteAccountWorkspace(account.AccountId);
        }
    }

    [Fact]
    public async Task Master_password_sign_in_keeps_rejecting_a_foreign_windows_users_legacy_path()
    {
        var foreignPath = $@"Z:\ForeignProfile\AppData\Local\OrderlyData\orderly.db";
        var account = CreateAccount(foreignPath);
        PrepareExpectedAccountDatabase(account.AccountId);
        var repository = new InMemoryLocalAccountRepository([account]);
        var service = new LocalAuthService(repository, new NoOpLegacyDatabaseMigrationService(), new SessionContextService());

        try
        {
            var result = await service.SignInAsync(account.Username, MasterPassword);

            Assert.False(result.Succeeded);
            Assert.Equal("账号数据路径异常，已拒绝登录。", result.ErrorMessage);
            Assert.Equal(foreignPath, account.DatabasePath);
        }
        finally
        {
            DeleteAccountWorkspace(account.AccountId);
        }
    }

    [Fact]
    public async Task Master_password_sign_in_keeps_rejecting_a_foreign_windows_users_legacy_account_workspace_path()
    {
        var accountId = Guid.NewGuid().ToString("N");
        var foreignPath = $@"Z:\ForeignProfile\AppData\Local\Orderly\accounts\{accountId}\orderly.db";
        var account = CreateAccount(accountId, foreignPath);
        PrepareExpectedAccountDatabase(account.AccountId);
        var repository = new InMemoryLocalAccountRepository([account]);
        var service = new LocalAuthService(repository, new NoOpLegacyDatabaseMigrationService(), new SessionContextService());

        try
        {
            var result = await service.SignInAsync(account.Username, MasterPassword);

            Assert.False(result.Succeeded);
            Assert.Equal("账号数据路径异常，已拒绝登录。", result.ErrorMessage);
            Assert.Equal(foreignPath, account.DatabasePath);
        }
        finally
        {
            DeleteAccountWorkspace(account.AccountId);
        }
    }

    [Fact]
    public async Task Quick_login_repairs_the_current_windows_legacy_database_path_and_still_signs_in()
    {
        var account = CreateAccount(DatabasePaths.GetLegacyDatabasePath(), quickLoginEnabled: false);
        var expectedPath = PrepareExpectedAccountDatabase(account.AccountId);
        var repository = new InMemoryLocalAccountRepository([account]);
        var sessionContext = new SessionContextService();
        var authService = new LocalAuthService(repository, new NoOpLegacyDatabaseMigrationService(), sessionContext);
        var service = new QuickLoginService(repository, sessionContext, authService);
        var ticketPath = GetTicketPath(account.AccountId);
        var dataKey = RandomNumberGenerator.GetBytes(32);

        try
        {
            sessionContext.SetCurrent(new LocalSessionContext
            {
                AccountId = account.AccountId,
                Username = account.Username,
                DisplayName = account.DisplayName,
                Role = account.Role,
                DatabasePath = expectedPath,
                DataKey = dataKey,
                SignedInAt = DateTime.Now
            });

            await service.SetEnabledForCurrentAccountAsync(true);
            var status = await service.GetStatusAsync(account.Username);
            sessionContext.Clear();
            var result = await service.SignInWithPinAsync(account.Username, Pin);

            Assert.True(status.IsEnabled);
            Assert.True(status.IsAvailableThisBoot);
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Session);
            Assert.Equal(expectedPath, account.DatabasePath);
            Assert.Equal(expectedPath, result.Session!.DatabasePath);
        }
        finally
        {
            sessionContext.Clear();
            CryptographicOperations.ZeroMemory(dataKey);
            TryDeleteFile(ticketPath);
            DeleteAccountWorkspace(account.AccountId);
        }
    }

    private static LocalAccount CreateAccount(string databasePath, bool quickLoginEnabled = false)
    {
        return CreateAccount(Guid.NewGuid().ToString("N"), databasePath, quickLoginEnabled);
    }

    private static LocalAccount CreateAccount(string accountId, string databasePath, bool quickLoginEnabled = false)
    {
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = LocalCredentialSecurity.ComputeHash(MasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = LocalCredentialSecurity.ComputePinHash(
            Pin,
            pinSalt,
            LocalCredentialSecurity.DefaultPinIterations,
            LocalCredentialSecurity.CurrentCredentialFormatVersion);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = LocalCredentialSecurity.WrapPasswordDataKey(
            MasterPassword,
            passwordSalt,
            LocalCredentialSecurity.DefaultPasswordIterations,
            dataKey);

        return new LocalAccount
        {
            AccountId = accountId,
            Username = $"repair-{accountId[..8]}",
            DisplayName = "Repair Test",
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations,
            PasswordKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion,
            PinHash = pinHash,
            PinSalt = pinSalt,
            PinIterations = LocalCredentialSecurity.DefaultPinIterations,
            PinHashVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion,
            EncryptedDataKey = wrapped.Ciphertext,
            DataKeyNonce = wrapped.Nonce,
            DataKeyTag = wrapped.Tag,
            DatabasePath = databasePath,
            Role = LocalAccountRole.Owner,
            IsEnabled = true,
            QuickLoginEnabled = quickLoginEnabled,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private static string GetCurrentWindowsLegacyAccountDatabasePath(string accountId)
    {
        return Path.Combine(
            DatabasePaths.GetLegacyAppRootPath(),
            "accounts",
            accountId,
            "orderly.db");
    }

    private static string PrepareExpectedAccountDatabase(string accountId)
    {
        var path = DatabasePaths.GetExpectedAccountDatabasePath(accountId);
        var directory = Path.GetDirectoryName(path);
        Assert.False(string.IsNullOrWhiteSpace(directory));
        Directory.CreateDirectory(directory!);
        if (!File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        return path;
    }

    private static void DeleteAccountWorkspace(string accountId)
    {
        var directory = DatabasePaths.GetExpectedAccountDirectoryPath(accountId);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetTicketPath(string accountId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountId));
        return Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), $"quick-login-{Convert.ToHexString(hash).ToLowerInvariant()}.dpapi");
    }

    private static void TryDeleteFile(string path)
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
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class NoOpLegacyDatabaseMigrationService : ILegacyDatabaseMigrationService
    {
        public Task<LegacyDatabaseMigrationPlan> BuildPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LegacyDatabaseMigrationResult> CopyAsync(LegacyDatabaseMigrationPlan plan, bool overwriteTarget, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
