namespace Orderly.Server.Services;

public sealed class ConflictException : Exception
{
    public string? ActorDisplayName { get; }
    public DateTime? UpdatedAt { get; }
    public long? LatestRevision { get; }

    public ConflictException(string message) : base(message) { }

    public ConflictException(string message, string? actorDisplayName, DateTime? updatedAt, long? latestRevision)
        : base(message)
    {
        ActorDisplayName = actorDisplayName;
        UpdatedAt = updatedAt;
        LatestRevision = latestRevision;
    }
}
