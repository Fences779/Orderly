namespace Orderly.Contracts.Auth;

public sealed class SubmitUserApplicationRequest
{
    public string InviteCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InitialPassword { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
}
