namespace Orderly.Contracts.Auth;

public sealed class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
}
