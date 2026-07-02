namespace Orderly.Contracts.Commerce;

public sealed class CreateCustomerCommand : WriteCommandBase
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WeChat { get; set; }
    public string? Email { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
