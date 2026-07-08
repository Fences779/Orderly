namespace Orderly.Contracts.Auth;

public sealed class CreateInvitationRequest
{
    public string Code { get; set; } = string.Empty;
    public string CloudRole { get; set; } = string.Empty;
    public string BusinessLabel { get; set; } = string.Empty;
    public int MaxUses { get; set; } = 1;
    public DateTime? ExpiresAtUtc { get; set; }
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string IdempotencyKey
    {
        get => ClientRequestId;
        set => ClientRequestId = value;
    }
}
