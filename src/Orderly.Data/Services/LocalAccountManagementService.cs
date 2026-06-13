using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class LocalAccountManagementService : ILocalAccountManagementService
{
    private readonly ILocalAccountRepository _accountRepository;
    private readonly CredentialAttemptTracker _credentialAttemptTracker;
    private readonly ISessionContextService _sessionContextService;
    private readonly ISecurityAuditService _securityAudit;
    private readonly IAbuseDetectionService _abuseDetection;

    public LocalAccountManagementService(
        ILocalAccountRepository accountRepository,
        ISessionContextService sessionContextService,
        CredentialAttemptTracker? credentialAttemptTracker = null,
        ISecurityAuditService? securityAuditService = null,
        IAbuseDetectionService? abuseDetectionService = null)
    {
        _accountRepository = accountRepository;
        _credentialAttemptTracker = credentialAttemptTracker ?? new CredentialAttemptTracker();
        _sessionContextService = sessionContextService;
        _securityAudit = securityAuditService ?? new SecurityAuditService();
        _abuseDetection = abuseDetectionService ?? new DefaultAbuseDetectionService();
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
        ThrowIfUnauthenticatedDirectoryReadThrottled();
        // 跨事件埋点：未认证目录读取属于跨账户枚举信号，汇入跨事件聚合检测。
        TryObserveAbuse(AbuseSignalKind.CrossAccountEnumeration, subject: null);
        var accounts = await _accountRepository.ListAsync(cancellationToken);
        return MapUnauthenticatedDirectorySummaries(accounts);
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
        if (!MasterPasswordPolicy.TryValidate(request.MasterPassword, out var passwordValidationError))
        {
            throw new InvalidOperationException(passwordValidationError);
        }

        if (!LocalCredentialSecurity.IsValidPin(request.Pin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var username = LocalCredentialSecurity.NormalizeAccountUsername(request.Username);
        var displayName = LocalCredentialSecurity.NormalizeAccountDisplayName(request.DisplayName, username);
        if (await _accountRepository.GetByUsernameAsync(username, cancellationToken) is not null)
        {
            throw new InvalidOperationException("用户名已存在。");
        }

        var now = DateTime.Now;
        var accountId = Guid.NewGuid().ToString("N");
        var memberDataKey = RandomNumberGenerator.GetBytes(32);
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = LocalCredentialSecurity.ComputeHash(request.MasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = LocalCredentialSecurity.ComputePinHash(
            request.Pin,
            pinSalt,
            LocalCredentialSecurity.DefaultPinIterations,
            LocalCredentialSecurity.CurrentCredentialFormatVersion);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByPassword = ([], [], []);
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByOwner = ([], [], []);

        try
        {
            wrappedByPassword = LocalCredentialSecurity.WrapPasswordDataKey(request.MasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, memberDataKey);
            wrappedByOwner = WrapDataKeyWithKey(ownerDataKey, memberDataKey);

            var member = new LocalAccount
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
                RecoveryKeyVersion = LocalCredentialSecurity.CurrentCredentialFormatVersion,
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

            var initializer = new DatabaseInitializer(new SqliteConnectionFactory(member.DatabasePath, () => memberDataKey.ToArray()));
            await initializer.InitializeAsync(cancellationToken);

            // 安全审计（BC-6）：成员创建成功恰好记录一条 MemberCreated（账号标签 + 脱敏元数据，绝不含明文凭证）。
            await TryAuditAsync(SecurityAuditEventKind.MemberCreated, accountId, "member-created", cancellationToken);

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
        var ownerSession = RequireOwnerSession();
        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许禁用 Member 账号。");
        }

        if (!string.Equals(member.AdminOwnerAccountId, ownerSession.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            // 跨事件埋点：对不属于当前主账号的账号发起禁用操作 = 跨账户操作探测。
            TryObserveAbuse(AbuseSignalKind.CrossAccountOperationProbe, ownerSession.AccountId);
            throw new InvalidOperationException("该账号不属于当前主账号，无法禁用。");
        }

        member.IsEnabled = false;
        member.UpdatedAt = DateTime.Now;
        await _accountRepository.UpdateAsync(member, cancellationToken);

        // 安全审计（BC-6）：成员停用成功恰好记录一条 MemberDisabled（账号标签 + 脱敏元数据，绝不含明文凭证）。
        await TryAuditAsync(SecurityAuditEventKind.MemberDisabled, member.AccountId, "member-disabled", cancellationToken);
    }

    public async Task DeleteMemberAsync(string memberAccountId, CancellationToken cancellationToken = default)
    {
        // 服务层权限双重校验之一：当前会话必须为 Owner；非 Owner 由 RequireOwnerSession 记越权审计并拒绝，不执行任何后端操作。
        var ownerSession = RequireOwnerSession();
        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);

        // 服务层权限双重校验之二：经成员管理权限矩阵纯函数判定（仅 Owner 且目标非自身）。
        // 任何人不可删自身（含 Owner 不可删自身）。被拒绝时直接抛出，不执行任何后端操作。
        if (!MemberManagementPolicy.CanDeleteMember(ownerSession.Role, ownerSession.AccountId, member.AccountId))
        {
            throw new InvalidOperationException("当前账号无权删除该成员（不可删除自身）。");
        }

        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许删除 Member 账号。");
        }

        if (!string.Equals(member.AdminOwnerAccountId, ownerSession.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            // 跨事件埋点：对不属于当前主账号的账号发起删除操作 = 跨账户操作探测。
            TryObserveAbuse(AbuseSignalKind.CrossAccountOperationProbe, ownerSession.AccountId);
            throw new InvalidOperationException("该账号不属于当前主账号，无法删除。");
        }

        // 删除仅移除登录账号记录（LocalAccount），保留其名下全部历史业务数据：
        // 不级联删除业务数据工作区（区别于 DeleteAccountAsync）、不匿名化；
        // 业务数据的来源 / 创建人仍展示该（已删除）账号标签 / 标识用于归属展示（需求 7.10）。
        await _accountRepository.DeleteAsync(member.AccountId, cancellationToken);

        // 安全审计（BC-6 / 任务 11.5）：删除成功后恰好记录一条 MemberDeleted
        //（写入加密本地存储、追加式 + 完整性校验保持防篡改、仅账号标签 + 脱敏元数据，绝不记录明文凭证）。
        await TryAuditAsync(SecurityAuditEventKind.MemberDeleted, member.AccountId, "member-deleted", cancellationToken);
    }

    public async Task DeleteAccountAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string targetAccountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetAccountId))
        {
            throw new InvalidOperationException("删除账号所需的主账号验证信息不完整。");
        }

        var owner = await VerifyOwnerIdentityInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        var target = await GetAccountRequiredAsync(targetAccountId, cancellationToken);
        if (target.Role == LocalAccountRole.Owner)
        {
            throw new InvalidOperationException("主账号不允许从账户管理页删除。");
        }

        if (!string.Equals(target.AdminOwnerAccountId, owner.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            // 跨事件埋点：对不属于当前主账号的账号发起删除操作 = 跨账户操作探测。
            TryObserveAbuse(AbuseSignalKind.CrossAccountOperationProbe, owner.AccountId);
            throw new InvalidOperationException("该账号不属于当前主账号，无法删除。");
        }

        var workspaceDirectory = ResolveDeletableAccountWorkspaceDirectory(target.AccountId, target.DatabasePath);
        await _accountRepository.DeleteAsync(target.AccountId, cancellationToken);
        DeleteAccountWorkspace(workspaceDirectory);

        // 安全审计（BC-6）：经 Owner 验证删除成员账号成功后恰好记录一条 MemberDeleted
        //（账号标签 + 脱敏元数据，绝不记录明文凭证）。
        await TryAuditAsync(SecurityAuditEventKind.MemberDeleted, target.AccountId, "member-deleted", cancellationToken);
    }
}
