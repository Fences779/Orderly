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
