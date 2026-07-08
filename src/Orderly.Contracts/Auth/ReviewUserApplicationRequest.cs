namespace Orderly.Contracts.Auth;

public sealed class ReviewUserApplicationRequest
{
    public string Reason { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string IdempotencyKey
    {
        get => ClientRequestId;
        set => ClientRequestId = value;
    }
}
