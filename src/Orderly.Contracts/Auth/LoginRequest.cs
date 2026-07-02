namespace Orderly.Contracts.Auth;

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
}
