using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class FollowUpRepository : IFollowUpRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public FollowUpRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FollowUp> CreateAsync(FollowUp followUp, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (followUp.CreatedAt == default)
        {
            followUp.CreatedAt = now;
        }

        followUp.UpdatedAt = now;
        followUp.DeletedAt = null;
        followUp.IsSynced = false;
        followUp.Version = Math.Max(1, followUp.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FollowUps (
                CustomerId, DealId, OrderId, Title, Content, Status, ScheduledAt, CompletedAt, ReminderAt,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $title, $content, $status, $scheduledAt, $completedAt, $reminderAt,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, followUp);
        followUp.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return followUp;
    }

    public async Task<FollowUp?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE DeletedAt IS NULL AND Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public Task<IReadOnlyList<FollowUp>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY ScheduledAt ASC", cancellationToken);
    }

    public Task<IReadOnlyList<FollowUp>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND CustomerId = $customerId ORDER BY ScheduledAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public Task<IReadOnlyList<FollowUp>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND Status IN (0, 1, 5) ORDER BY ScheduledAt ASC", cancellationToken);
    }

    public async Task UpdateAsync(FollowUp followUp, CancellationToken cancellationToken = default)
    {
        followUp.UpdatedAt = DateTime.Now;
        followUp.IsSynced = false;
        followUp.Version = Math.Max(1, followUp.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE FollowUps
            SET CustomerId = $customerId,
                DealId = $dealId,
                OrderId = $orderId,
                Title = $title,
                Content = $content,
                Status = $status,
                ScheduledAt = $scheduledAt,
                CompletedAt = $completedAt,
                ReminderAt = $reminderAt,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, followUp);
        command.Parameters.AddWithValue("$id", followUp.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE FollowUps
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

    private async Task<IReadOnlyList<FollowUp>> QueryAsync(string whereClause, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {whereClause};";
        configure?.Invoke(command);

        var rows = new List<FollowUp>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private const string SelectSql = """
        SELECT Id, CustomerId, DealId, OrderId, Title, Content, Status, ScheduledAt, CompletedAt, ReminderAt,
               CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM FollowUps
        """;

    private static void AddParameters(SqliteCommand command, FollowUp followUp)
    {
        command.Parameters.AddWithValue("$customerId", followUp.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(followUp.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(followUp.OrderId));
        command.Parameters.AddWithValue("$title", followUp.Title);
        command.Parameters.AddWithValue("$content", followUp.Content);
        command.Parameters.AddWithValue("$status", (int)followUp.Status);
        command.Parameters.AddWithValue("$scheduledAt", followUp.ScheduledAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", ToDbDate(followUp.CompletedAt));
        command.Parameters.AddWithValue("$reminderAt", ToDbDate(followUp.ReminderAt));
        command.Parameters.AddWithValue("$createdAt", followUp.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", followUp.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(followUp.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", followUp.RemoteId);
        command.Parameters.AddWithValue("$isSynced", followUp.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", followUp.Version);
    }

    private static FollowUp Map(SqliteDataReader reader)
    {
        return new FollowUp
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Title = reader.GetString(4),
            Content = reader.GetString(5),
            Status = (FollowUpStatus)reader.GetInt32(6),
            ScheduledAt = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind),
            CompletedAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            ReminderAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(13),
            IsSynced = reader.GetInt32(14) == 1,
            Version = reader.GetInt32(15)
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
