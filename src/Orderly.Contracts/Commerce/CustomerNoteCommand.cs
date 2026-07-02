namespace Orderly.Contracts.Commerce;

public sealed class CustomerNoteCommand : WriteCommandBase
{
    public string Note { get; set; } = string.Empty;
}
