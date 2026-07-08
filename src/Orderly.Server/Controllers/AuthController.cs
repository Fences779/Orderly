using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Permissions;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ICloudAuthService _authService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICloudPermissionService _permissions;

    public AuthController(ICloudAuthService authService, ICurrentUserContext currentUser, ICloudPermissionService permissions)
    {
        _authService = authService;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> LoginAsync([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request, GetIp(), GetUserAgent());
        if (response == null)
            return Unauthorized(new { Error = "Invalid username or password, or account is locked/disabled." });
        return Ok(response);
    }

    [HttpPost("applications")]
    [AllowAnonymous]
    public async Task<ActionResult<CloudUserApplicationDto>> SubmitApplicationAsync([FromBody] SubmitUserApplicationRequest request)
    {
        var application = await _authService.SubmitApplicationAsync(request, GetIp(), GetUserAgent());
        if (application == null)
            return BadRequest(new { Error = "Invalid invitation, username, password, or device information." });
        return Ok(application);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> RefreshAsync([FromBody] RefreshRequest request)
    {
        var response = await _authService.RefreshAsync(request);
        if (response == null) return Unauthorized();
        return Ok(response);
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Access tokens are short-lived; refresh tokens are rotated/revoked on the server.
        return NoContent();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAllAsync()
    {
        if (_currentUser.UserId is not Guid userId) return Unauthorized();
        await _authService.InvalidateSessionsAsync(userId);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthMeResponse>> MeAsync()
    {
        if (_currentUser.UserId is not Guid userId) return Unauthorized();
        var user = await _authService.GetUserAsync(userId);
        var membership = await _authService.GetMembershipAsync(userId);
        if (user == null || membership == null) return Unauthorized();
        return Ok(new AuthMeResponse
        {
            User = new CloudUserDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                IsEnabled = user.IsEnabled,
                CreatedAtUtc = user.CreatedAt,
                UpdatedAtUtc = user.UpdatedAt
            },
            WorkspaceMembership = new CloudWorkspaceMembershipDto
            {
                WorkspaceId = membership.WorkspaceId,
                UserId = membership.UserId,
                CloudRole = membership.CloudRole,
                BusinessLabel = membership.BusinessLabel,
                RolePolicyVersion = membership.RolePolicyVersion,
                IsEnabled = membership.IsEnabled
            }
        });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request)
    {
        if (_currentUser.UserId is not Guid userId) return Unauthorized();
        var ok = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        if (!ok) return BadRequest(new { Error = "Current password is incorrect." });
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordRequest request)
    {
        if (_currentUser.UserId is not Guid userId) return Unauthorized();
        var membership = await _authService.GetMembershipAsync(userId);
        if (membership == null || !_permissions.IsAdmin(membership)) return Forbid();

        var ok = await _authService.ResetPasswordAsync(request.UserId, request.NewPassword, userId, request.ClientRequestId);
        if (!ok) return BadRequest(new { Error = "Cannot reset password for this user." });
        return NoContent();
    }

    private string GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string GetUserAgent() => HttpContext.Request.Headers.UserAgent.ToString();
}
