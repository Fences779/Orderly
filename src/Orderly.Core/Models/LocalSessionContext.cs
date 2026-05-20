namespace Orderly.Core.Models;

public sealed class LocalSessionContext
{
    public string AccountId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public LocalAccountRole Role { get; init; }
    public string DatabasePath { get; init; } = string.Empty;
    public byte[] DataKey { get; init; } = [];
    public DateTime SignedInAt { get; init; } = DateTime.Now;
}
