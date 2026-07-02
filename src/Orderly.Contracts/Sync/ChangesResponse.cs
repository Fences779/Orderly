namespace Orderly.Contracts.Sync;

public sealed class ChangesResponse
{
    public long FromSequence { get; set; }
    public long ToSequence { get; set; }
    public bool FullResyncRequired { get; set; }
    public IReadOnlyList<ChangeLogEntryDto> Changes { get; set; } = Array.Empty<ChangeLogEntryDto>();
}
