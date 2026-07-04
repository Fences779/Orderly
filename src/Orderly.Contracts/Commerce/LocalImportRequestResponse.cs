namespace Orderly.Contracts.Commerce;

public sealed class LocalImportDryRunRequest
{
    public Guid SourceInstanceId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public LocalImportPackage Package { get; set; } = new();
}

public sealed class LocalImportCommitRequest
{
    public Guid DryRunBatchId { get; set; }
    public Guid SourceInstanceId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
}

public sealed class LocalImportCounts
{
    public int Products { get; set; }
    public int Customers { get; set; }
    public int InventoryItems { get; set; }
    public int Orders { get; set; }
    public int OrderItems { get; set; }
    public int PaymentRecords { get; set; }
    public int CashFlowEntries { get; set; }
    public int ExistingMapped { get; set; }
    public int NewRecords { get; set; }
}

public sealed class LocalImportIssue
{
    public string EntityType { get; set; } = string.Empty;
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class LocalImportDryRunResponse
{
    public Guid DryRunBatchId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public LocalImportCounts Counts { get; set; } = new();
    public List<LocalImportIssue> Issues { get; set; } = new();
    public bool CanCommit { get; set; }
}

public sealed class LocalImportCommitResponse
{
    public Guid BatchId { get; set; }
    public string Status { get; set; } = string.Empty;
    public LocalImportCounts Imported { get; set; } = new();
    public List<LocalImportIssue> Failures { get; set; } = new();
}

public sealed class LocalImportBatchStatusDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SourceInstanceId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime DryRunAtUtc { get; set; }
    public DateTime? CommittedAtUtc { get; set; }
}
