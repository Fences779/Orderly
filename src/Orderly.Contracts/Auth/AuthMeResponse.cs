namespace Orderly.Contracts.Auth;

public sealed class AuthMeResponse
{
    public CloudUserDto User { get; set; } = new();
    public CloudWorkspaceMembershipDto WorkspaceMembership { get; set; } = new();
}
