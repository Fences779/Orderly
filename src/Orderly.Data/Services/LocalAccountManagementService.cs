using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class LocalAccountManagementService : ILocalAccountManagementService
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
            CryptographicOperations.ZeroMemory(ownerDataKey);
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
        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = ComputeHash(request.Pin, pinSalt, DefaultPinIterations);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByPassword = ([], [], []);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByOwner = ([], [], []);

        try
        {
            wrappedByPassword = WrapDataKey(request.MasterPassword, passwordSalt, DefaultPasswordIterations, memberDataKey);
            wrappedByOwner = WrapDataKeyWithKey(ownerDataKey, memberDataKey);

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
        finally
        {
            SensitiveBuffer.Clear(memberDataKey, passwordSalt, passwordHash, pinSalt, pinHash);
            SensitiveBuffer.ClearWrappedDataKey(wrappedByPassword);
            SensitiveBuffer.ClearWrappedDataKey(wrappedByOwner);
        }
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
}
