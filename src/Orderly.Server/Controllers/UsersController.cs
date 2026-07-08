using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Auth;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

public class UsersController : CloudControllerBase
{
    public UsersController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> ListUsersAsync()
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var users = await AuthService.ListUsersAsync(membership.WorkspaceId);
        return Ok(users);
    }

    [HttpGet("users/login-failures")]
    public async Task<ActionResult<IReadOnlyList<CloudLoginFailureDto>>> ListLoginFailuresAsync([FromQuery] int limit = 100)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var failures = await AuthService.ListLoginFailuresAsync(membership.WorkspaceId, limit);
        return Ok(failures);
    }

    [HttpGet("users/invitations")]
    public async Task<ActionResult<IReadOnlyList<CloudInvitationDto>>> ListInvitationsAsync()
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var invitations = await AuthService.ListInvitationsAsync(membership.WorkspaceId);
        return Ok(invitations);
    }

    [HttpPost("users/invitations")]
    public async Task<ActionResult<CloudInvitationDto>> CreateInvitationAsync([FromBody] CreateInvitationRequest request)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var invitation = await AuthService.CreateInvitationAsync(request, UserId);
        if (invitation == null) return BadRequest(new { Error = "Invalid invitation request or duplicate code." });
        return Ok(invitation);
    }

    [HttpGet("users/applications")]
    public async Task<ActionResult<IReadOnlyList<CloudUserApplicationDto>>> ListApplicationsAsync([FromQuery] int limit = 100)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var applications = await AuthService.ListApplicationsAsync(membership.WorkspaceId, limit);
        return Ok(applications);
    }

    [HttpPost("users/applications/{applicationId:guid}/approve")]
    public async Task<ActionResult<CloudUserApplicationDto>> ApproveApplicationAsync(Guid applicationId, [FromBody] ReviewUserApplicationRequest request)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var application = await AuthService.ApproveApplicationAsync(applicationId, UserId, request.Reason, request.ClientRequestId);
        if (application == null) return BadRequest(new { Error = "Application cannot be approved." });
        return Ok(application);
    }

    [HttpPost("users/applications/{applicationId:guid}/reject")]
    public async Task<ActionResult<CloudUserApplicationDto>> RejectApplicationAsync(Guid applicationId, [FromBody] ReviewUserApplicationRequest request)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var application = await AuthService.RejectApplicationAsync(applicationId, UserId, request.Reason, request.ClientRequestId);
        if (application == null) return BadRequest(new { Error = "Application cannot be rejected." });
        return Ok(application);
    }

    [HttpGet("users/devices")]
    public async Task<ActionResult<IReadOnlyList<CloudDeviceDto>>> ListDevicesAsync()
    {
        var membership = await GetMembershipAsync();
        var devices = await AuthService.ListDevicesAsync(membership.WorkspaceId, UserId);
        return Ok(devices);
    }

    [HttpPost("users/devices/{deviceRecordId:guid}/approve")]
    public async Task<IActionResult> ApproveDeviceAsync(Guid deviceRecordId, [FromBody] ReviewUserApplicationRequest request)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var ok = await AuthService.ApproveDeviceAsync(deviceRecordId, UserId, request.ClientRequestId);
        if (!ok) return BadRequest(new { Error = "Device cannot be approved." });
        return NoContent();
    }

    [HttpPost("users/devices/{deviceRecordId:guid}/revoke")]
    public async Task<IActionResult> RevokeDeviceAsync(Guid deviceRecordId, [FromBody] ReviewUserApplicationRequest request)
    {
        var ok = await AuthService.RevokeDeviceAsync(deviceRecordId, UserId, request.ClientRequestId);
        if (!ok) return BadRequest(new { Error = "Device cannot be revoked." });
        return NoContent();
    }

    [HttpPost("users")]
    public async Task<ActionResult<CloudUserDto>> CreateUserAsync([FromBody] CreateUserRequest request)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var user = await AuthService.CreateUserAsync(request, UserId);
        if (user == null) return BadRequest(new { Error = "Invalid role/label or username already exists." });
        return Ok(new CloudUserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsEnabled = user.IsEnabled,
            CreatedAtUtc = user.CreatedAt,
            UpdatedAtUtc = user.UpdatedAt
        });
    }

    [HttpPatch("users/{userId:guid}/disable")]
    public async Task<IActionResult> DisableUserAsync(Guid userId)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        var ok = await AuthService.DisableUserAsync(userId, UserId);
        if (!ok) return BadRequest(new { Error = "Cannot disable the last admin or yourself." });
        return NoContent();
    }

    [HttpPost("users/{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPasswordAsync(Guid userId, [FromBody] ResetPasswordRequest request)
    {
        var membership = await GetMembershipAsync();
        if (!Permissions.CanManageUsers(membership)) return Forbid();
        if (request.UserId != Guid.Empty && request.UserId != userId)
        {
            return BadRequest(new { Error = "Request user id does not match route user id." });
        }

        var ok = await AuthService.ResetPasswordAsync(userId, request.NewPassword, UserId, request.ClientRequestId);
        if (!ok) return BadRequest(new { Error = "Cannot reset password for this user." });
        return NoContent();
    }
}
