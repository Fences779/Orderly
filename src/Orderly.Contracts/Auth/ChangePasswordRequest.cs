namespace Orderly.Contracts.Auth;

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string IdempotencyKey
    {
        get => ClientRequestId;
        set => ClientRequestId = value;
    }
}
