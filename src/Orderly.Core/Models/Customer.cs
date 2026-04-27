namespace Orderly.Core.Models;

public sealed class Customer : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public CustomerStatus Status { get; set; }
    public CustomerPriority Priority { get; set; } = CustomerPriority.Normal;
    public string SourcePlatform { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ContactHandle { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public DateTime? LastContactAt { get; set; }
}
