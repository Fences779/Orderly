namespace Orderly.Contracts.Auth;

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public CloudUserDto User { get; set; } = new();
    public CloudWorkspaceMembershipDto WorkspaceMembership { get; set; } = new();
    public DateTime ServerTimeUtc { get; set; }
}
