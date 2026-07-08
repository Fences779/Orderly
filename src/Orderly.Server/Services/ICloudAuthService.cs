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
    Task<bool> ValidateDeviceAccessAsync(Guid userId, string deviceId);
    Task<CloudUserRecord?> CreateUserAsync(CreateUserRequest request, Guid actorUserId);
    Task<bool> DisableUserAsync(Guid userId, Guid actorUserId);
    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<bool> ResetPasswordAsync(Guid userId, string newPassword, Guid actorUserId, string? clientRequestId = null);
    Task InvalidateSessionsAsync(Guid userId);
    Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync(Guid workspaceId);
    Task<IReadOnlyList<CloudLoginFailureDto>> ListLoginFailuresAsync(Guid workspaceId, int limit = 100);
    Task<CloudInvitationDto?> CreateInvitationAsync(CreateInvitationRequest request, Guid actorUserId);
    Task<IReadOnlyList<CloudInvitationDto>> ListInvitationsAsync(Guid workspaceId);
    Task<CloudUserApplicationDto?> SubmitApplicationAsync(SubmitUserApplicationRequest request, string ipAddress, string userAgent);
    Task<IReadOnlyList<CloudUserApplicationDto>> ListApplicationsAsync(Guid workspaceId, int limit = 100);
    Task<CloudUserApplicationDto?> ApproveApplicationAsync(Guid applicationId, Guid actorUserId, string? reason, string? clientRequestId = null);
    Task<CloudUserApplicationDto?> RejectApplicationAsync(Guid applicationId, Guid actorUserId, string? reason, string? clientRequestId = null);
    Task<IReadOnlyList<CloudDeviceDto>> ListDevicesAsync(Guid workspaceId, Guid actorUserId);
    Task<bool> ApproveDeviceAsync(Guid deviceRecordId, Guid actorUserId, string? clientRequestId = null);
    Task<bool> RevokeDeviceAsync(Guid deviceRecordId, Guid actorUserId, string? clientRequestId = null);
    Task EnsureBootstrapAdminAsync(string bootstrapToken);
}
