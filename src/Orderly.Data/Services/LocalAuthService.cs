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

    private readonly ILocalAccountRepository _accountRepository;
    private readonly CredentialAttemptTracker _credentialAttemptTracker;
    private readonly ILegacyDatabaseMigrationService _legacyMigrationService;
    private readonly ISessionContextService _sessionContextService;
    private readonly ISecurityAuditService _securityAudit;

    public LocalAuthService(
        ILocalAccountRepository accountRepository,
        ILegacyDatabaseMigrationService legacyMigrationService,
        ISessionContextService sessionContextService,
        CredentialAttemptTracker? credentialAttemptTracker = null,
        ISecurityAuditService? securityAuditService = null)
    {
        _accountRepository = accountRepository;
        _credentialAttemptTracker = credentialAttemptTracker ?? new CredentialAttemptTracker();
        _legacyMigrationService = legacyMigrationService;
        _sessionContextService = sessionContextService;
        _securityAudit = securityAuditService ?? new SecurityAuditService();
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
        var pinHash = LocalCredentialSecurity.ComputePinHash(
            request.Pin,
            pinSalt,
            LocalCredentialSecurity.DefaultPinIterations,
            LocalCredentialSecurity.CurrentCredentialFormatVersion);

        var recoveryKey = LocalCredentialSecurity.GenerateRecoveryKey();
        var normalizedRecoveryKey = LocalCredentialSecurity.NormalizeRecoveryKey(recoveryKey);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var recoveryHash = LocalCredentialSecurity.ComputeHash(normalizedRecoveryKey, recoverySalt, LocalCredentialSecurity.DefaultRecoveryIterations);

        var dataKey = RandomNumberGenerator.GetBytes(32);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrapped = ([], [], []);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) recoveryWrapped = ([], [], []);
        var accountCreated = false;
        LocalSessionContext? pendingSession = null;
        try
        {
            wrapped = LocalCredentialSecurity.WrapPasswordDataKey(request.MasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, dataKey);
            recoveryWrapped = LocalCredentialSecurity.WrapRecoveryDataKey(normalizedRecoveryKey, recoverySalt, LocalCredentialSecurity.DefaultRecoveryIterations, dataKey);

            var account = new LocalAccount
            {
                AccountId = accountId,
                Username = username,
                DisplayName = displayName,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations,
                PasswordKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion,
                PinHash = pinHash,
                PinSalt = pinSalt,
                PinIterations = LocalCredentialSecurity.DefaultPinIterations,
                PinHashVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion,
                RecoveryKeyHash = recoveryHash,
                RecoveryKeySalt = recoverySalt,
                RecoveryKeyIterations = LocalCredentialSecurity.DefaultRecoveryIterations,
                RecoveryKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion,
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
            accountCreated = true;

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

            pendingSession = CreateSession(account, dataKey, now);
            _sessionContextService.SetCurrent(pendingSession);
            await BackfillFirstOwnerSensitiveFieldsAsync(accountConnectionFactory, cancellationToken);
            CryptographicOperations.ZeroMemory(pendingSession.DataKey);

            return new CreateFirstOwnerResult
            {
                Session = pendingSession,
                RecoveryKey = recoveryKey,
                LegacyMigrationPlan = migrationPlan,
                LegacyMigrationResult = migrationResult
            };
        }
        catch
        {
            _sessionContextService.Clear();
            if (pendingSession?.DataKey is { Length: > 0 } pendingDataKey)
            {
                CryptographicOperations.ZeroMemory(pendingDataKey);
            }

            if (accountCreated)
            {
                await CleanupFailedFirstOwnerAsync(accountId, databasePath, cancellationToken);
            }

            throw;
        }
        finally
        {
            SensitiveBuffer.Clear(dataKey, passwordSalt, passwordHash, pinSalt, pinHash, recoverySalt, recoveryHash);
            SensitiveBuffer.ClearWrappedDataKey(wrapped);
            SensitiveBuffer.ClearWrappedDataKey(recoveryWrapped);
        }
    }

    private async Task BackfillFirstOwnerSensitiveFieldsAsync(
        SqliteConnectionFactory accountConnectionFactory,
        CancellationToken cancellationToken)
    {
        var fieldEncryptionService = new FieldEncryptionService(_sessionContextService);
        var sensitiveMigrationService = new SensitiveFieldMigrationService(accountConnectionFactory, fieldEncryptionService);
        await sensitiveMigrationService.BackfillAsync(cancellationToken);
    }

    private async Task CleanupFailedFirstOwnerAsync(
        string accountId,
        string databasePath,
        CancellationToken cancellationToken)
    {
        await _accountRepository.DeleteAsync(accountId, cancellationToken);
        DeleteExpectedAccountDatabaseFiles(accountId, databasePath);
    }

    private static void DeleteExpectedAccountDatabaseFiles(string accountId, string databasePath)
    {
        if (!DatabasePaths.IsExpectedAccountDatabasePath(accountId, databasePath))
        {
            return;
        }

        foreach (var path in new[] { databasePath, databasePath + "-journal", databasePath + "-wal", databasePath + "-shm" })
        {
            if (LocalDataFileSecurity.IsReparsePoint(path) || !File.Exists(path))
            {
                continue;
            }

            File.Delete(path);
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
                account.DataKeyTag,
                account.PasswordKeyVersion);
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
            UpgradePasswordAndRecoveryMaterialIfNeeded(account, masterPassword, dataKey);
            await _accountRepository.UpdateAsync(account, cancellationToken);

            var session = CreateSession(account, dataKey, account.LastLoginAt.Value);
            _sessionContextService.SetCurrent(session);
            CryptographicOperations.ZeroMemory(session.DataKey);
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

        var verified = LocalCredentialSecurity.VerifyPinHash(pin, account.PinSalt, account.PinIterations, account.PinHash, account.PinHashVersion);
        if (verified
            && _sessionContextService.IsSignedIn
            && !_sessionContextService.IsDataKeyAvailable)
        {
            verified = _sessionContextService.TryRestoreDataKey(normalizedAccountId);
        }

        if (verified)
        {
            await UpgradePinHashIfNeededAsync(account, pin, cancellationToken);
        }

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

        if (!LocalCredentialSecurity.TryNormalizeRecoveryKey(recoveryKey, out var normalizedRecoveryKey))
        {
            RecordCredentialFailure("recovery", normalizedAccountId);
            return false;
        }

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
        byte[] tag,
        int formatVersion)
    {
        return LocalCredentialSecurity.UnwrapPasswordDataKey(
            masterPassword,
            passwordSalt,
            iterations,
            encryptedDataKey,
            nonce,
            tag,
            formatVersion);
    }

    private static void UpgradePasswordAndRecoveryMaterialIfNeeded(LocalAccount account, string masterPassword, byte[] dataKey)
    {
        UpgradePasswordMaterialIfNeeded(account, masterPassword, dataKey);
        UpgradeRecoveryMaterialIfNeeded(account, dataKey);
    }

    private static void UpgradePasswordMaterialIfNeeded(LocalAccount account, string masterPassword, byte[] dataKey)
    {
        if (account.PasswordKeyVersion >= LocalCredentialSecurity.CurrentCredentialFormatVersion
            && account.PasswordIterations >= LocalCredentialSecurity.DefaultPasswordIterations)
        {
            return;
        }

        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = LocalCredentialSecurity.ComputeHash(masterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
        var wrapped = LocalCredentialSecurity.WrapPasswordDataKey(masterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, dataKey);
        SensitiveBuffer.Clear(account.PasswordSalt, account.PasswordHash);
        SensitiveBuffer.ClearWrappedDataKey((account.EncryptedDataKey, account.DataKeyNonce, account.DataKeyTag));

        account.PasswordSalt = passwordSalt;
        account.PasswordHash = passwordHash;
        account.PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations;
        account.PasswordKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion;
        account.EncryptedDataKey = wrapped.Ciphertext;
        account.DataKeyNonce = wrapped.Nonce;
        account.DataKeyTag = wrapped.Tag;
    }

    private static void UpgradeRecoveryMaterialIfNeeded(LocalAccount account, byte[] dataKey)
    {
        var hasRecoveryMaterial = LocalCredentialSecurity.HasUsableHashParameters(
                account.RecoveryKeySalt,
                account.RecoveryKeyIterations,
                account.RecoveryKeyHash)
            && LocalCredentialSecurity.HasUsableWrappedDataKey(
                account.RecoveryEncryptedDataKey,
                account.RecoveryDataKeyNonce,
                account.RecoveryDataKeyTag);
        if (!hasRecoveryMaterial)
        {
            account.RecoveryKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion;
            return;
        }

        if (account.RecoveryKeyVersion >= LocalCredentialSecurity.CurrentCredentialFormatVersion)
        {
            return;
        }

        var recoveryDataKey = LocalCredentialSecurity.UnwrapRecoveryDataKeyWithVerifierHash(
            account.RecoveryKeyHash,
            account.RecoveryEncryptedDataKey,
            account.RecoveryDataKeyNonce,
            account.RecoveryDataKeyTag,
            account.RecoveryKeyVersion);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(recoveryDataKey, dataKey))
            {
                throw new InvalidOperationException("Recovery Key 数据密钥与当前账号不匹配。");
            }

            var wrapped = LocalCredentialSecurity.WrapRecoveryDataKeyWithVerifierHash(account.RecoveryKeyHash, dataKey);
            SensitiveBuffer.ClearWrappedDataKey((account.RecoveryEncryptedDataKey, account.RecoveryDataKeyNonce, account.RecoveryDataKeyTag));
            account.RecoveryEncryptedDataKey = wrapped.Ciphertext;
            account.RecoveryDataKeyNonce = wrapped.Nonce;
            account.RecoveryDataKeyTag = wrapped.Tag;
            account.RecoveryKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion;
        }
        finally
        {
            SensitiveBuffer.Clear(recoveryDataKey);
        }
    }

    private async Task UpgradePinHashIfNeededAsync(LocalAccount account, string pin, CancellationToken cancellationToken)
    {
        if (account.PinHashVersion >= LocalCredentialSecurity.CurrentCredentialFormatVersion
            && account.PinIterations >= LocalCredentialSecurity.DefaultPinIterations)
        {
            return;
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = LocalCredentialSecurity.ComputePinHash(
            pin,
            pinSalt,
            LocalCredentialSecurity.DefaultPinIterations,
            LocalCredentialSecurity.CurrentCredentialFormatVersion);
        try
        {
            SensitiveBuffer.Clear(account.PinSalt, account.PinHash);
            account.PinSalt = pinSalt;
            account.PinHash = pinHash;
            account.PinIterations = LocalCredentialSecurity.DefaultPinIterations;
            account.PinHashVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion;
            account.UpdatedAt = DateTime.Now;
            await _accountRepository.UpdateAsync(account, cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(pinSalt, pinHash);
        }
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
        var blocked = _credentialAttemptTracker.IsBlocked(purpose, identifier);
        if (blocked)
        {
            // 安全敏感事件：账户/凭证因失败锁定被拒绝（防篡改审计，主体以哈希存储）。
            TryAudit(SecurityEventType.AccountLockout, identifier, SecurityEventOutcome.Locked);
        }

        return blocked;
    }

    private void RecordCredentialResult(string purpose, string identifier, bool success)
    {
        _credentialAttemptTracker.RecordResult(purpose, identifier, success);
        if (!success)
        {
            TryAudit(SecurityEventType.AuthenticationFailure, identifier, SecurityEventOutcome.Failure);
        }
    }

    private void RecordCredentialFailure(string purpose, string identifier)
    {
        _credentialAttemptTracker.RecordFailure(purpose, identifier);
        // 安全敏感事件：认证失败（防篡改审计，主体以哈希存储）。
        TryAudit(SecurityEventType.AuthenticationFailure, identifier, SecurityEventOutcome.Failure);
    }

    private void ClearCredentialFailures(string purpose, string identifier)
    {
        _credentialAttemptTracker.ClearFailures(purpose, identifier);
    }

    // 安全审计写入为"尽力而为"：绝不改变既有控制流与返回语义，任何异常都被吞掉。
    private void TryAudit(SecurityEventType eventType, string? subject, SecurityEventOutcome outcome)
    {
        try
        {
            _securityAudit.Record(eventType, subject, outcome);
        }
        catch
        {
            // 审计失败不得影响安全分支的原有行为。
        }
    }
}
