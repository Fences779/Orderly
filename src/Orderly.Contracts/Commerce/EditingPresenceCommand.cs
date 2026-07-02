namespace Orderly.Contracts.Commerce;

public sealed class EditingPresenceCommand
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
}
