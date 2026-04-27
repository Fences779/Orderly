using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class PriceAdjustmentRepository : IPriceAdjustmentRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public PriceAdjustmentRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PriceAdjustment> CreateAsync(PriceAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (adjustment.CreatedAt == default)
        {
            adjustment.CreatedAt = now;
        }

        adjustment.UpdatedAt = now;
        adjustment.DeletedAt = null;
        adjustment.IsSynced = false;
        adjustment.Version = Math.Max(1, adjustment.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PriceAdjustments (
                CustomerId, DealId, OrderId, OriginalAmount, AdjustedAmount, Reason, Status,
                RequestedBy, ApprovedBy, ApprovedAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $originalAmount, $adjustedAmount, $reason, $status,
                $requestedBy, $approvedBy, $approvedAt, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, adjustment);
        adjustment.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return adjustment;
    }

    public async Task<PriceAdjustment?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE DeletedAt IS NULL AND Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public Task<IReadOnlyList<PriceAdjustment>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY UpdatedAt DESC", cancellationToken);
    }

    public Task<IReadOnlyList<PriceAdjustment>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND OrderId = $orderId ORDER BY UpdatedAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$orderId", orderId);
        });
    }

    public Task<IReadOnlyList<PriceAdjustment>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND CustomerId = $customerId ORDER BY UpdatedAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public Task<IReadOnlyList<PriceAdjustment>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND Status = 1 ORDER BY CreatedAt ASC", cancellationToken);
    }

    public async Task UpdateAsync(PriceAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        adjustment.UpdatedAt = DateTime.Now;
        adjustment.IsSynced = false;
        adjustment.Version = Math.Max(1, adjustment.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PriceAdjustments
            SET CustomerId = $customerId,
                DealId = $dealId,
                OrderId = $orderId,
                OriginalAmount = $originalAmount,
                AdjustedAmount = $adjustedAmount,
                Reason = $reason,
                Status = $status,
                RequestedBy = $requestedBy,
                ApprovedBy = $approvedBy,
                ApprovedAt = $approvedAt,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, adjustment);
        command.Parameters.AddWithValue("$id", adjustment.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PriceAdjustments
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

    private async Task<IReadOnlyList<PriceAdjustment>> QueryAsync(string whereClause, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {whereClause};";
        configure?.Invoke(command);

        var rows = new List<PriceAdjustment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private const string SelectSql = """
        SELECT Id, CustomerId, DealId, OrderId, OriginalAmount, AdjustedAmount, Reason, Status,
               RequestedBy, ApprovedBy, ApprovedAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM PriceAdjustments
        """;

    private static void AddParameters(SqliteCommand command, PriceAdjustment adjustment)
    {
        command.Parameters.AddWithValue("$customerId", adjustment.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(adjustment.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(adjustment.OrderId));
        command.Parameters.AddWithValue("$originalAmount", adjustment.OriginalAmount);
        command.Parameters.AddWithValue("$adjustedAmount", adjustment.AdjustedAmount);
        command.Parameters.AddWithValue("$reason", adjustment.Reason);
        command.Parameters.AddWithValue("$status", (int)adjustment.Status);
        command.Parameters.AddWithValue("$requestedBy", adjustment.RequestedBy);
        command.Parameters.AddWithValue("$approvedBy", adjustment.ApprovedBy);
        command.Parameters.AddWithValue("$approvedAt", ToDbDate(adjustment.ApprovedAt));
        command.Parameters.AddWithValue("$createdAt", adjustment.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", adjustment.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(adjustment.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", adjustment.RemoteId);
        command.Parameters.AddWithValue("$isSynced", adjustment.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", adjustment.Version);
    }

    private static PriceAdjustment Map(SqliteDataReader reader)
    {
        return new PriceAdjustment
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            OriginalAmount = reader.GetDecimal(4),
            AdjustedAmount = reader.GetDecimal(5),
            Reason = reader.GetString(6),
            Status = (PriceAdjustmentStatus)reader.GetInt32(7),
            RequestedBy = reader.GetString(8),
            ApprovedBy = reader.GetString(9),
            ApprovedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            CreatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(14),
            IsSynced = reader.GetInt32(15) == 1,
            Version = reader.GetInt32(16)
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
