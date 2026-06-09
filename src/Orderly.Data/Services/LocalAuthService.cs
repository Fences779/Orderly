using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class LocalAuthService : ILocalAuthService
{
    private const int DefaultPasswordIterations = 200000;
    private const int DefaultPinIterations = 200000;
    private const int DefaultRecoveryIterations = 200000;

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
        var passwordHash = ComputeHash(request.MasterPassword, passwordSalt, DefaultPasswordIterations);

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = ComputeHash(request.Pin, pinSalt, DefaultPinIterations);

        var recoveryKey = GenerateRecoveryKey();
        var normalizedRecoveryKey = NormalizeRecoveryKey(recoveryKey);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var recoveryHash = ComputeHash(normalizedRecoveryKey, recoverySalt, DefaultRecoveryIterations);

        var dataKey = RandomNumberGenerator.GetBytes(32);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrapped = ([], [], []);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) recoveryWrapped = ([], [], []);
        try
        {
            wrapped = WrapDataKey(request.MasterPassword, passwordSalt, DefaultPasswordIterations, dataKey);
            recoveryWrapped = WrapDataKey(normalizedRecoveryKey, recoverySalt, DefaultRecoveryIterations, dataKey);

            var account = new LocalAccount
            {
                AccountId = accountId,
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                PasswordIterations = DefaultPasswordIterations,
                PinHash = pinHash,
                PinSalt = pinSalt,
                PinIterations = DefaultPinIterations,
                RecoveryKeyHash = recoveryHash,
                RecoveryKeySalt = recoverySalt,
                RecoveryKeyIterations = DefaultRecoveryIterations,
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

        if (!VerifyHash(masterPassword, account.PasswordSalt, account.PasswordIterations, account.PasswordHash))
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
        if (string.IsNullOrWhiteSpace(accountId) || !IsValidPin(pin))
        {
            return false;
        }

        var account = await _accountRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (account is null || !account.IsEnabled)
        {
            return false;
        }

        return VerifyHash(pin, account.PinSalt, account.PinIterations, account.PinHash);
    }

    public async Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(recoveryKey))
        {
            return false;
        }

        var account = await _accountRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        if (account.RecoveryKeyIterations <= 0
            || account.RecoveryKeySalt.Length == 0
            || account.RecoveryKeyHash.Length == 0)
        {
            return false;
        }

        var normalizedRecoveryKey = NormalizeRecoveryKey(recoveryKey);
        return VerifyHash(normalizedRecoveryKey, account.RecoveryKeySalt, account.RecoveryKeyIterations, account.RecoveryKeyHash);
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

        if (!IsValidPin(request.Pin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }
    }

    private static bool IsValidPin(string pin)
    {
        return pin.Length == 6 && pin.All(char.IsDigit);
    }

    private static byte[] ComputeHash(string value, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, 32);
    }

    private static bool VerifyHash(string value, byte[] salt, int iterations, byte[] expectedHash)
    {
        var actualHash = ComputeHash(value, salt, iterations);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKey(string masterPassword, byte[] passwordSalt, int iterations, byte[] dataKey)
    {
        var key = ComputeHash(masterPassword, passwordSalt, iterations);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[16];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, dataKey, ciphertext, tag);
            return (ciphertext, nonce, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
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
        var key = ComputeHash(masterPassword, passwordSalt, iterations);
        var dataKey = new byte[encryptedDataKey.Length];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, encryptedDataKey, tag, dataKey);
            return dataKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string GenerateRecoveryKey()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return string.Join("-", Enumerable.Range(0, raw.Length / 4).Select(index => raw.Substring(index * 4, 4)));
    }

    private static string NormalizeRecoveryKey(string recoveryKey)
    {
        return recoveryKey.Trim().ToUpperInvariant();
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
