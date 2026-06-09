using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class LocalAuthService : ILocalAuthService
{
    private readonly ILocalAccountRepository _accountRepository;
    private readonly ILegacyDatabaseMigrationService _legacyMigrationService;
    private readonly ISessionContextService _sessionContextService;

    public LocalAuthService(
        ILocalAccountRepository accountRepository,
        ILegacyDatabaseMigrationService legacyMigrationService,
        ISessionContextService sessionContextService)
    {
        _accountRepository = accountRepository;
        _legacyMigrationService = legacyMigrationService;
        _sessionContextService = sessionContextService;
    }

    public async Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
    {
        return await _accountRepository.CountAsync(cancellationToken) > 0;
    }

    public Task<LegacyDatabaseMigrationPlan> BuildLegacyMigrationPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default)
    {
        return _legacyMigrationService.BuildPlanAsync(ownerAccountId, cancellationToken);
    }

    public async Task<CreateFirstOwnerResult> CreateFirstOwnerAsync(CreateFirstOwnerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOwnerRequest(request);

        if (await _accountRepository.CountAsync(cancellationToken) > 0)
        {
            throw new InvalidOperationException("本地账号已存在，不能重复执行首次 Owner 创建。");
        }

        var username = request.Username.Trim();
        var existing = await _accountRepository.GetByUsernameAsync(username, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException("用户名已存在。");
        }

        var now = DateTime.Now;
        var accountId = Guid.NewGuid().ToString("N");
        var databasePath = DatabasePaths.GetAccountDatabasePath(accountId);

        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = LocalCredentialSecurity.ComputeHash(request.MasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = LocalCredentialSecurity.ComputeHash(request.Pin, pinSalt, LocalCredentialSecurity.DefaultPinIterations);

        var recoveryKey = LocalCredentialSecurity.GenerateRecoveryKey();
        var normalizedRecoveryKey = LocalCredentialSecurity.NormalizeRecoveryKey(recoveryKey);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var recoveryHash = LocalCredentialSecurity.ComputeHash(normalizedRecoveryKey, recoverySalt, LocalCredentialSecurity.DefaultRecoveryIterations);

        var dataKey = RandomNumberGenerator.GetBytes(32);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrapped = ([], [], []);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) recoveryWrapped = ([], [], []);
        try
        {
            wrapped = LocalCredentialSecurity.WrapDataKey(request.MasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, dataKey);
            recoveryWrapped = LocalCredentialSecurity.WrapDataKey(normalizedRecoveryKey, recoverySalt, LocalCredentialSecurity.DefaultRecoveryIterations, dataKey);

            var account = new LocalAccount
            {
                AccountId = accountId,
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations,
                PinHash = pinHash,
                PinSalt = pinSalt,
                PinIterations = LocalCredentialSecurity.DefaultPinIterations,
                RecoveryKeyHash = recoveryHash,
                RecoveryKeySalt = recoverySalt,
                RecoveryKeyIterations = LocalCredentialSecurity.DefaultRecoveryIterations,
                RecoveryEncryptedDataKey = recoveryWrapped.Ciphertext,
                RecoveryDataKeyNonce = recoveryWrapped.Nonce,
                RecoveryDataKeyTag = recoveryWrapped.Tag,
                EncryptedDataKey = wrapped.Ciphertext,
                DataKeyNonce = wrapped.Nonce,
                DataKeyTag = wrapped.Tag,
                DatabasePath = databasePath,
                Role = LocalAccountRole.Owner,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                LastLoginAt = now
            };

            await _accountRepository.CreateAsync(account, cancellationToken);

            var migrationPlan = await _legacyMigrationService.BuildPlanAsync(accountId, cancellationToken);
            LegacyDatabaseMigrationResult? migrationResult = null;
            if (request.ImportLegacyDatabase && migrationPlan.LegacyDatabaseExists)
            {
                if (migrationPlan.State == LegacyDatabaseMigrationState.ReadyToCopy
                    || migrationPlan.State == LegacyDatabaseMigrationState.TargetAlreadyExists)
                {
                    migrationResult = await _legacyMigrationService.CopyAsync(
                        migrationPlan,
                        overwriteTarget: request.OverwriteTargetOnLegacyImport,
                        cancellationToken);
                }
            }

            var accountConnectionFactory = new SqliteConnectionFactory(databasePath);
            var initializer = new DatabaseInitializer(accountConnectionFactory);
            await initializer.InitializeAsync(cancellationToken);

            var session = CreateSession(account, dataKey, now);
            _sessionContextService.SetCurrent(session);
            return new CreateFirstOwnerResult
            {
                Session = session,
                RecoveryKey = recoveryKey,
                LegacyMigrationPlan = migrationPlan,
                LegacyMigrationResult = migrationResult
            };
        }
        finally
        {
            SensitiveBuffer.Clear(dataKey, passwordSalt, passwordHash, pinSalt, pinHash, recoverySalt, recoveryHash);
            SensitiveBuffer.ClearWrappedDataKey(wrapped);
            SensitiveBuffer.ClearWrappedDataKey(recoveryWrapped);
        }
    }

    public async Task<LocalSignInResult> SignInAsync(string username, string masterPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(masterPassword))
        {
            return LocalSignInResult.Failure("用户名和主密码不能为空。");
        }

        var account = await _accountRepository.GetByUsernameAsync(username.Trim(), cancellationToken);
        if (account is null)
        {
            return LocalSignInResult.Failure("账号不存在或主密码错误。");
        }

        if (!account.IsEnabled)
        {
            return LocalSignInResult.Failure("账号已被禁用。");
        }

        if (!LocalCredentialSecurity.VerifyHash(masterPassword, account.PasswordSalt, account.PasswordIterations, account.PasswordHash))
        {
            return LocalSignInResult.Failure("账号不存在或主密码错误。");
        }

        byte[] dataKey;
        try
        {
            dataKey = UnwrapDataKey(
                masterPassword,
                account.PasswordSalt,
                account.PasswordIterations,
                account.EncryptedDataKey,
                account.DataKeyNonce,
                account.DataKeyTag);
        }
        catch (CryptographicException)
        {
            return LocalSignInResult.Failure("主密码验证通过，但密钥解包失败。");
        }
        catch (InvalidOperationException)
        {
            return LocalSignInResult.Failure("主密码验证通过，但密钥解包失败。");
        }

        try
        {
            account.LastLoginAt = DateTime.Now;
            account.UpdatedAt = account.LastLoginAt.Value;
            await _accountRepository.UpdateAsync(account, cancellationToken);

            var session = CreateSession(account, dataKey, account.LastLoginAt.Value);
            _sessionContextService.SetCurrent(session);
            return LocalSignInResult.Success(session);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public async Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || !LocalCredentialSecurity.IsValidPin(pin))
        {
            return false;
        }

        var account = await _accountRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (account is null || !account.IsEnabled)
        {
            return false;
        }

        return LocalCredentialSecurity.VerifyHash(pin, account.PinSalt, account.PinIterations, account.PinHash);
    }

    public async Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(recoveryKey))
        {
            return false;
        }

        var account = await _accountRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (account is null || !account.IsEnabled)
        {
            return false;
        }

        if (!LocalCredentialSecurity.HasUsableHashParameters(account.RecoveryKeySalt, account.RecoveryKeyIterations, account.RecoveryKeyHash)
            || !LocalCredentialSecurity.HasUsableWrappedDataKey(account.RecoveryEncryptedDataKey, account.RecoveryDataKeyNonce, account.RecoveryDataKeyTag))
        {
            return false;
        }

        var normalizedRecoveryKey = LocalCredentialSecurity.NormalizeRecoveryKey(recoveryKey);
        return LocalCredentialSecurity.VerifyHash(normalizedRecoveryKey, account.RecoveryKeySalt, account.RecoveryKeyIterations, account.RecoveryKeyHash);
    }

    private static void ValidateOwnerRequest(CreateFirstOwnerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new InvalidOperationException("用户名不能为空。");
        }

        if (!MasterPasswordPolicy.TryValidate(request.MasterPassword, out var passwordValidationError))
        {
            throw new InvalidOperationException(passwordValidationError);
        }

        if (!LocalCredentialSecurity.IsValidPin(request.Pin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }
    }

    private static byte[] UnwrapDataKey(
        string masterPassword,
        byte[] passwordSalt,
        int iterations,
        byte[] encryptedDataKey,
        byte[] nonce,
        byte[] tag)
    {
        return LocalCredentialSecurity.UnwrapDataKey(
            masterPassword,
            passwordSalt,
            iterations,
            encryptedDataKey,
            nonce,
            tag);
    }

    private static LocalSessionContext CreateSession(LocalAccount account, byte[] dataKey, DateTime signedInAt)
    {
        return new LocalSessionContext
        {
            AccountId = account.AccountId,
            Username = account.Username,
            DisplayName = account.DisplayName,
            Role = account.Role,
            DatabasePath = account.DatabasePath,
            DataKey = dataKey.ToArray(),
            SignedInAt = signedInAt
        };
    }
}
