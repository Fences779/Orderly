namespace Orderly.Core.Models;

public sealed class LocalAccountSummary
{
    public string AccountId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public LocalAccountRole Role { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public bool IsMostRecentlyLoggedIn { get; init; }
}
