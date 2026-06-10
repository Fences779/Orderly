using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class LocalAuthService : ILocalAuthService
{
    private const string GenericSignInFailureMessage = "账号不存在或主密码错误。";
    private const int MaxCredentialFailuresBeforeCooldown = 5;
    private static readonly TimeSpan CredentialFailureCooldown = TimeSpan.FromMinutes(5);

    private readonly ILocalAccountRepository _accountRepository;
    private readonly ILegacyDatabaseMigrationService _legacyMigrationService;
    private readonly ISessionContextService _sessionContextService;
    private readonly object _credentialAttemptsLock = new();
    private readonly Dictionary<string, CredentialAttemptState> _credentialAttempts = new(StringComparer.Ordinal);

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

        var username = LocalCredentialSecurity.NormalizeAccountUsername(request.Username);
        var displayName = LocalCredentialSecurity.NormalizeAccountDisplayName(request.DisplayName, username);
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
                DisplayName = displayName,
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
        if (!LocalCredentialSecurity.TryNormalizeAccountUsername(username, out var normalizedUsername)
            || string.IsNullOrWhiteSpace(masterPassword))
        {
            return LocalSignInResult.Failure("用户名和主密码不能为空。");
        }

        if (IsCredentialAttemptBlocked("signin", normalizedUsername))
        {
            return LocalSignInResult.Failure("认证尝试过于频繁，请稍后再试。");
        }

        var account = await _accountRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
        if (account is null)
        {
            RecordCredentialFailure("signin", normalizedUsername);
            return LocalSignInResult.Failure(GenericSignInFailureMessage);
        }

        if (!account.IsEnabled)
        {
            RecordCredentialFailure("signin", normalizedUsername);
            return LocalSignInResult.Failure(GenericSignInFailureMessage);
        }

        if (!LocalCredentialSecurity.VerifyHash(masterPassword, account.PasswordSalt, account.PasswordIterations, account.PasswordHash))
        {
            RecordCredentialFailure("signin", normalizedUsername);
            return LocalSignInResult.Failure(GenericSignInFailureMessage);
        }

        if (!IsAccountDatabasePathSafe(account))
        {
            return LocalSignInResult.Failure("账号数据路径异常，已拒绝登录。");
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
            RecordCredentialFailure("signin", normalizedUsername);
            return LocalSignInResult.Failure(GenericSignInFailureMessage);
        }
        catch (InvalidOperationException)
        {
            RecordCredentialFailure("signin", normalizedUsername);
            return LocalSignInResult.Failure(GenericSignInFailureMessage);
        }

        try
        {
            account.LastLoginAt = DateTime.Now;
            account.UpdatedAt = account.LastLoginAt.Value;
            await _accountRepository.UpdateAsync(account, cancellationToken);

            var session = CreateSession(account, dataKey, account.LastLoginAt.Value);
            _sessionContextService.SetCurrent(session);
            ClearCredentialFailures("signin", normalizedUsername);
            return LocalSignInResult.Success(session);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public async Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
    {
        if (!LocalCredentialSecurity.TryNormalizeAccountId(accountId, out var normalizedAccountId))
        {
            return false;
        }

        if (IsCredentialAttemptBlocked("pin", normalizedAccountId))
        {
            return false;
        }

        if (!LocalCredentialSecurity.IsValidPin(pin))
        {
            RecordCredentialFailure("pin", normalizedAccountId);
            return false;
        }

        var account = await _accountRepository.GetByAccountIdAsync(normalizedAccountId, cancellationToken);
        if (account is null || !IsAccountUsableForCredentialCheck(account))
        {
            RecordCredentialFailure("pin", normalizedAccountId);
            return false;
        }

        var verified = LocalCredentialSecurity.VerifyHash(pin, account.PinSalt, account.PinIterations, account.PinHash);
        RecordCredentialResult("pin", normalizedAccountId, verified);
        return verified;
    }

    public async Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default)
    {
        if (!LocalCredentialSecurity.TryNormalizeAccountId(accountId, out var normalizedAccountId))
        {
            return false;
        }

        if (IsCredentialAttemptBlocked("recovery", normalizedAccountId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(recoveryKey))
        {
            RecordCredentialFailure("recovery", normalizedAccountId);
            return false;
        }

        var account = await _accountRepository.GetByAccountIdAsync(normalizedAccountId, cancellationToken);
        if (account is null || !IsAccountUsableForCredentialCheck(account))
        {
            RecordCredentialFailure("recovery", normalizedAccountId);
            return false;
        }

        if (!LocalCredentialSecurity.HasUsableHashParameters(account.RecoveryKeySalt, account.RecoveryKeyIterations, account.RecoveryKeyHash)
            || !LocalCredentialSecurity.HasUsableWrappedDataKey(account.RecoveryEncryptedDataKey, account.RecoveryDataKeyNonce, account.RecoveryDataKeyTag))
        {
            RecordCredentialFailure("recovery", normalizedAccountId);
            return false;
        }

        var normalizedRecoveryKey = LocalCredentialSecurity.NormalizeRecoveryKey(recoveryKey);
        var verified = LocalCredentialSecurity.VerifyHash(normalizedRecoveryKey, account.RecoveryKeySalt, account.RecoveryKeyIterations, account.RecoveryKeyHash);
        RecordCredentialResult("recovery", normalizedAccountId, verified);
        return verified;
    }

    private static void ValidateOwnerRequest(CreateFirstOwnerRequest request)
    {
        var normalizedUsername = LocalCredentialSecurity.NormalizeAccountUsername(request.Username);
        _ = LocalCredentialSecurity.NormalizeAccountDisplayName(request.DisplayName, normalizedUsername);

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
            DatabasePath = DatabasePaths.GetExpectedAccountDatabasePath(account.AccountId),
            DataKey = dataKey.ToArray(),
            SignedInAt = signedInAt
        };
    }

    private static bool IsAccountUsableForCredentialCheck(LocalAccount account)
    {
        return account.IsEnabled && IsAccountDatabasePathSafe(account);
    }

    private static bool IsAccountDatabasePathSafe(LocalAccount account)
    {
        try
        {
            if (!DatabasePaths.IsExpectedAccountDatabasePath(account.AccountId, account.DatabasePath)
                || LocalDataFileSecurity.IsReparsePoint(account.DatabasePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(account.DatabasePath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directory, "账号数据目录");
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsCredentialAttemptBlocked(string purpose, string identifier)
    {
        var key = BuildCredentialAttemptKey(purpose, identifier);
        var now = DateTimeOffset.UtcNow;
        lock (_credentialAttemptsLock)
        {
            if (!_credentialAttempts.TryGetValue(key, out var state))
            {
                return false;
            }

            if (state.LockedUntil > now)
            {
                return true;
            }

            if (state.LockedUntil != default)
            {
                _credentialAttempts.Remove(key);
            }

            return false;
        }
    }

    private void RecordCredentialResult(string purpose, string identifier, bool success)
    {
        if (success)
        {
            ClearCredentialFailures(purpose, identifier);
            return;
        }

        RecordCredentialFailure(purpose, identifier);
    }

    private void RecordCredentialFailure(string purpose, string identifier)
    {
        var key = BuildCredentialAttemptKey(purpose, identifier);
        var now = DateTimeOffset.UtcNow;
        lock (_credentialAttemptsLock)
        {
            if (_credentialAttempts.TryGetValue(key, out var state) && state.LockedUntil > now)
            {
                return;
            }

            if (state is null || state.LockedUntil != default)
            {
                state = new CredentialAttemptState();
                _credentialAttempts[key] = state;
            }

            state.FailedCount++;
            if (state.FailedCount >= MaxCredentialFailuresBeforeCooldown)
            {
                state.FailedCount = 0;
                state.LockedUntil = now.Add(CredentialFailureCooldown);
            }
        }
    }

    private void ClearCredentialFailures(string purpose, string identifier)
    {
        var key = BuildCredentialAttemptKey(purpose, identifier);
        lock (_credentialAttemptsLock)
        {
            _credentialAttempts.Remove(key);
        }
    }

    private static string BuildCredentialAttemptKey(string purpose, string identifier)
    {
        return purpose + ":" + identifier.Trim().ToLowerInvariant();
    }

    private sealed class CredentialAttemptState
    {
        public int FailedCount { get; set; }

        public DateTimeOffset LockedUntil { get; set; }
    }
}
