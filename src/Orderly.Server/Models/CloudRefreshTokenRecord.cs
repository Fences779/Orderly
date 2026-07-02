namespace Orderly.Server.Models;

public sealed class CloudRefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TokenFamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
