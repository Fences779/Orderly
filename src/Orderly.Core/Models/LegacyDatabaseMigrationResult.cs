namespace Orderly.Core.Models;

public sealed class LegacyDatabaseMigrationResult
{
    public LegacyDatabaseMigrationPlan Plan { get; init; } = new();
    public bool Copied { get; init; }
    public bool Overwritten { get; init; }
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.Now;
}
