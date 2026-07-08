namespace Orderly.Contracts.Auth;

public sealed class ReviewUserApplicationRequest
{
    public string Reason { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
}
