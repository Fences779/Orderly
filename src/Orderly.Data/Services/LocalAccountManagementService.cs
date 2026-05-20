using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class LocalAccountManagementService : ILocalAccountManagementService
{
    private const int DefaultPasswordIterations = 200000;
    private const int DefaultPinIterations = 200000;

    private readonly ILocalAccountRepository _accountRepository;
    private readonly ISessionContextService _sessionContextService;

    public LocalAccountManagementService(
        ILocalAccountRepository accountRepository,
        ISessionContextService sessionContextService)
    {
        _accountRepository = accountRepository;
        _sessionContextService = sessionContextService;
    }

    public async Task<IReadOnlyList<LocalAccountSummary>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        var session = RequireCurrentSession();
        var accounts = await _accountRepository.ListAsync(cancellationToken);
        IEnumerable<LocalAccount> filtered = session.Role == LocalAccountRole.Owner
            ? accounts
            : accounts.Where(account => string.Equals(account.AccountId, session.AccountId, StringComparison.OrdinalIgnoreCase));

        return MapSummaries(filtered);
    }

    public async Task<IReadOnlyList<LocalAccountSummary>> ListAccountDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepository.ListAsync(cancellationToken);
        return MapSummaries(accounts);
    }

    public async Task<LocalAccountSummary> CreateMemberAsync(CreateMemberAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerSession = RequireOwnerSession();
        return await CreateMemberInternalAsync(ownerSession.AccountId, ownerSession.DataKey, request, cancellationToken);
    }

    public async Task VerifyOwnerCredentialsAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        var (_, ownerDataKey) = await VerifyOwnerCredentialsInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        Array.Clear(ownerDataKey, 0, ownerDataKey.Length);
    }

    public async Task<LocalAccountSummary> CreateMemberWithOwnerVerificationAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CreateMemberAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (owner, ownerDataKey) = await VerifyOwnerCredentialsInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        try
        {
            return await CreateMemberInternalAsync(owner.AccountId, ownerDataKey, request, cancellationToken);
        }
        finally
        {
            Array.Clear(ownerDataKey, 0, ownerDataKey.Length);
        }
    }

    private async Task<(LocalAccount Owner, byte[] OwnerDataKey)> VerifyOwnerCredentialsInternalAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            throw new InvalidOperationException("管理员验证信息不完整。");
        }

        if (!IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            throw new InvalidOperationException("主账号不存在或不可用。");
        }

        if (!VerifyHash(ownerMasterPassword, owner.PasswordSalt, owner.PasswordIterations, owner.PasswordHash))
        {
            throw new InvalidOperationException("主账号主密码错误。");
        }

        if (!VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            throw new InvalidOperationException("主账号 PIN 错误。");
        }

        try
        {
            return (
                owner,
                UnwrapDataKey(
                    ownerMasterPassword,
                    owner.PasswordSalt,
                    owner.PasswordIterations,
                    owner.EncryptedDataKey,
                    owner.DataKeyNonce,
                    owner.DataKeyTag));
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("主账号密钥解包失败。");
        }
    }

    private async Task<LocalAccountSummary> CreateMemberInternalAsync(
        string ownerAccountId,
        byte[] ownerDataKey,
        CreateMemberAccountRequest request,
        CancellationToken cancellationToken)
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

        var username = request.Username.Trim();
        if (await _accountRepository.GetByUsernameAsync(username, cancellationToken) is not null)
        {
            throw new InvalidOperationException("用户名已存在。");
        }

        var now = DateTime.Now;
        var accountId = Guid.NewGuid().ToString("N");
        var memberDataKey = RandomNumberGenerator.GetBytes(32);
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = ComputeHash(request.MasterPassword, passwordSalt, DefaultPasswordIterations);
        var wrappedByPassword = WrapDataKey(request.MasterPassword, passwordSalt, DefaultPasswordIterations, memberDataKey);

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = ComputeHash(request.Pin, pinSalt, DefaultPinIterations);
        var wrappedByOwner = WrapDataKeyWithKey(ownerDataKey, memberDataKey);

        var member = new LocalAccount
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
            EncryptedDataKey = wrappedByPassword.Ciphertext,
            DataKeyNonce = wrappedByPassword.Nonce,
            DataKeyTag = wrappedByPassword.Tag,
            AdminOwnerAccountId = ownerAccountId,
            AdminEncryptedDataKey = wrappedByOwner.Ciphertext,
            AdminDataKeyNonce = wrappedByOwner.Nonce,
            AdminDataKeyTag = wrappedByOwner.Tag,
            DatabasePath = DatabasePaths.GetAccountDatabasePath(accountId),
            Role = LocalAccountRole.Member,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _accountRepository.CreateAsync(member, cancellationToken);

        var initializer = new DatabaseInitializer(new SqliteConnectionFactory(member.DatabasePath));
        await initializer.InitializeAsync(cancellationToken);

        return MapSummary(member);
    }

    public async Task DisableMemberAsync(string memberAccountId, CancellationToken cancellationToken = default)
    {
        RequireOwnerSession();
        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许禁用 Member 账号。");
        }

        member.IsEnabled = false;
        member.UpdatedAt = DateTime.Now;
        await _accountRepository.UpdateAsync(member, cancellationToken);
    }

    public async Task DeleteAccountAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string targetAccountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrWhiteSpace(targetAccountId))
        {
            throw new InvalidOperationException("删除账号所需的主账号验证信息不完整。");
        }

        if (!IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            throw new InvalidOperationException("主账号不存在或不可用。");
        }

        if (!VerifyHash(ownerMasterPassword, owner.PasswordSalt, owner.PasswordIterations, owner.PasswordHash))
        {
            throw new InvalidOperationException("主账号主密码错误。");
        }

        if (!VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            throw new InvalidOperationException("主账号 PIN 错误。");
        }

        var target = await GetAccountRequiredAsync(targetAccountId, cancellationToken);
        if (target.Role == LocalAccountRole.Owner)
        {
            throw new InvalidOperationException("主账号不允许从账户管理页删除。");
        }

        if (!string.Equals(target.AdminOwnerAccountId, owner.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("该账号不属于当前主账号，无法删除。");
        }

        await _accountRepository.DeleteAsync(target.AccountId, cancellationToken);
        DeleteAccountWorkspace(target.DatabasePath);
    }

    public async Task ChangeCurrentMasterPasswordAsync(string currentMasterPassword, string newMasterPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentMasterPassword))
        {
            throw new InvalidOperationException("当前主密码不能为空。");
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var session = RequireCurrentSession();
        var account = await GetAccountRequiredAsync(session.AccountId, cancellationToken);
        if (!account.IsEnabled)
        {
            throw new InvalidOperationException("当前账号已被禁用。");
        }

        if (!VerifyHash(currentMasterPassword, account.PasswordSalt, account.PasswordIterations, account.PasswordHash))
        {
            throw new InvalidOperationException("当前主密码错误。");
        }

        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
        var wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, session.DataKey);

        account.PasswordSalt = passwordSalt;
        account.PasswordHash = passwordHash;
        account.PasswordIterations = DefaultPasswordIterations;
        account.EncryptedDataKey = wrappedByPassword.Ciphertext;
        account.DataKeyNonce = wrappedByPassword.Nonce;
        account.DataKeyTag = wrappedByPassword.Tag;
        account.UpdatedAt = DateTime.Now;

        await _accountRepository.UpdateAsync(account, cancellationToken);
    }

    public async Task ChangeCurrentPinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default)
    {
        if (!IsValidPin(currentPin) || !IsValidPin(newPin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var session = RequireCurrentSession();
        var account = await GetAccountRequiredAsync(session.AccountId, cancellationToken);
        if (!account.IsEnabled)
        {
            throw new InvalidOperationException("当前账号已被禁用。");
        }

        if (!VerifyHash(currentPin, account.PinSalt, account.PinIterations, account.PinHash))
        {
            throw new InvalidOperationException("当前 PIN 错误。");
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        account.PinSalt = pinSalt;
        account.PinHash = ComputeHash(newPin, pinSalt, DefaultPinIterations);
        account.PinIterations = DefaultPinIterations;
        account.UpdatedAt = DateTime.Now;
        await _accountRepository.UpdateAsync(account, cancellationToken);
    }

    public async Task ResetMemberMasterPasswordAsync(string memberAccountId, string newMasterPassword, CancellationToken cancellationToken = default)
    {
        var ownerSession = RequireOwnerSession();
        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许重置 Member 主密码。");
        }

        var memberDataKey = UnwrapDataKeyWithKey(ownerSession.DataKey, member.AdminEncryptedDataKey, member.AdminDataKeyNonce, member.AdminDataKeyTag);
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
        var wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, memberDataKey);

        member.PasswordSalt = passwordSalt;
        member.PasswordHash = passwordHash;
        member.PasswordIterations = DefaultPasswordIterations;
        member.EncryptedDataKey = wrappedByPassword.Ciphertext;
        member.DataKeyNonce = wrappedByPassword.Nonce;
        member.DataKeyTag = wrappedByPassword.Tag;
        member.UpdatedAt = DateTime.Now;

        await _accountRepository.UpdateAsync(member, cancellationToken);
    }

    public async Task VerifyMemberPasswordResetAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        var (member, ownerDataKey) = await VerifyMemberPasswordResetInternalAsync(
            memberUsername,
            memberPin,
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        Array.Clear(ownerDataKey, 0, ownerDataKey.Length);
        GC.KeepAlive(member);
    }

    public async Task ResetMemberMasterPasswordWithOwnerVerificationAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberPin)
            || string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrEmpty(newMasterPassword))
        {
            throw new InvalidOperationException("重置 Member 主密码所需参数不完整。");
        }

        if (!IsValidPin(memberPin.Trim()) || !IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var (member, ownerDataKey) = await VerifyMemberPasswordResetInternalAsync(
            memberUsername,
            memberPin,
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        try
        {
            var memberDataKey = UnwrapDataKeyWithKey(ownerDataKey, member.AdminEncryptedDataKey, member.AdminDataKeyNonce, member.AdminDataKeyTag);
            var passwordSalt = RandomNumberGenerator.GetBytes(16);
            var passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
            var wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, memberDataKey);

            member.PasswordSalt = passwordSalt;
            member.PasswordHash = passwordHash;
            member.PasswordIterations = DefaultPasswordIterations;
            member.EncryptedDataKey = wrappedByPassword.Ciphertext;
            member.DataKeyNonce = wrappedByPassword.Nonce;
            member.DataKeyTag = wrappedByPassword.Tag;
            member.UpdatedAt = DateTime.Now;

            await _accountRepository.UpdateAsync(member, cancellationToken);
        }
        finally
        {
            Array.Clear(ownerDataKey, 0, ownerDataKey.Length);
        }
    }

    public async Task ResetMemberPinAsync(string memberAccountId, string newPin, CancellationToken cancellationToken = default)
    {
        RequireOwnerSession();
        if (!IsValidPin(newPin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许重置 Member PIN。");
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        member.PinSalt = pinSalt;
        member.PinHash = ComputeHash(newPin, pinSalt, DefaultPinIterations);
        member.PinIterations = DefaultPinIterations;
        member.UpdatedAt = DateTime.Now;
        await _accountRepository.UpdateAsync(member, cancellationToken);
    }

    public async Task ResetOwnerMasterPasswordWithRecoveryKeyAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        await VerifyOwnerPasswordRecoveryAsync(ownerUsername, ownerPin, recoveryKey, cancellationToken);

        if (string.IsNullOrEmpty(newMasterPassword))
        {
            throw new InvalidOperationException("恢复流程参数不完整。");
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken)
            ?? throw new InvalidOperationException("Owner 账号不存在或不可用。");

        var normalizedRecoveryKey = recoveryKey.Trim().ToUpperInvariant();
        var ownerDataKey = UnwrapDataKey(
            normalizedRecoveryKey,
            owner.RecoveryKeySalt,
            owner.RecoveryKeyIterations,
            owner.RecoveryEncryptedDataKey,
            owner.RecoveryDataKeyNonce,
            owner.RecoveryDataKeyTag);

        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
        var wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, ownerDataKey);

        owner.PasswordSalt = passwordSalt;
        owner.PasswordHash = passwordHash;
        owner.PasswordIterations = DefaultPasswordIterations;
        owner.EncryptedDataKey = wrappedByPassword.Ciphertext;
        owner.DataKeyNonce = wrappedByPassword.Nonce;
        owner.DataKeyTag = wrappedByPassword.Tag;
        owner.UpdatedAt = DateTime.Now;
        await _accountRepository.UpdateAsync(owner, cancellationToken);
    }

    public async Task VerifyOwnerPasswordRecoveryAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrWhiteSpace(recoveryKey))
        {
            throw new InvalidOperationException("恢复流程参数不完整。");
        }

        if (!IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            throw new InvalidOperationException("Owner 账号不存在或不可用。");
        }

        if (!VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            throw new InvalidOperationException("PIN 校验失败。");
        }

        var normalizedRecoveryKey = recoveryKey.Trim().ToUpperInvariant();
        if (!VerifyHash(normalizedRecoveryKey, owner.RecoveryKeySalt, owner.RecoveryKeyIterations, owner.RecoveryKeyHash))
        {
            throw new InvalidOperationException("Recovery Key 校验失败。");
        }

        if (owner.RecoveryEncryptedDataKey.Length == 0 || owner.RecoveryDataKeyNonce.Length == 0 || owner.RecoveryDataKeyTag.Length == 0)
        {
            throw new InvalidOperationException("Owner 账号缺少恢复密钥包裹的数据密钥，无法执行恢复。");
        }
    }

    private async Task<(LocalAccount Member, byte[] OwnerDataKey)> VerifyMemberPasswordResetInternalAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberPin)
            || string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            throw new InvalidOperationException("重置 Member 主密码所需参数不完整。");
        }

        if (!IsValidPin(memberPin.Trim()) || !IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var member = await _accountRepository.GetByUsernameAsync(memberUsername.Trim(), cancellationToken);
        if (member is null || member.Role != LocalAccountRole.Member || !member.IsEnabled)
        {
            throw new InvalidOperationException("成员账号不存在或不可用。");
        }

        if (!VerifyHash(memberPin.Trim(), member.PinSalt, member.PinIterations, member.PinHash))
        {
            throw new InvalidOperationException("成员账号 PIN 错误。");
        }

        var (owner, ownerDataKey) = await VerifyOwnerCredentialsInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        if (!string.Equals(member.AdminOwnerAccountId, owner.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            Array.Clear(ownerDataKey, 0, ownerDataKey.Length);
            throw new InvalidOperationException("该成员账号不属于当前主账号，无法重置主密码。");
        }

        return (member, ownerDataKey);
    }

    private LocalSessionContext RequireCurrentSession()
    {
        return _sessionContextService.Current ?? throw new InvalidOperationException("当前没有已登录会话。");
    }

    private LocalSessionContext RequireOwnerSession()
    {
        var session = RequireCurrentSession();
        if (session.Role != LocalAccountRole.Owner)
        {
            throw new UnauthorizedAccessException("仅 Owner 允许执行此操作。");
        }

        return session;
    }

    private async Task<LocalAccount> GetAccountRequiredAsync(string accountId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("账号标识不能为空。");
        }

        var account = await _accountRepository.GetByAccountIdAsync(accountId.Trim(), cancellationToken);
        return account ?? throw new InvalidOperationException("目标账号不存在。");
    }

    private static LocalAccountSummary MapSummary(LocalAccount account)
    {
        return MapSummary(account, isMostRecentlyLoggedIn: false);
    }

    private static LocalAccountSummary MapSummary(LocalAccount account, bool isMostRecentlyLoggedIn)
    {
        return new LocalAccountSummary
        {
            AccountId = account.AccountId,
            Username = account.Username,
            DisplayName = account.DisplayName,
            Role = account.Role,
            IsEnabled = account.IsEnabled,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt,
            IsMostRecentlyLoggedIn = isMostRecentlyLoggedIn
        };
    }

    private static IReadOnlyList<LocalAccountSummary> MapSummaries(IEnumerable<LocalAccount> accounts)
    {
        var orderedAccounts = accounts
            .OrderBy(account => account.CreatedAt)
            .ToList();
        var mostRecentlyLoggedInAccountId = orderedAccounts
            .Where(account => account.LastLoginAt.HasValue)
            .OrderByDescending(account => account.LastLoginAt)
            .ThenByDescending(account => account.UpdatedAt)
            .Select(account => account.AccountId)
            .FirstOrDefault();

        return orderedAccounts
            .Select(account => MapSummary(
                account,
                !string.IsNullOrWhiteSpace(mostRecentlyLoggedInAccountId)
                && string.Equals(account.AccountId, mostRecentlyLoggedInAccountId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
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
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static void DeleteAccountWorkspace(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        var accountsRoot = Path.GetFullPath(DatabasePaths.GetAccountsDirectoryPath());
        var targetDirectory = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(targetDirectory)
            || !targetDirectory.StartsWith(accountsRoot, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(targetDirectory))
        {
            return;
        }

        Directory.Delete(targetDirectory, recursive: true);
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKey(string secret, byte[] salt, int iterations, byte[] dataKey)
    {
        var key = ComputeHash(secret, salt, iterations);
        return WrapDataKeyWithKey(key, dataKey);
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKeyWithKey(byte[] key, byte[] dataKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, dataKey, ciphertext, tag);
        return (ciphertext, nonce, tag);
    }

    private static byte[] UnwrapDataKey(string secret, byte[] salt, int iterations, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var key = ComputeHash(secret, salt, iterations);
        return UnwrapDataKeyWithKey(key, ciphertext, nonce, tag);
    }

    private static byte[] UnwrapDataKeyWithKey(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        if (ciphertext.Length == 0 || nonce.Length == 0 || tag.Length == 0)
        {
            throw new InvalidOperationException("账号缺少可用的数据密钥包裹信息。");
        }

        var dataKey = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, dataKey);
        return dataKey;
    }
}
