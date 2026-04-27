using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ActivityLogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ActivityLog> CreateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (activityLog.CreatedAt == default)
        {
            activityLog.CreatedAt = now;
        }

        activityLog.UpdatedAt = now;
        activityLog.DeletedAt = null;
        activityLog.IsSynced = false;
        activityLog.Version = Math.Max(1, activityLog.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ActivityLogs (
                Type, CustomerId, DealId, OrderId, Title, Description, Operator, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $type, $customerId, $dealId, $orderId, $title, $description, $operator, $metadataJson,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, activityLog);
        activityLog.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return activityLog;
    }

    public async Task<ActivityLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE DeletedAt IS NULL AND Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public Task<IReadOnlyList<ActivityLog>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY CreatedAt DESC", cancellationToken);
    }

    public Task<IReadOnlyList<ActivityLog>> ListRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY CreatedAt DESC LIMIT $count", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$count", Math.Max(1, count));
        });
    }

    public Task<IReadOnlyList<ActivityLog>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND CustomerId = $customerId ORDER BY CreatedAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public async Task UpdateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        activityLog.UpdatedAt = DateTime.Now;
        activityLog.IsSynced = false;
        activityLog.Version = Math.Max(1, activityLog.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ActivityLogs
            SET Type = $type,
                CustomerId = $customerId,
                DealId = $dealId,
                OrderId = $orderId,
                Title = $title,
                Description = $description,
                Operator = $operator,
                MetadataJson = $metadataJson,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, activityLog);
        command.Parameters.AddWithValue("$id", activityLog.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ActivityLogs
            SET DeletedAt = $deletedAt,
                UpdatedAt = $updatedAt,
                IsSynced = 0,
                Version = Version + 1
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$deletedAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ActivityLog>> QueryAsync(string whereClause, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {whereClause};";
        configure?.Invoke(command);

        var rows = new List<ActivityLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private const string SelectSql = """
        SELECT Id, Type, CustomerId, DealId, OrderId, Title, Description, Operator, MetadataJson,
               CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM ActivityLogs
        """;

    private static void AddParameters(SqliteCommand command, ActivityLog activityLog)
    {
        command.Parameters.AddWithValue("$type", (int)activityLog.Type);
        command.Parameters.AddWithValue("$customerId", ToDbInt(activityLog.CustomerId));
        command.Parameters.AddWithValue("$dealId", ToDbInt(activityLog.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(activityLog.OrderId));
        command.Parameters.AddWithValue("$title", activityLog.Title);
        command.Parameters.AddWithValue("$description", activityLog.Description);
        command.Parameters.AddWithValue("$operator", activityLog.Operator);
        command.Parameters.AddWithValue("$metadataJson", activityLog.MetadataJson);
        command.Parameters.AddWithValue("$createdAt", activityLog.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", activityLog.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(activityLog.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", activityLog.RemoteId);
        command.Parameters.AddWithValue("$isSynced", activityLog.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", activityLog.Version);
    }

    private static ActivityLog Map(SqliteDataReader reader)
    {
        return new ActivityLog
        {
            Id = reader.GetInt32(0),
            Type = (ActivityType)reader.GetInt32(1),
            CustomerId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DealId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            OrderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Title = reader.GetString(5),
            Description = reader.GetString(6),
            Operator = reader.GetString(7),
            MetadataJson = reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(12),
            IsSynced = reader.GetInt32(13) == 1,
            Version = reader.GetInt32(14)
        };
    }

    private static object ToDbInt(int? value)
    {
        return value is null ? DBNull.Value : value.Value;
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }
}
