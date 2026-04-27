namespace Orderly.Core.Models;

public abstract class EntityBase
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime? DeletedAt { get; set; }
    public string RemoteId { get; set; } = string.Empty;
    public bool IsSynced { get; set; }
    public int Version { get; set; } = 1;
}
