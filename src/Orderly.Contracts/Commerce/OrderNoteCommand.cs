namespace Orderly.Contracts.Commerce;

public sealed class OrderNoteCommand : WriteCommandBase
{
    public Guid OrderId { get; set; }
    public string Note { get; set; } = string.Empty;
}
