namespace Orderly.Contracts.Auth;

public sealed class UserSummaryDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CloudRole { get; set; } = string.Empty;
    public string BusinessLabel { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string CreatedByDisplayName { get; set; } = string.Empty;
}
