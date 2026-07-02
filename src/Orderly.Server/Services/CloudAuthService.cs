using System.Security.Cryptography;
using Dapper;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Permissions;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class CloudAuthService : ICloudAuthService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly IAuditLogService _auditLogService;
    private readonly ServerOptions _options;

    public CloudAuthService(
        PostgresConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        IAuditLogService auditLogService,
        ServerOptions options)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _auditLogService = auditLogService;
        _options = options;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string ipAddress, string userAgent)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        var user = await connection.QueryFirstOrDefaultAsync<CloudUserRecord>(
            "SELECT * FROM \"CloudUsers\" WHERE \"Username\" = @username;",
            new { username = request.Username });

        if (user == null)
        {
            // Audit failure without workspace? We need workspace. Use system? For login fail we can log against a system workspace? Simpler: no audit yet.
            return null;
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            return null;
        }

        if (!user.IsEnabled)
        {
            return null;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            var failedCount = user.FailedLoginCount + 1;
            DateTime? lockedUntil = failedCount >= 5 ? DateTime.UtcNow.AddMinutes(15) : null;
            await connection.ExecuteAsync(
                "UPDATE \"CloudUsers\" SET \"FailedLoginCount\" = @failedCount, \"LockedUntil\" = @lockedUntil, \"UpdatedAt\" = @now WHERE \"Id\" = @id;",
                new { failedCount, lockedUntil, now = DateTime.UtcNow, id = user.Id });
            return null;
        }

        var membership = await GetMembershipForUserAsync(connection, user.Id);
        if (membership == null || !membership.IsEnabled)
        {
            return null;
        }

        await connection.ExecuteAsync(
            "UPDATE \"CloudUsers\" SET \"FailedLoginCount\" = 0, \"LockedUntil\" = NULL, \"UpdatedAt\" = @now WHERE \"Id\" = @id;",
            new { now = DateTime.UtcNow, id = user.Id });

        var (accessToken, refreshToken) = await GenerateTokensAsync(connection, user);

        await _auditLogService.LogAsync(
            membership.WorkspaceId,
            "LoginSuccess",
            "User",
            user.Id,
            null,
            null,
            reason: null,
            request.ClientRequestId,
            ipAddress,
            userAgent);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = MapUser(user),
            WorkspaceMembership = MapMembership(membership, string.Empty),
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    public async Task<LoginResponse?> RefreshAsync(RefreshRequest request)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var tokenHash = HashToken(request.RefreshToken);

        var tokenRecord = await connection.QueryFirstOrDefaultAsync<CloudRefreshTokenRecord>(
            "SELECT * FROM \"CloudRefreshTokens\" WHERE \"TokenHash\" = @tokenHash;",
            new { tokenHash });

        if (tokenRecord == null || tokenRecord.RevokedAt.HasValue || tokenRecord.ExpiresAt <= DateTime.UtcNow)
            return null;

        var user = await connection.QueryFirstOrDefaultAsync<CloudUserRecord>(
            "SELECT * FROM \"CloudUsers\" WHERE \"Id\" = @id;",
            new { id = tokenRecord.UserId });

        if (user == null || !user.IsEnabled) return null;

        var membership = await GetMembershipForUserAsync(connection, user.Id);
        if (membership == null || !membership.IsEnabled) return null;

        // Rotate refresh token: revoke old, create new
        var newRefreshPlain = GenerateRefreshToken();
        var newRefreshHash = HashToken(newRefreshPlain);
        var newTokenId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'Rotated', \"ReplacedByTokenId\" = @newTokenId WHERE \"Id\" = @id;",
            new { now, newTokenId, id = tokenRecord.Id }, tx);
        await connection.ExecuteAsync(
            "INSERT INTO \"CloudRefreshTokens\" (\"Id\", \"UserId\", \"TokenFamilyId\", \"TokenHash\", \"CreatedAt\", \"ExpiresAt\") VALUES (@id, @userId, @familyId, @hash, @now, @expires);",
            new { id = newTokenId, userId = user.Id, familyId = tokenRecord.TokenFamilyId, hash = newRefreshHash, now, expires = now.AddDays(_options.RefreshTokenLifetimeDays) }, tx);
        await tx.CommitAsync();

        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Username, user.DisplayName, user.TokenVersion);
        var workspace = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT \"Name\" FROM \"CloudWorkspaces\" WHERE \"Id\" = @id;", new { id = membership.WorkspaceId });

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshPlain,
            User = MapUser(user),
            WorkspaceMembership = MapMembership(membership, workspace ?? string.Empty),
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    public async Task<CloudUserRecord?> GetUserAsync(Guid userId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        return await connection.QueryFirstOrDefaultAsync<CloudUserRecord>(
            "SELECT * FROM \"CloudUsers\" WHERE \"Id\" = @id;", new { id = userId });
    }

    public async Task<CloudWorkspaceMemberRecord?> GetMembershipAsync(Guid userId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        return await GetMembershipForUserAsync(connection, userId);
    }

    public async Task<bool> ValidateTokenVersionAsync(Guid userId, int tokenVersion)
    {
        var user = await GetUserAsync(userId);
        return user != null && user.IsEnabled && user.TokenVersion == tokenVersion;
    }

    public async Task<CloudUserRecord?> CreateUserAsync(CreateUserRequest request, Guid actorUserId)
    {
        if (!CloudRole.IsValid(request.CloudRole) || !BusinessLabel.IsValid(request.BusinessLabel))
            return null;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null) return null;

        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var passwordHash = _passwordHasher.HashPassword(request.InitialPassword);

        await using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUsers"" (""Id"", ""Username"", ""DisplayName"", ""PasswordHash"", ""IsEnabled"", ""CreatedByUserId"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (@id, @username, @displayName, @passwordHash, TRUE, @createdBy, @now, @now);",
            new { id = userId, request.Username, request.DisplayName, passwordHash, createdBy = actorUserId, now }, tx);

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaceMembers"" (""WorkspaceId"", ""UserId"", ""CloudRole"", ""BusinessLabel"", ""RolePolicyVersion"", ""IsEnabled"", ""CreatedByUserId"", ""CreatedAt"", ""UpdatedByUserId"", ""UpdatedAt"")
            VALUES (@workspaceId, @userId, @role, @label, 1, TRUE, @createdBy, @now, @createdBy, @now);",
            new { workspaceId = actorMembership.WorkspaceId, userId, role = request.CloudRole, label = request.BusinessLabel, createdBy = actorUserId, now }, tx);

        await tx.CommitAsync();
        return await GetUserAsync(userId);
    }

    public async Task<bool> DisableUserAsync(Guid userId, Guid actorUserId)
    {
        if (userId == actorUserId) return false; // cannot disable self

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null) return false;

        // Ensure not last admin
        var adminCount = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudWorkspaceMembers"" m
              JOIN ""CloudUsers"" u ON u.""Id"" = m.""UserId""
              WHERE m.""WorkspaceId"" = @workspaceId AND m.""CloudRole"" = @adminRole AND u.""IsEnabled"" = TRUE AND m.""IsEnabled"" = TRUE;",
            new { workspaceId = actorMembership.WorkspaceId, adminRole = CloudRole.Admin });

        var targetRole = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT \"CloudRole\" FROM \"CloudWorkspaceMembers\" WHERE \"WorkspaceId\" = @workspaceId AND \"UserId\" = @userId;",
            new { workspaceId = actorMembership.WorkspaceId, userId });
        if (targetRole == CloudRole.Admin && adminCount <= 1) return false;

        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            @"UPDATE ""CloudWorkspaceMembers"" SET ""IsEnabled"" = FALSE, ""UpdatedByUserId"" = @actor, ""UpdatedAt"" = @now
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId;",
            new { actor = actorUserId, now, workspaceId = actorMembership.WorkspaceId, userId });
        await connection.ExecuteAsync(
            @"UPDATE ""CloudUsers"" SET ""IsEnabled"" = FALSE, ""DisabledAt"" = @now, ""DisabledByUserId"" = @actor, ""UpdatedAt"" = @now
              WHERE ""Id"" = @userId;",
            new { now, actor = actorUserId, userId });
        // Revoke refresh tokens
        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'AccountDisabled' WHERE \"UserId\" = @userId AND \"RevokedAt\" IS NULL;",
            new { now, userId });
        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await GetUserAsync(userId);
        if (user == null) return false;
        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash)) return false;
        await SetPasswordAsync(userId, newPassword);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string newPassword, Guid actorUserId)
    {
        await SetPasswordAsync(userId, newPassword);
        return true;
    }

    public async Task InvalidateSessionsAsync(Guid userId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            "UPDATE \"CloudUsers\" SET \"TokenVersion\" = \"TokenVersion\" + 1, \"UpdatedAt\" = @now WHERE \"Id\" = @id;",
            new { now, id = userId });
        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'LogoutAll' WHERE \"UserId\" = @userId AND \"RevokedAt\" IS NULL;",
            new { now, userId });
    }

    public async Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync(Guid workspaceId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var sql = @"
            SELECT u.""Id"", u.""Username"", u.""DisplayName"", u.""IsEnabled"", u.""CreatedAt"", u.""UpdatedAt"",
                   COALESCE(creator.""DisplayName"", '') AS ""CreatedByDisplayName""
            FROM ""CloudWorkspaceMembers"" m
            JOIN ""CloudUsers"" u ON u.""Id"" = m.""UserId""
            LEFT JOIN ""CloudUsers"" creator ON creator.""Id"" = u.""CreatedByUserId""
            WHERE m.""WorkspaceId"" = @workspaceId
            ORDER BY u.""CreatedAt"" DESC;";
        var rows = await connection.QueryAsync<UserSummaryDto>(sql, new { workspaceId });
        return rows.ToList();
    }

    public async Task EnsureBootstrapAdminAsync(string bootstrapToken)
    {
        if (string.IsNullOrEmpty(_options.BootstrapAdminToken)) return;
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(bootstrapToken),
            System.Text.Encoding.UTF8.GetBytes(_options.BootstrapAdminToken))) return;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var anyUser = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"CloudUsers\";") > 0;
        if (anyUser) return;

        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var passwordHash = _passwordHasher.HashPassword("OrderlyAdmin@123");

        await using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"CloudWorkspaces\" (\"Id\", \"Name\", \"DefaultCurrencyCode\", \"CreatedAt\", \"UpdatedAt\") VALUES (@id, 'Default Workspace', 'CNY', @now, @now);",
            new { id = workspaceId, now }, tx);
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUsers"" (""Id"", ""Username"", ""DisplayName"", ""PasswordHash"", ""IsEnabled"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (@id, 'admin', '运营负责人', @passwordHash, TRUE, @now, @now);",
            new { id = userId, passwordHash, now }, tx);
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaceMembers"" (""WorkspaceId"", ""UserId"", ""CloudRole"", ""BusinessLabel"", ""RolePolicyVersion"", ""IsEnabled"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (@workspaceId, @userId, @role, @label, 1, TRUE, @now, @now);",
            new { workspaceId, userId, role = CloudRole.Admin, label = BusinessLabel.Operator, now }, tx);
        await connection.ExecuteAsync(
            "INSERT INTO \"CloudWorkspaceSyncState\" (\"WorkspaceId\", \"LastSequence\", \"UpdatedAt\") VALUES (@workspaceId, 0, @now);",
            new { workspaceId, now }, tx);
        await tx.CommitAsync();
    }

    private async Task SetPasswordAsync(Guid userId, string newPassword)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var hash = _passwordHasher.HashPassword(newPassword);
        await connection.ExecuteAsync(
            @"UPDATE ""CloudUsers"" SET ""PasswordHash"" = @hash, ""TokenVersion"" = ""TokenVersion"" + 1,
               ""PasswordChangedAt"" = @now, ""UpdatedAt"" = @now WHERE ""Id"" = @id;",
            new { hash, now = DateTime.UtcNow, id = userId });
        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'PasswordChanged' WHERE \"UserId\" = @userId AND \"RevokedAt\" IS NULL;",
            new { now = DateTime.UtcNow, userId });
    }

    private async Task<CloudWorkspaceMemberRecord?> GetMembershipForUserAsync(System.Data.Common.DbConnection connection, Guid userId)
    {
        return await connection.QueryFirstOrDefaultAsync<CloudWorkspaceMemberRecord>(
            "SELECT * FROM \"CloudWorkspaceMembers\" WHERE \"UserId\" = @userId AND \"IsEnabled\" = TRUE;",
            new { userId });
    }

    private async Task<(string accessToken, string refreshToken)> GenerateTokensAsync(System.Data.Common.DbConnection connection, CloudUserRecord user)
    {
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Username, user.DisplayName, user.TokenVersion);
        var refreshPlain = GenerateRefreshToken();
        var refreshHash = HashToken(refreshPlain);
        var tokenId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            "INSERT INTO \"CloudRefreshTokens\" (\"Id\", \"UserId\", \"TokenFamilyId\", \"TokenHash\", \"CreatedAt\", \"ExpiresAt\") VALUES (@id, @userId, @familyId, @hash, @now, @expires);",
            new { id = tokenId, userId = user.Id, familyId, hash = refreshHash, now, expires = now.AddDays(_options.RefreshTokenLifetimeDays) });
        return (accessToken, refreshPlain);
    }

    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static CloudUserDto MapUser(CloudUserRecord user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        IsEnabled = user.IsEnabled,
        CreatedAtUtc = user.CreatedAt,
        UpdatedAtUtc = user.UpdatedAt
    };

    private static CloudWorkspaceMembershipDto MapMembership(CloudWorkspaceMemberRecord membership, string workspaceName) => new()
    {
        WorkspaceId = membership.WorkspaceId,
        WorkspaceName = workspaceName,
        UserId = membership.UserId,
        CloudRole = membership.CloudRole,
        BusinessLabel = membership.BusinessLabel,
        RolePolicyVersion = membership.RolePolicyVersion,
        IsEnabled = membership.IsEnabled
    };
}
