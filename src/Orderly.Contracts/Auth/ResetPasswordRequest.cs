namespace Orderly.Contracts.Auth;

public sealed class ResetPasswordRequest
{
    public Guid UserId { get; set; }
    public string NewPassword { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string IdempotencyKey
    {
        get => ClientRequestId;
        set => ClientRequestId = value;
    }
}
