using Orderly.Contracts.Auth;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface ICloudAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, string ipAddress, string userAgent);
    Task<LoginResponse?> RefreshAsync(RefreshRequest request);
    Task<CloudUserRecord?> GetUserAsync(Guid userId);
    Task<CloudWorkspaceMemberRecord?> GetMembershipAsync(Guid userId);
    Task<bool> ValidateTokenVersionAsync(Guid userId, int tokenVersion);
    Task<CloudUserRecord?> CreateUserAsync(CreateUserRequest request, Guid actorUserId);
    Task<bool> DisableUserAsync(Guid userId, Guid actorUserId);
    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<bool> ResetPasswordAsync(Guid userId, string newPassword, Guid actorUserId, string? clientRequestId = null);
    Task InvalidateSessionsAsync(Guid userId);
    Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync(Guid workspaceId);
    Task<IReadOnlyList<CloudLoginFailureDto>> ListLoginFailuresAsync(Guid workspaceId, int limit = 100);
    Task EnsureBootstrapAdminAsync(string bootstrapToken);
}
