using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public abstract class CloudEntityDto
{
    public Guid Id { get; set; }
    public long Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public EntityLifecycleStatus Lifecycle { get; set; }
    public string? CustomFieldsJson { get; set; }
}
