using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly IIdempotencyService _idempotency;
    private readonly ServerOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CloudAuthService(
        PostgresConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        IAuditLogService auditLogService,
        IIdempotencyService idempotency,
        ServerOptions options)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _auditLogService = auditLogService;
        _idempotency = idempotency;
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
            await RecordLoginFailureAsync(connection, null, request.Username, "UserNotFound", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            await RecordLoginFailureAsync(connection, user, request.Username, "AccountLocked", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        if (!user.IsEnabled)
        {
            await RecordLoginFailureAsync(connection, user, request.Username, "AccountDisabled", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            var failedCount = user.FailedLoginCount + 1;
            DateTime? lockedUntil = failedCount >= 5 ? DateTime.UtcNow.AddMinutes(15) : null;
            await connection.ExecuteAsync(
                "UPDATE \"CloudUsers\" SET \"FailedLoginCount\" = @failedCount, \"LockedUntil\" = @lockedUntil, \"UpdatedAt\" = @now WHERE \"Id\" = @id;",
                new { failedCount, lockedUntil, now = DateTime.UtcNow, id = user.Id });
            await RecordLoginFailureAsync(connection, user, request.Username, "BadPassword", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        var membership = await GetMembershipForUserAsync(connection, user.Id);
        if (membership == null || !membership.IsEnabled)
        {
            await RecordLoginFailureAsync(connection, user, request.Username, "WorkspaceMembershipDisabled", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        var device = await EnsureApprovedLoginDeviceAsync(connection, user, membership, request, ipAddress, userAgent);
        if (device == null)
        {
            return null;
        }

        await connection.ExecuteAsync(
            "UPDATE \"CloudUsers\" SET \"FailedLoginCount\" = 0, \"LockedUntil\" = NULL, \"UpdatedAt\" = @now WHERE \"Id\" = @id;",
            new { now = DateTime.UtcNow, id = user.Id });

        var (accessToken, refreshToken) = await GenerateTokensAsync(connection, user, device.DeviceId);

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

        if (tokenRecord == null)
            return null;

        if (tokenRecord.RevokedAt.HasValue)
        {
            await RevokeRefreshTokenFamilyAsync(connection, tokenRecord, "RefreshTokenReuse");
            return null;
        }

        if (tokenRecord.ExpiresAt <= DateTime.UtcNow)
            return null;

        var user = await connection.QueryFirstOrDefaultAsync<CloudUserRecord>(
            "SELECT * FROM \"CloudUsers\" WHERE \"Id\" = @id;",
            new { id = tokenRecord.UserId });

        if (user == null || !user.IsEnabled) return null;

        var membership = await GetMembershipForUserAsync(connection, user.Id);
        if (membership == null || !membership.IsEnabled) return null;

        if (string.IsNullOrWhiteSpace(tokenRecord.DeviceId)
            || !await ValidateDeviceAccessAsync(user.Id, tokenRecord.DeviceId))
        {
            return null;
        }

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
            "INSERT INTO \"CloudRefreshTokens\" (\"Id\", \"UserId\", \"TokenFamilyId\", \"TokenHash\", \"DeviceId\", \"CreatedAt\", \"ExpiresAt\") VALUES (@id, @userId, @familyId, @hash, @deviceId, @now, @expires);",
            new { id = newTokenId, userId = user.Id, familyId = tokenRecord.TokenFamilyId, hash = newRefreshHash, deviceId = tokenRecord.DeviceId, now, expires = now.AddDays(_options.RefreshTokenLifetimeDays) }, tx);
        await tx.CommitAsync();

        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Username, user.DisplayName, user.TokenVersion, tokenRecord.DeviceId);
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

    public async Task<bool> ValidateDeviceAccessAsync(Guid userId, string deviceId)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);
        if (string.IsNullOrWhiteSpace(normalizedDeviceId)) return false;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudDevices""
              WHERE ""UserId"" = @userId AND ""DeviceId"" = @deviceId AND ""Status"" = @status;",
            new { userId, deviceId = normalizedDeviceId, status = CloudDeviceStatus.Approved });
        return count > 0;
    }

    public async Task<CloudUserRecord?> CreateUserAsync(CreateUserRequest request, Guid actorUserId)
    {
        if (!CloudRole.IsValid(request.CloudRole) || !BusinessLabel.IsValid(request.BusinessLabel))
            return null;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership)) return null;

        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var passwordHash = _passwordHasher.HashPassword(request.InitialPassword);

        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "user:create", request.ClientRequestId, request);
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<CloudUserRecord>(idempotency);
        }

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUsers"" (""Id"", ""Username"", ""DisplayName"", ""PasswordHash"", ""IsEnabled"", ""CreatedByUserId"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (@id, @username, @displayName, @passwordHash, TRUE, @createdBy, @now, @now);",
            new { id = userId, request.Username, request.DisplayName, passwordHash, createdBy = actorUserId, now }, tx);

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaceMembers"" (""WorkspaceId"", ""UserId"", ""CloudRole"", ""BusinessLabel"", ""RolePolicyVersion"", ""IsEnabled"", ""CreatedByUserId"", ""CreatedAt"", ""UpdatedByUserId"", ""UpdatedAt"")
            VALUES (@workspaceId, @userId, @role, @label, 1, TRUE, @createdBy, @now, @createdBy, @now);",
            new { workspaceId = actorMembership.WorkspaceId, userId, role = request.CloudRole, label = request.BusinessLabel, createdBy = actorUserId, now }, tx);

        await _auditLogService.LogAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            "UserCreated",
            "user",
            userId,
            null,
            null,
            reason: null,
            request.ClientRequestId);

        var createdUser = await connection.QueryFirstOrDefaultAsync<CloudUserRecord>(
            "SELECT * FROM \"CloudUsers\" WHERE \"Id\" = @id;",
            new { id = userId },
            tx)
            ?? throw new InvalidOperationException("Created user could not be loaded.");
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "user:create", request.ClientRequestId, createdUser, "user", userId);
        await tx.CommitAsync();
        return createdUser;
    }

    public async Task<bool> DisableUserAsync(Guid userId, Guid actorUserId, string? clientRequestId = null)
    {
        if (userId == actorUserId) return false; // cannot disable self
        if (string.IsNullOrWhiteSpace(clientRequestId)) return false;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership)) return false;

        // Ensure not last admin
        var adminCount = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudWorkspaceMembers"" m
              JOIN ""CloudUsers"" u ON u.""Id"" = m.""UserId""
              WHERE m.""WorkspaceId"" = @workspaceId AND m.""CloudRole"" = @adminRole AND u.""IsEnabled"" = TRUE AND m.""IsEnabled"" = TRUE;",
            new { workspaceId = actorMembership.WorkspaceId, adminRole = CloudRole.Admin });

        var targetRole = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT \"CloudRole\" FROM \"CloudWorkspaceMembers\" WHERE \"WorkspaceId\" = @workspaceId AND \"UserId\" = @userId;",
            new { workspaceId = actorMembership.WorkspaceId, userId });
        if (targetRole == null) return false;
        if (targetRole == CloudRole.Admin && adminCount <= 1) return false;

        var now = DateTime.UtcNow;
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            actorUserId,
            "user:disable",
            clientRequestId,
            new { userId });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<bool>(idempotency);
        }

        await connection.ExecuteAsync(
            @"UPDATE ""CloudWorkspaceMembers"" SET ""IsEnabled"" = FALSE, ""UpdatedByUserId"" = @actor, ""UpdatedAt"" = @now
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId;",
            new { actor = actorUserId, now, workspaceId = actorMembership.WorkspaceId, userId }, tx);
        await connection.ExecuteAsync(
            @"UPDATE ""CloudUsers"" SET ""IsEnabled"" = FALSE, ""DisabledAt"" = @now, ""DisabledByUserId"" = @actor, ""UpdatedAt"" = @now
              WHERE ""Id"" = @userId;",
            new { now, actor = actorUserId, userId }, tx);
        // Revoke refresh tokens
        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'AccountDisabled' WHERE \"UserId\" = @userId AND \"RevokedAt\" IS NULL;",
            new { now, userId }, tx);
        await _auditLogService.LogAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            "UserDisabled",
            "user",
            userId,
            null,
            null,
            reason: null,
            clientRequestId);
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "user:disable", clientRequestId, true, "user", userId);
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string? clientRequestId = null)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId)) return false;
        if (string.IsNullOrWhiteSpace(newPassword)) return false;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var membership = await GetMembershipForUserAsync(connection, userId);
        if (membership == null) return false;

        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            membership.WorkspaceId,
            userId,
            "auth:changePassword",
            clientRequestId,
            new
            {
                CurrentPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(currentPassword))),
                NewPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newPassword)))
            });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<bool>(idempotency);
        }

        var user = await connection.QueryFirstOrDefaultAsync<CloudUserRecord>(
            "SELECT * FROM \"CloudUsers\" WHERE \"Id\" = @id FOR UPDATE;",
            new { id = userId },
            tx);
        if (user == null || !_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            await tx.RollbackAsync();
            return false;
        }

        var now = DateTime.UtcNow;
        var hash = _passwordHasher.HashPassword(newPassword);
        await connection.ExecuteAsync(
            @"UPDATE ""CloudUsers"" SET ""PasswordHash"" = @hash, ""TokenVersion"" = ""TokenVersion"" + 1,
               ""PasswordChangedAt"" = @now, ""UpdatedAt"" = @now WHERE ""Id"" = @id;",
            new { hash, now, id = userId },
            tx);
        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'PasswordChanged' WHERE \"UserId\" = @userId AND \"RevokedAt\" IS NULL;",
            new { now, userId },
            tx);
        await _auditLogService.LogAsync(connection, tx, membership.WorkspaceId, "UserPasswordChanged", "user", userId, null, null, clientRequestId: clientRequestId);
        await CompleteIdempotencyAsync(connection, tx, membership.WorkspaceId, userId, "auth:changePassword", clientRequestId, true, "user", userId);
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string newPassword, Guid actorUserId, string? clientRequestId = null)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) return false;
        if (string.IsNullOrWhiteSpace(clientRequestId)) return false;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership)) return false;

        var targetMembership = await connection.QueryFirstOrDefaultAsync<CloudWorkspaceMemberRecord>(
            @"SELECT * FROM ""CloudWorkspaceMembers""
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId;",
            new { workspaceId = actorMembership.WorkspaceId, userId });
        if (targetMembership == null) return false;

        var hash = _passwordHasher.HashPassword(newPassword);
        var now = DateTime.UtcNow;
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            actorUserId,
            "user:resetPassword",
            clientRequestId,
            new { userId, NewPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newPassword))) });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<bool>(idempotency);
        }

        var affected = await connection.ExecuteAsync(
            @"UPDATE ""CloudUsers"" SET ""PasswordHash"" = @hash, ""TokenVersion"" = ""TokenVersion"" + 1,
               ""PasswordChangedAt"" = @now, ""UpdatedAt"" = @now WHERE ""Id"" = @id;",
            new { hash, now, id = userId }, tx);
        if (affected == 0) return false;

        await connection.ExecuteAsync(
            "UPDATE \"CloudRefreshTokens\" SET \"RevokedAt\" = @now, \"RevokedReason\" = 'PasswordReset' WHERE \"UserId\" = @userId AND \"RevokedAt\" IS NULL;",
            new { now, userId }, tx);
        await _auditLogService.LogAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            "UserPasswordReset",
            "user",
            userId,
            null,
            null,
            reason: null,
            clientRequestId);
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "user:resetPassword", clientRequestId, true, "user", userId);
        await tx.CommitAsync();
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
            SELECT u.""Id"", u.""Username"", u.""DisplayName"", m.""CloudRole"", m.""BusinessLabel"",
                   u.""IsEnabled"", u.""CreatedAt"" AS ""CreatedAtUtc"", u.""UpdatedAt"" AS ""UpdatedAtUtc"",
                   COALESCE(creator.""DisplayName"", '') AS ""CreatedByDisplayName""
            FROM ""CloudWorkspaceMembers"" m
            JOIN ""CloudUsers"" u ON u.""Id"" = m.""UserId""
            LEFT JOIN ""CloudUsers"" creator ON creator.""Id"" = u.""CreatedByUserId""
            WHERE m.""WorkspaceId"" = @workspaceId
            ORDER BY u.""CreatedAt"" DESC;";
        var rows = await connection.QueryAsync<UserSummaryDto>(sql, new { workspaceId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<CloudLoginFailureDto>> ListLoginFailuresAsync(Guid workspaceId, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var rows = await connection.QueryAsync<CloudLoginFailureDto>(
            @"SELECT ""Id"", ""WorkspaceId"", ""UserId"", ""Username"", ""Reason"", ""ClientRequestId"",
                     ""IpAddress"", ""UserAgent"", ""OccurredAt"" AS ""OccurredAtUtc""
              FROM ""CloudLoginFailures""
              WHERE ""WorkspaceId"" = @workspaceId OR ""WorkspaceId"" IS NULL
              ORDER BY ""OccurredAt"" DESC
              LIMIT @limit;",
            new { workspaceId, limit });
        return rows.ToList();
    }

    public async Task<CloudInvitationDto?> CreateInvitationAsync(CreateInvitationRequest request, Guid actorUserId)
    {
        if (!CloudRole.IsValid(request.CloudRole) || !BusinessLabel.IsValid(request.BusinessLabel))
            return null;

        var code = NormalizeInviteCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            code = GenerateInviteCode();
        }

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership)) return null;

        var now = DateTime.UtcNow;
        var invitationId = Guid.NewGuid();
        var maxUses = Math.Clamp(request.MaxUses, 1, 1000);
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "invitation:create", request.ClientRequestId, request);
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<CloudInvitationDto>(idempotency);
        }

        var exists = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudInvitations"" WHERE ""Code"" = @code;",
            new { code },
            tx);
        if (exists > 0)
        {
            await tx.RollbackAsync();
            return null;
        }

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudInvitations"" (
                ""Id"", ""WorkspaceId"", ""Code"", ""CloudRole"", ""BusinessLabel"", ""Status"",
                ""MaxUses"", ""UsedCount"", ""ExpiresAt"", ""CreatedByUserId"", ""CreatedAt"")
              VALUES (
                @id, @workspaceId, @code, @role, @label, @status,
                @maxUses, 0, @expiresAt, @createdBy, @now);",
            new
            {
                id = invitationId,
                workspaceId = actorMembership.WorkspaceId,
                code,
                role = request.CloudRole,
                label = request.BusinessLabel,
                status = CloudInvitationStatus.Active,
                maxUses,
                expiresAt = request.ExpiresAtUtc,
                createdBy = actorUserId,
                now
            }, tx);
        await _auditLogService.LogAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            "InvitationCreated",
            "invitation",
            invitationId,
            null,
            JsonSerializer.Serialize(new { code, request.CloudRole, request.BusinessLabel, maxUses, request.ExpiresAtUtc }),
            reason: null,
            request.ClientRequestId);
        var invitation = await GetInvitationDtoAsync(connection, invitationId)
            ?? throw new InvalidOperationException("Invitation was created but could not be loaded.");
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "invitation:create", request.ClientRequestId, invitation, "invitation", invitationId);
        await tx.CommitAsync();

        return invitation;
    }

    public async Task<IReadOnlyList<CloudInvitationDto>> ListInvitationsAsync(Guid workspaceId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var rows = await connection.QueryAsync<CloudInvitationDto>(
            @"SELECT i.""Id"", i.""WorkspaceId"", i.""Code"", i.""CloudRole"", i.""BusinessLabel"", i.""Status"",
                     i.""MaxUses"", i.""UsedCount"", i.""ExpiresAt"" AS ""ExpiresAtUtc"",
                     i.""CreatedAt"" AS ""CreatedAtUtc"", COALESCE(u.""DisplayName"", '') AS ""CreatedByDisplayName""
              FROM ""CloudInvitations"" i
              LEFT JOIN ""CloudUsers"" u ON u.""Id"" = i.""CreatedByUserId""
              WHERE i.""WorkspaceId"" = @workspaceId
              ORDER BY i.""CreatedAt"" DESC;",
            new { workspaceId });
        return rows.ToList();
    }

    public async Task<CloudUserApplicationDto?> SubmitApplicationAsync(SubmitUserApplicationRequest request, string ipAddress, string userAgent)
    {
        var inviteCode = NormalizeInviteCode(request.InviteCode);
        var username = NormalizeUsername(request.Username);
        var displayName = NormalizeDisplayName(request.DisplayName);
        var deviceId = NormalizeDeviceId(request.DeviceId);
        var deviceName = NormalizeDeviceName(request.DeviceName);
        if (string.IsNullOrWhiteSpace(inviteCode)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(request.InitialPassword)
            || string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var invitation = await connection.QueryFirstOrDefaultAsync<InvitationRow>(
            @"SELECT * FROM ""CloudInvitations""
              WHERE ""Code"" = @inviteCode AND ""Status"" = @status;",
            new { inviteCode, status = CloudInvitationStatus.Active });
        if (invitation == null) return null;
        if (invitation.ExpiresAt.HasValue && invitation.ExpiresAt.Value <= DateTime.UtcNow) return null;
        if (invitation.UsedCount >= invitation.MaxUses) return null;

        var existingApplication = await connection.QueryFirstOrDefaultAsync<Guid?>(
            @"SELECT ""Id"" FROM ""CloudUserApplications""
              WHERE ""WorkspaceId"" = @workspaceId AND ""ClientRequestId"" = @clientRequestId;",
            new { workspaceId = invitation.WorkspaceId, clientRequestId = request.ClientRequestId });
        if (existingApplication.HasValue)
        {
            return await GetApplicationDtoAsync(connection, existingApplication.Value);
        }

        var usernameExists = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudUsers"" WHERE ""Username"" = @username;",
            new { username });
        if (usernameExists > 0) return null;

        var pendingExists = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudUserApplications""
              WHERE ""Username"" = @username AND ""Status"" = @status;",
            new { username, status = CloudUserApplicationStatus.Pending });
        if (pendingExists > 0) return null;

        var applicationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var passwordHash = _passwordHasher.HashPassword(request.InitialPassword);
        await using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUserApplications"" (
                ""Id"", ""WorkspaceId"", ""InvitationId"", ""InviteCode"", ""Username"", ""DisplayName"",
                ""PasswordHash"", ""Status"", ""RequestedDeviceId"", ""RequestedDeviceName"", ""RequestedAt"",
                ""ClientRequestId"", ""IpAddress"", ""UserAgent"")
              VALUES (
                @id, @workspaceId, @invitationId, @inviteCode, @username, @displayName,
                @passwordHash, @status, @deviceId, @deviceName, @now,
                @clientRequestId, @ipAddress, @userAgent);",
            new
            {
                id = applicationId,
                workspaceId = invitation.WorkspaceId,
                invitationId = invitation.Id,
                inviteCode,
                username,
                displayName,
                passwordHash,
                status = CloudUserApplicationStatus.Pending,
                deviceId,
                deviceName,
                now,
                clientRequestId = request.ClientRequestId,
                ipAddress,
                userAgent
            }, tx);
        await _auditLogService.LogAsync(
            connection,
            tx,
            invitation.WorkspaceId,
            "UserApplicationSubmitted",
            "userApplication",
            applicationId,
            null,
            JsonSerializer.Serialize(new { username, displayName, deviceName }),
            reason: null,
            request.ClientRequestId,
            ipAddress,
            userAgent);
        var application = await GetApplicationDtoAsync(connection, applicationId)
            ?? throw new InvalidOperationException("Application was created but could not be loaded.");
        await tx.CommitAsync();

        return application;
    }

    public async Task<IReadOnlyList<CloudUserApplicationDto>> ListApplicationsAsync(Guid workspaceId, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var rows = await connection.QueryAsync<CloudUserApplicationDto>(
            ApplicationDtoSelectSql + @"
              WHERE a.""WorkspaceId"" = @workspaceId
              ORDER BY a.""RequestedAt"" DESC
              LIMIT @limit;",
            new { workspaceId, limit });
        return rows.ToList();
    }

    public async Task<CloudUserApplicationDto?> ApproveApplicationAsync(Guid applicationId, Guid actorUserId, string? reason, string? clientRequestId = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership)) return null;

        var app = await connection.QueryFirstOrDefaultAsync<ApplicationApprovalRow>(
            @"SELECT a.*, i.""CloudRole"", i.""BusinessLabel""
              FROM ""CloudUserApplications"" a
              JOIN ""CloudInvitations"" i ON i.""Id"" = a.""InvitationId""
              WHERE a.""Id"" = @applicationId AND a.""WorkspaceId"" = @workspaceId;",
            new { applicationId, workspaceId = actorMembership.WorkspaceId });
        if (app == null) return null;
        if (!string.Equals(app.Status, CloudUserApplicationStatus.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return await GetApplicationDtoAsync(connection, applicationId);
        }
        if (string.IsNullOrWhiteSpace(clientRequestId)) return null;

        var usernameExists = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudUsers"" WHERE ""Username"" = @username;",
            new { username = app.Username });
        if (usernameExists > 0) return null;

        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var deviceRecordId = Guid.NewGuid();
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            actorUserId,
            "application:approve",
            clientRequestId,
            new { applicationId, reason });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<CloudUserApplicationDto>(idempotency);
        }

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUsers"" (
                ""Id"", ""Username"", ""DisplayName"", ""PasswordHash"", ""IsEnabled"",
                ""CreatedByUserId"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (
                @userId, @username, @displayName, @passwordHash, TRUE,
                @actorUserId, @now, @now);",
            new { userId, app.Username, app.DisplayName, app.PasswordHash, actorUserId, now }, tx);
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaceMembers"" (
                ""WorkspaceId"", ""UserId"", ""CloudRole"", ""BusinessLabel"", ""RolePolicyVersion"",
                ""IsEnabled"", ""CreatedByUserId"", ""CreatedAt"", ""UpdatedByUserId"", ""UpdatedAt"")
              VALUES (
                @workspaceId, @userId, @role, @label, 1,
                TRUE, @actorUserId, @now, @actorUserId, @now);",
            new { workspaceId = app.WorkspaceId, userId, role = app.CloudRole, label = app.BusinessLabel, actorUserId, now }, tx);
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudDevices"" (
                ""Id"", ""WorkspaceId"", ""UserId"", ""DeviceId"", ""DeviceName"", ""Status"",
                ""FirstSeenAt"", ""LastSeenAt"", ""ApprovedByUserId"", ""ApprovedAt"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (
                @id, @workspaceId, @userId, @deviceId, @deviceName, @status,
                @now, @now, @actorUserId, @now, @now, @now);",
            new
            {
                id = deviceRecordId,
                workspaceId = app.WorkspaceId,
                userId,
                deviceId = app.RequestedDeviceId,
                deviceName = app.RequestedDeviceName,
                status = CloudDeviceStatus.Approved,
                actorUserId,
                now
            }, tx);
        await connection.ExecuteAsync(
            @"UPDATE ""CloudUserApplications""
              SET ""Status"" = @status, ""ReviewedByUserId"" = @actorUserId, ""ReviewedAt"" = @now,
                  ""ReviewReason"" = @reason, ""CreatedUserId"" = @userId
              WHERE ""Id"" = @applicationId;",
            new { status = CloudUserApplicationStatus.Approved, actorUserId, now, reason, userId, applicationId }, tx);
        await connection.ExecuteAsync(
            @"UPDATE ""CloudInvitations""
              SET ""UsedCount"" = ""UsedCount"" + 1
              WHERE ""Id"" = @invitationId;",
            new { invitationId = app.InvitationId }, tx);
        await _auditLogService.LogAsync(connection, tx, app.WorkspaceId, "UserApplicationApproved", "userApplication", applicationId, null, null, reason, clientRequestId);
        await _auditLogService.LogAsync(connection, tx, app.WorkspaceId, "UserCreated", "user", userId, null, null, reason: null, clientRequestId);
        await _auditLogService.LogAsync(connection, tx, app.WorkspaceId, "DeviceApproved", "device", deviceRecordId, null, null, "First device from approved application.", clientRequestId);
        var approvedApplication = await GetApplicationDtoAsync(connection, applicationId)
            ?? throw new InvalidOperationException("Approved application could not be loaded.");
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "application:approve", clientRequestId, approvedApplication, "userApplication", applicationId);
        await tx.CommitAsync();

        return approvedApplication;
    }

    public async Task<CloudUserApplicationDto?> RejectApplicationAsync(Guid applicationId, Guid actorUserId, string? reason, string? clientRequestId = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership)) return null;
        if (string.IsNullOrWhiteSpace(clientRequestId)) return null;

        var now = DateTime.UtcNow;
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            actorUserId,
            "application:reject",
            clientRequestId,
            new { applicationId, reason });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<CloudUserApplicationDto>(idempotency);
        }

        var affected = await connection.ExecuteAsync(
            @"UPDATE ""CloudUserApplications""
              SET ""Status"" = @status, ""ReviewedByUserId"" = @actorUserId, ""ReviewedAt"" = @now,
                  ""ReviewReason"" = @reason
              WHERE ""Id"" = @applicationId AND ""WorkspaceId"" = @workspaceId AND ""Status"" = @pending;",
            new
            {
                status = CloudUserApplicationStatus.Rejected,
                actorUserId,
                now,
                reason,
                applicationId,
                workspaceId = actorMembership.WorkspaceId,
                pending = CloudUserApplicationStatus.Pending
            }, tx);
        if (affected == 0)
        {
            await tx.RollbackAsync();
            return await GetApplicationDtoAsync(connection, applicationId);
        }

        await _auditLogService.LogAsync(connection, tx, actorMembership.WorkspaceId, "UserApplicationRejected", "userApplication", applicationId, null, null, reason, clientRequestId);
        var rejectedApplication = await GetApplicationDtoAsync(connection, applicationId)
            ?? throw new InvalidOperationException("Rejected application could not be loaded.");
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "application:reject", clientRequestId, rejectedApplication, "userApplication", applicationId);
        await tx.CommitAsync();

        return rejectedApplication;
    }

    public async Task<IReadOnlyList<CloudDeviceDto>> ListDevicesAsync(Guid workspaceId, Guid actorUserId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || actorMembership.WorkspaceId != workspaceId) return Array.Empty<CloudDeviceDto>();

        var canManageDevices = CanManageCloudAdministration(actorMembership);
        var sql = DeviceDtoSelectSql + (canManageDevices
            ? @" WHERE d.""WorkspaceId"" = @workspaceId"
            : @" WHERE d.""WorkspaceId"" = @workspaceId AND d.""UserId"" = @actorUserId")
            + @" ORDER BY d.""UpdatedAt"" DESC;";
        var rows = await connection.QueryAsync<CloudDeviceDto>(sql, new { workspaceId, actorUserId });
        return rows.ToList();
    }

    public async Task<bool> ApproveDeviceAsync(Guid deviceRecordId, Guid actorUserId, string? clientRequestId = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null || !CanManageCloudAdministration(actorMembership))
            return false;
        if (string.IsNullOrWhiteSpace(clientRequestId)) return false;

        var now = DateTime.UtcNow;
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            actorUserId,
            "device:approve",
            clientRequestId,
            new { deviceRecordId });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<bool>(idempotency);
        }

        var affected = await connection.ExecuteAsync(
            @"UPDATE ""CloudDevices""
              SET ""Status"" = @status, ""ApprovedByUserId"" = @actorUserId, ""ApprovedAt"" = @now,
                  ""RevokedByUserId"" = NULL, ""RevokedAt"" = NULL, ""UpdatedAt"" = @now
              WHERE ""Id"" = @deviceRecordId AND ""WorkspaceId"" = @workspaceId;",
            new { status = CloudDeviceStatus.Approved, actorUserId, now, deviceRecordId, workspaceId = actorMembership.WorkspaceId }, tx);
        if (affected == 0)
        {
            await tx.RollbackAsync();
            return false;
        }

        await _auditLogService.LogAsync(connection, tx, actorMembership.WorkspaceId, "DeviceApproved", "device", deviceRecordId, null, null, reason: null, clientRequestId);
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "device:approve", clientRequestId, true, "device", deviceRecordId);
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> RevokeDeviceAsync(Guid deviceRecordId, Guid actorUserId, string? clientRequestId = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var actorMembership = await GetMembershipForUserAsync(connection, actorUserId);
        if (actorMembership == null) return false;
        if (string.IsNullOrWhiteSpace(clientRequestId)) return false;

        var device = await connection.QueryFirstOrDefaultAsync<CloudDeviceRecord>(
            @"SELECT * FROM ""CloudDevices""
              WHERE ""Id"" = @deviceRecordId AND ""WorkspaceId"" = @workspaceId;",
            new { deviceRecordId, workspaceId = actorMembership.WorkspaceId });
        if (device == null) return false;

        var canManageDevices = CanManageCloudAdministration(actorMembership);
        if (!canManageDevices && device.UserId != actorUserId) return false;

        var now = DateTime.UtcNow;
        await using var tx = await connection.BeginTransactionAsync();
        var idempotency = await TryBeginIdempotencyAsync(
            connection,
            tx,
            actorMembership.WorkspaceId,
            actorUserId,
            "device:revoke",
            clientRequestId,
            new { deviceRecordId });
        if (!idempotency.ShouldExecute)
        {
            await tx.CommitAsync();
            return Replay<bool>(idempotency);
        }

        await connection.ExecuteAsync(
            @"UPDATE ""CloudDevices""
              SET ""Status"" = @status, ""RevokedByUserId"" = @actorUserId, ""RevokedAt"" = @now, ""UpdatedAt"" = @now
              WHERE ""Id"" = @deviceRecordId;",
            new { status = CloudDeviceStatus.Revoked, actorUserId, now, deviceRecordId }, tx);
        await connection.ExecuteAsync(
            @"UPDATE ""CloudRefreshTokens""
              SET ""RevokedAt"" = @now, ""RevokedReason"" = 'DeviceRevoked'
              WHERE ""UserId"" = @userId AND ""DeviceId"" = @deviceId AND ""RevokedAt"" IS NULL;",
            new { now, userId = device.UserId, device.DeviceId }, tx);
        await _auditLogService.LogAsync(connection, tx, actorMembership.WorkspaceId, "DeviceRevoked", "device", deviceRecordId, null, null, reason: null, clientRequestId);
        await CompleteIdempotencyAsync(connection, tx, actorMembership.WorkspaceId, actorUserId, "device:revoke", clientRequestId, true, "device", deviceRecordId);
        await tx.CommitAsync();
        return true;
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
        if (string.IsNullOrWhiteSpace(_options.BootstrapAdminPassword) || _options.BootstrapAdminPassword.Length < 12)
        {
            throw new InvalidOperationException("ORDERLY_BOOTSTRAP_ADMIN_PASSWORD must be set to a strong one-time initial password.");
        }

        var passwordHash = _passwordHasher.HashPassword(_options.BootstrapAdminPassword);

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

    private async Task<CloudWorkspaceMemberRecord?> GetAnyMembershipForUserAsync(System.Data.Common.DbConnection connection, Guid userId)
    {
        return await connection.QueryFirstOrDefaultAsync<CloudWorkspaceMemberRecord>(
            "SELECT * FROM \"CloudWorkspaceMembers\" WHERE \"UserId\" = @userId ORDER BY \"CreatedAt\" LIMIT 1;",
            new { userId });
    }

    private async Task RecordLoginFailureAsync(
        System.Data.Common.DbConnection connection,
        CloudUserRecord? user,
        string username,
        string reason,
        string? clientRequestId,
        string ipAddress,
        string userAgent)
    {
        var membership = user == null
            ? null
            : await GetAnyMembershipForUserAsync(connection, user.Id);
        Guid? workspaceId = membership?.WorkspaceId;
        Guid? userId = user?.Id;

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudLoginFailures"" (
                ""Id"", ""WorkspaceId"", ""UserId"", ""Username"", ""Reason"",
                ""ClientRequestId"", ""IpAddress"", ""UserAgent"", ""OccurredAt"")
              VALUES (
                @id, @workspaceId, @userId, @username, @reason,
                @clientRequestId, @ipAddress, @userAgent, @occurredAt);",
            new
            {
                id = Guid.NewGuid(),
                workspaceId,
                userId,
                username = string.IsNullOrWhiteSpace(username) ? "(empty)" : username.Trim(),
                reason,
                clientRequestId,
                ipAddress,
                userAgent,
                occurredAt = DateTime.UtcNow
            });

        if (membership == null || user == null) return;

        await _auditLogService.LogAsync(
            connection,
            null,
            membership.WorkspaceId,
            "LoginFailed",
            "user",
            user.Id,
            null,
            null,
            reason,
            clientRequestId,
            ipAddress,
            userAgent);
    }

    private static async Task RevokeRefreshTokenFamilyAsync(
        System.Data.Common.DbConnection connection,
        CloudRefreshTokenRecord tokenRecord,
        string reason)
    {
        var now = DateTime.UtcNow;
        await using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            @"UPDATE ""CloudRefreshTokens""
              SET ""RevokedAt"" = COALESCE(""RevokedAt"", @now),
                  ""RevokedReason"" = CASE WHEN ""RevokedAt"" IS NULL THEN @reason ELSE ""RevokedReason"" END
              WHERE ""TokenFamilyId"" = @familyId;",
            new { now, reason, familyId = tokenRecord.TokenFamilyId }, tx);
        await connection.ExecuteAsync(
            "UPDATE \"CloudUsers\" SET \"TokenVersion\" = \"TokenVersion\" + 1, \"UpdatedAt\" = @now WHERE \"Id\" = @userId;",
            new { now, userId = tokenRecord.UserId }, tx);
        await tx.CommitAsync();
    }

    private async Task<(string accessToken, string refreshToken)> GenerateTokensAsync(System.Data.Common.DbConnection connection, CloudUserRecord user, string deviceId)
    {
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Username, user.DisplayName, user.TokenVersion, deviceId);
        var refreshPlain = GenerateRefreshToken();
        var refreshHash = HashToken(refreshPlain);
        var tokenId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            "INSERT INTO \"CloudRefreshTokens\" (\"Id\", \"UserId\", \"TokenFamilyId\", \"TokenHash\", \"DeviceId\", \"CreatedAt\", \"ExpiresAt\") VALUES (@id, @userId, @familyId, @hash, @deviceId, @now, @expires);",
            new { id = tokenId, userId = user.Id, familyId, hash = refreshHash, deviceId, now, expires = now.AddDays(_options.RefreshTokenLifetimeDays) });
        return (accessToken, refreshPlain);
    }

    private async Task<CloudDeviceRecord?> EnsureApprovedLoginDeviceAsync(
        System.Data.Common.DbConnection connection,
        CloudUserRecord user,
        CloudWorkspaceMemberRecord membership,
        LoginRequest request,
        string ipAddress,
        string userAgent)
    {
        var deviceId = NormalizeDeviceId(request.DeviceId);
        var deviceName = NormalizeDeviceName(request.DeviceName);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await RecordLoginFailureAsync(connection, user, request.Username, "DeviceIdMissing", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        var now = DateTime.UtcNow;
        var existing = await connection.QueryFirstOrDefaultAsync<CloudDeviceRecord>(
            @"SELECT * FROM ""CloudDevices""
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId AND ""DeviceId"" = @deviceId;",
            new { workspaceId = membership.WorkspaceId, userId = user.Id, deviceId });

        if (existing != null)
        {
            await connection.ExecuteAsync(
                @"UPDATE ""CloudDevices""
                  SET ""DeviceName"" = @deviceName, ""LastSeenAt"" = @now,
                      ""LastIpAddress"" = @ipAddress, ""LastUserAgent"" = @userAgent, ""UpdatedAt"" = @now
                  WHERE ""Id"" = @id;",
                new { id = existing.Id, deviceName, now, ipAddress, userAgent });

            if (CloudDeviceStatus.IsActive(existing.Status))
            {
                existing.DeviceName = deviceName;
                existing.LastSeenAt = now;
                return existing;
            }

            await RecordLoginFailureAsync(connection, user, request.Username, $"Device{existing.Status}", request.ClientRequestId, ipAddress, userAgent);
            return null;
        }

        var userDeviceCount = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudDevices""
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId;",
            new { workspaceId = membership.WorkspaceId, userId = user.Id });
        var status = userDeviceCount == 0 ? CloudDeviceStatus.Approved : CloudDeviceStatus.Pending;
        var device = new CloudDeviceRecord
        {
            Id = Guid.NewGuid(),
            WorkspaceId = membership.WorkspaceId,
            UserId = user.Id,
            DeviceId = deviceId,
            DeviceName = deviceName,
            Status = status,
            FirstSeenAt = now,
            LastSeenAt = now,
            ApprovedByUserId = status == CloudDeviceStatus.Approved ? user.Id : null,
            ApprovedAt = status == CloudDeviceStatus.Approved ? now : null
        };

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudDevices"" (
                ""Id"", ""WorkspaceId"", ""UserId"", ""DeviceId"", ""DeviceName"", ""Status"",
                ""FirstSeenAt"", ""LastSeenAt"", ""ApprovedByUserId"", ""ApprovedAt"",
                ""LastIpAddress"", ""LastUserAgent"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (
                @Id, @WorkspaceId, @UserId, @DeviceId, @DeviceName, @Status,
                @FirstSeenAt, @LastSeenAt, @ApprovedByUserId, @ApprovedAt,
                @ipAddress, @userAgent, @now, @now);",
            new
            {
                device.Id,
                device.WorkspaceId,
                device.UserId,
                device.DeviceId,
                device.DeviceName,
                device.Status,
                device.FirstSeenAt,
                device.LastSeenAt,
                device.ApprovedByUserId,
                device.ApprovedAt,
                ipAddress,
                userAgent,
                now
            });

        await _auditLogService.LogAsync(
            connection,
            null,
            membership.WorkspaceId,
            status == CloudDeviceStatus.Approved ? "DeviceFirstActivated" : "DeviceApprovalRequired",
            "device",
            device.Id,
            null,
            JsonSerializer.Serialize(new { deviceId, deviceName, status }),
            reason: null,
            request.ClientRequestId,
            ipAddress,
            userAgent);

        if (status == CloudDeviceStatus.Approved)
        {
            return device;
        }

        await RecordLoginFailureAsync(connection, user, request.Username, "DevicePendingApproval", request.ClientRequestId, ipAddress, userAgent);
        return null;
    }

    private async Task<CloudInvitationDto?> GetInvitationDtoAsync(System.Data.Common.DbConnection connection, Guid invitationId)
    {
        return await connection.QueryFirstOrDefaultAsync<CloudInvitationDto>(
            @"SELECT i.""Id"", i.""WorkspaceId"", i.""Code"", i.""CloudRole"", i.""BusinessLabel"", i.""Status"",
                     i.""MaxUses"", i.""UsedCount"", i.""ExpiresAt"" AS ""ExpiresAtUtc"",
                     i.""CreatedAt"" AS ""CreatedAtUtc"", COALESCE(u.""DisplayName"", '') AS ""CreatedByDisplayName""
              FROM ""CloudInvitations"" i
              LEFT JOIN ""CloudUsers"" u ON u.""Id"" = i.""CreatedByUserId""
              WHERE i.""Id"" = @invitationId;",
            new { invitationId });
    }

    private async Task<CloudUserApplicationDto?> GetApplicationDtoAsync(System.Data.Common.DbConnection connection, Guid applicationId)
    {
        return await connection.QueryFirstOrDefaultAsync<CloudUserApplicationDto>(
            ApplicationDtoSelectSql + @" WHERE a.""Id"" = @applicationId;",
            new { applicationId });
    }

    private const string ApplicationDtoSelectSql = @"
        SELECT a.""Id"", a.""WorkspaceId"", a.""InvitationId"", a.""InviteCode"",
               a.""Username"", a.""DisplayName"", a.""Status"",
               a.""RequestedDeviceId"", a.""RequestedDeviceName"",
               a.""RequestedAt"" AS ""RequestedAtUtc"", a.""ReviewedAt"" AS ""ReviewedAtUtc"",
               COALESCE(reviewer.""DisplayName"", '') AS ""ReviewedByDisplayName"",
               COALESCE(a.""ReviewReason"", '') AS ""ReviewReason"",
               a.""CreatedUserId""
        FROM ""CloudUserApplications"" a
        LEFT JOIN ""CloudUsers"" reviewer ON reviewer.""Id"" = a.""ReviewedByUserId""";

    private const string DeviceDtoSelectSql = @"
        SELECT d.""Id"", d.""WorkspaceId"", d.""UserId"", u.""Username"", u.""DisplayName"",
               d.""DeviceId"", d.""DeviceName"", d.""Status"",
               d.""FirstSeenAt"" AS ""FirstSeenAtUtc"", d.""LastSeenAt"" AS ""LastSeenAtUtc"",
               d.""ApprovedAt"" AS ""ApprovedAtUtc"", COALESCE(approver.""DisplayName"", '') AS ""ApprovedByDisplayName"",
               d.""RevokedAt"" AS ""RevokedAtUtc"", COALESCE(revoker.""DisplayName"", '') AS ""RevokedByDisplayName""
        FROM ""CloudDevices"" d
        JOIN ""CloudUsers"" u ON u.""Id"" = d.""UserId""
        LEFT JOIN ""CloudUsers"" approver ON approver.""Id"" = d.""ApprovedByUserId""
        LEFT JOIN ""CloudUsers"" revoker ON revoker.""Id"" = d.""RevokedByUserId""";

    private static bool CanManageCloudAdministration(CloudWorkspaceMemberRecord? membership) =>
        membership != null
        && string.Equals(membership.CloudRole, CloudRole.Admin, StringComparison.OrdinalIgnoreCase)
        && (string.Equals(membership.BusinessLabel, BusinessLabel.Operator, StringComparison.Ordinal)
            || string.Equals(membership.BusinessLabel, BusinessLabel.Investor, StringComparison.Ordinal));

    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    private static string ComputeRequestHash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private async Task<IdempotencyBeginResult> TryBeginIdempotencyAsync<T>(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid workspaceId,
        Guid userId,
        string action,
        string clientRequestId,
        T request,
        CancellationToken cancellationToken = default)
    {
        return await _idempotency.TryBeginAsync(
            workspaceId,
            userId,
            action,
            clientRequestId,
            ComputeRequestHash(request),
            connection,
            transaction,
            cancellationToken);
    }

    private async Task CompleteIdempotencyAsync<T>(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid workspaceId,
        Guid userId,
        string action,
        string clientRequestId,
        T response,
        string? resourceType,
        Guid? resourceId,
        CancellationToken cancellationToken = default)
    {
        await _idempotency.CompleteAsync(
            workspaceId,
            userId,
            action,
            clientRequestId,
            200,
            JsonSerializer.Serialize(response, JsonOptions),
            resourceType,
            resourceId,
            connection,
            transaction,
            cancellationToken);
    }

    private static T Replay<T>(IdempotencyBeginResult beginResult)
        => JsonSerializer.Deserialize<T>(beginResult.ResponseBodyJson ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Idempotency replay could not be deserialized.");

    private static string GenerateInviteCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeInviteCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string NormalizeUsername(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeDisplayName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeDeviceId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeDeviceName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未命名设备" : value.Trim();

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

    private sealed class InvitationRow
    {
        public Guid Id { get; set; }
        public Guid WorkspaceId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string CloudRole { get; set; } = string.Empty;
        public string BusinessLabel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int MaxUses { get; set; }
        public int UsedCount { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    private sealed class ApplicationApprovalRow
    {
        public Guid Id { get; set; }
        public Guid WorkspaceId { get; set; }
        public Guid InvitationId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string RequestedDeviceId { get; set; } = string.Empty;
        public string RequestedDeviceName { get; set; } = string.Empty;
        public string CloudRole { get; set; } = string.Empty;
        public string BusinessLabel { get; set; } = string.Empty;
    }
}
