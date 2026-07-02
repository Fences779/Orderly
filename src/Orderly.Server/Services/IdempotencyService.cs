using System.Data;
using Dapper;
using Npgsql;

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
        const string insertSql = @"
            INSERT INTO ""CloudIdempotencyKeys"" (
                ""WorkspaceId"", ""UserId"", ""Action"", ""ClientRequestId"",
                ""RequestHash"", ""Status"", ""CreatedAt"")
            VALUES (
                @workspaceId, @userId, @action, @clientRequestId,
                @requestHash, 'Pending', @now);";

        try
        {
            await connection.ExecuteAsync(insertSql, new
            {
                workspaceId,
                userId,
                action,
                clientRequestId,
                requestHash,
                now = DateTime.UtcNow
            }, transaction);
            return IdempotencyBeginResult.Execute();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Another request may be in flight or already completed. Lock the row to wait for it,
            // then replay the stored outcome. If the other transaction rolled back, the row vanishes
            // and we retry the insert.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                const string selectSql = @"
                    SELECT ""Status"", ""RequestHash"", ""ResponseStatusCode"", ""ResponseBodyJson"",
                           ""ResourceType"", ""ResourceId""
                    FROM ""CloudIdempotencyKeys""
                    WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId
                      AND ""Action"" = @action AND ""ClientRequestId"" = @clientRequestId
                    FOR UPDATE;";

                var row = await connection.QueryFirstOrDefaultAsync<IdempotencyRow>(
                    selectSql,
                    new { workspaceId, userId, action, clientRequestId },
                    transaction);

                if (row == null)
                {
                    // The other transaction rolled back; try to claim the key ourselves.
                    try
                    {
                        await connection.ExecuteAsync(insertSql, new
                        {
                            workspaceId,
                            userId,
                            action,
                            clientRequestId,
                            requestHash,
                            now = DateTime.UtcNow
                        }, transaction);
                        return IdempotencyBeginResult.Execute();
                    }
                    catch (PostgresException retryEx) when (retryEx.SqlState == PostgresErrorCodes.UniqueViolation)
                    {
                        continue;
                    }
                }

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

                // Status is Pending and we hold the row lock: the other request is still running.
                // This should be rare because we waited for it above; retry the loop.
                await Task.Delay(50, cancellationToken);
            }

            throw new InvalidOperationException("幂等键处于 Pending 状态且无法在合理时间内完成，请稍后重试。");
        }
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
}
