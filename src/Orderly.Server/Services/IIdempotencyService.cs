using System.Data;

namespace Orderly.Server.Services;

public interface IIdempotencyService
{
    /// <summary>
    /// Attempts to begin an idempotent action. Returns a result indicating whether the caller should
    /// execute the action, or whether a previous completed response should be replayed.
    /// </summary>
    Task<IdempotencyBeginResult> TryBeginAsync(
        Guid workspaceId,
        Guid userId,
        string action,
        string clientRequestId,
        string requestHash,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the idempotent action as completed so subsequent identical requests replay the response.
    /// </summary>
    Task CompleteAsync(
        Guid workspaceId,
        Guid userId,
        string action,
        string clientRequestId,
        int responseStatusCode,
        string responseBodyJson,
        string? resourceType,
        Guid? resourceId,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}

public sealed class IdempotencyBeginResult
{
    public bool ShouldExecute { get; }
    public int? ResponseStatusCode { get; }
    public string? ResponseBodyJson { get; }
    public string? ResourceType { get; }
    public Guid? ResourceId { get; }

    public static IdempotencyBeginResult Execute() => new(true, null, null, null, null);
    public static IdempotencyBeginResult Replay(int statusCode, string responseBodyJson, string? resourceType, Guid? resourceId)
        => new(false, statusCode, responseBodyJson, resourceType, resourceId);

    private IdempotencyBeginResult(bool shouldExecute, int? statusCode, string? body, string? resourceType, Guid? resourceId)
    {
        ShouldExecute = shouldExecute;
        ResponseStatusCode = statusCode;
        ResponseBodyJson = body;
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}
