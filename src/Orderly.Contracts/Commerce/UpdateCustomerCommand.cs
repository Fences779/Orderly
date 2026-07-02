namespace Orderly.Contracts.Commerce;

public sealed class UpdateCustomerCommand : WriteCommandBase
{
    public Guid CustomerId { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? WeChat { get; set; }
    public string? Email { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
