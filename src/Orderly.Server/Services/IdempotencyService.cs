using System.Data;
using Dapper;

namespace Orderly.Server.Services;

public sealed class IdempotencyService : IIdempotencyService
{
    public async Task<IdempotencyBeginResult> TryBeginAsync(
        Guid workspaceId,
        Guid userId,
        string action,
        string clientRequestId,
        string requestHash,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        const string selectSql = @"
            SELECT ""Status"", ""RequestHash"", ""ResponseStatusCode"", ""ResponseBodyJson"",
                   ""ResourceType"", ""ResourceId""
            FROM ""CloudIdempotencyKeys""
            WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId
              AND ""Action"" = @action AND ""ClientRequestId"" = @clientRequestId
            FOR UPDATE;";

        const string insertSql = @"
            INSERT INTO ""CloudIdempotencyKeys"" (
                ""WorkspaceId"", ""UserId"", ""Action"", ""ClientRequestId"",
                ""RequestHash"", ""Status"", ""CreatedAt"")
            VALUES (
                @workspaceId, @userId, @action, @clientRequestId,
                @requestHash, 'Pending', @now)
            ON CONFLICT (""WorkspaceId"", ""UserId"", ""Action"", ""ClientRequestId"") DO NOTHING
            RETURNING TRUE;";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var existing = await connection.QueryFirstOrDefaultAsync<IdempotencyRow>(
                selectSql,
                new { workspaceId, userId, action, clientRequestId },
                transaction);

            if (existing != null)
            {
                return ResolveExisting(existing, requestHash);
            }

            var inserted = await connection.ExecuteScalarAsync<bool?>(insertSql, new
            {
                workspaceId,
                userId,
                action,
                clientRequestId,
                requestHash,
                now = DateTime.UtcNow
            }, transaction);

            if (inserted == true)
            {
                return IdempotencyBeginResult.Execute();
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException("幂等键处于 Pending 状态且无法在合理时间内完成，请稍后重试。");
    }

    public async Task CompleteAsync(
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
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE ""CloudIdempotencyKeys""
            SET ""Status"" = 'Completed',
                ""ResponseStatusCode"" = @responseStatusCode,
                ""ResponseBodyJson"" = @responseBodyJson,
                ""ResourceType"" = @resourceType,
                ""ResourceId"" = @resourceId,
                ""CompletedAt"" = @now
            WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId
              AND ""Action"" = @action AND ""ClientRequestId"" = @clientRequestId;";

        await connection.ExecuteAsync(sql, new
        {
            workspaceId,
            userId,
            action,
            clientRequestId,
            responseStatusCode,
            responseBodyJson,
            resourceType,
            resourceId,
            now = DateTime.UtcNow
        }, transaction);
    }

    private sealed class IdempotencyRow
    {
        public string Status { get; set; } = string.Empty;
        public string RequestHash { get; set; } = string.Empty;
        public int? ResponseStatusCode { get; set; }
        public string? ResponseBodyJson { get; set; }
        public string? ResourceType { get; set; }
        public Guid? ResourceId { get; set; }
    }

    private static IdempotencyBeginResult ResolveExisting(IdempotencyRow row, string requestHash)
    {
        if (!string.Equals(row.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("同一个 ClientRequestId 被复用但请求内容不一致。");
        }

        if (row.Status == "Completed")
        {
            return IdempotencyBeginResult.Replay(
                row.ResponseStatusCode ?? 200,
                row.ResponseBodyJson ?? string.Empty,
                row.ResourceType,
                row.ResourceId);
        }

        throw new InvalidOperationException("幂等键处于 Pending 状态且无法在合理时间内完成，请稍后重试。");
    }
}
