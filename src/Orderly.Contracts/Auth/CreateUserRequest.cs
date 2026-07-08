namespace Orderly.Contracts.Auth;

public sealed class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InitialPassword { get; set; } = string.Empty;
    public string CloudRole { get; set; } = string.Empty;
    public string BusinessLabel { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string IdempotencyKey
    {
        get => ClientRequestId;
        set => ClientRequestId = value;
    }
}
