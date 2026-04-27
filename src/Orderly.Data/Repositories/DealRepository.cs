using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class DealRepository : IDealRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DealRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Deal> CreateAsync(Deal deal, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (deal.CreatedAt == default)
        {
            deal.CreatedAt = now;
        }

        deal.UpdatedAt = now;
        deal.DeletedAt = null;
        deal.IsSynced = false;
        deal.Version = Math.Max(1, deal.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Deals (
                CustomerId, Title, Stage, EstimatedAmount, Requirement, SourcePlatform, Channel,
                ExpectedCloseAt, ClosedAt, LostReason, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $title, $stage, $estimatedAmount, $requirement, $sourcePlatform, $channel,
                $expectedCloseAt, $closedAt, $lostReason, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, deal);
        deal.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return deal;
    }

    public async Task<Deal?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE DeletedAt IS NULL AND Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public Task<IReadOnlyList<Deal>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY UpdatedAt DESC", cancellationToken);
    }

    public Task<IReadOnlyList<Deal>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND CustomerId = $customerId ORDER BY UpdatedAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public async Task UpdateAsync(Deal deal, CancellationToken cancellationToken = default)
    {
        deal.UpdatedAt = DateTime.Now;
        deal.IsSynced = false;
        deal.Version = Math.Max(1, deal.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Deals
            SET CustomerId = $customerId,
                Title = $title,
                Stage = $stage,
                EstimatedAmount = $estimatedAmount,
                Requirement = $requirement,
                SourcePlatform = $sourcePlatform,
                Channel = $channel,
                ExpectedCloseAt = $expectedCloseAt,
                ClosedAt = $closedAt,
                LostReason = $lostReason,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, deal);
        command.Parameters.AddWithValue("$id", deal.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Deals
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

    private async Task<IReadOnlyList<Deal>> QueryAsync(string whereClause, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {whereClause};";
        configure?.Invoke(command);

        var rows = new List<Deal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private const string SelectSql = """
        SELECT Id, CustomerId, Title, Stage, EstimatedAmount, Requirement, SourcePlatform, Channel,
               ExpectedCloseAt, ClosedAt, LostReason, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM Deals
        """;

    private static void AddParameters(SqliteCommand command, Deal deal)
    {
        command.Parameters.AddWithValue("$customerId", deal.CustomerId);
        command.Parameters.AddWithValue("$title", deal.Title);
        command.Parameters.AddWithValue("$stage", (int)deal.Stage);
        command.Parameters.AddWithValue("$estimatedAmount", deal.EstimatedAmount);
        command.Parameters.AddWithValue("$requirement", deal.Requirement);
        command.Parameters.AddWithValue("$sourcePlatform", deal.SourcePlatform);
        command.Parameters.AddWithValue("$channel", deal.Channel);
        command.Parameters.AddWithValue("$expectedCloseAt", ToDbDate(deal.ExpectedCloseAt));
        command.Parameters.AddWithValue("$closedAt", ToDbDate(deal.ClosedAt));
        command.Parameters.AddWithValue("$lostReason", deal.LostReason);
        command.Parameters.AddWithValue("$createdAt", deal.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", deal.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(deal.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", deal.RemoteId);
        command.Parameters.AddWithValue("$isSynced", deal.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", deal.Version);
    }

    private static Deal Map(SqliteDataReader reader)
    {
        return new Deal
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            Title = reader.GetString(2),
            Stage = (DealStage)reader.GetInt32(3),
            EstimatedAmount = reader.GetDecimal(4),
            Requirement = reader.GetString(5),
            SourcePlatform = reader.GetString(6),
            Channel = reader.GetString(7),
            ExpectedCloseAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            ClosedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            LostReason = reader.GetString(10),
            CreatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(14),
            IsSynced = reader.GetInt32(15) == 1,
            Version = reader.GetInt32(16)
        };
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }
}
