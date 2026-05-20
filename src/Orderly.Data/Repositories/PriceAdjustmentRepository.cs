using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class PriceAdjustmentRepository : IPriceAdjustmentRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public PriceAdjustmentRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
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
                CustomerId, DealId, OrderId,
                OriginalAmount, OriginalAmountCiphertext,
                AdjustedAmount, AdjustedAmountCiphertext,
                Reason, ReasonCiphertext,
                Status,
                RequestedBy, RequestedByCiphertext,
                ApprovedBy, ApprovedByCiphertext,
                ApprovedAt, ApprovedAtCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId,
                $originalAmount, $originalAmountCiphertext,
                $adjustedAmount, $adjustedAmountCiphertext,
                $reason, $reasonCiphertext,
                $status,
                $requestedBy, $requestedByCiphertext,
                $approvedBy, $approvedByCiphertext,
                $approvedAt, $approvedAtCiphertext,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, adjustment, _fieldEncryptionService);
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
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
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
                OriginalAmountCiphertext = $originalAmountCiphertext,
                AdjustedAmount = $adjustedAmount,
                AdjustedAmountCiphertext = $adjustedAmountCiphertext,
                Reason = $reason,
                ReasonCiphertext = $reasonCiphertext,
                Status = $status,
                RequestedBy = $requestedBy,
                RequestedByCiphertext = $requestedByCiphertext,
                ApprovedBy = $approvedBy,
                ApprovedByCiphertext = $approvedByCiphertext,
                ApprovedAt = $approvedAt,
                ApprovedAtCiphertext = $approvedAtCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, adjustment, _fieldEncryptionService);
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
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private const string SelectSql = """
        SELECT Id, CustomerId, DealId, OrderId,
               OriginalAmount, OriginalAmountCiphertext,
               AdjustedAmount, AdjustedAmountCiphertext,
               Reason, ReasonCiphertext,
               Status,
               RequestedBy, RequestedByCiphertext,
               ApprovedBy, ApprovedByCiphertext,
               ApprovedAt, ApprovedAtCiphertext,
               CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM PriceAdjustments
        """;

    private static void AddParameters(SqliteCommand command, PriceAdjustment adjustment, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", adjustment.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(adjustment.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(adjustment.OrderId));
        command.Parameters.AddWithValue("$originalAmount", 0);
        command.Parameters.AddWithValue("$originalAmountCiphertext", fieldEncryptionService.Encrypt(adjustment.OriginalAmount.ToString(CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$adjustedAmount", 0);
        command.Parameters.AddWithValue("$adjustedAmountCiphertext", fieldEncryptionService.Encrypt(adjustment.AdjustedAmount.ToString(CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$reason", string.Empty);
        command.Parameters.AddWithValue("$reasonCiphertext", fieldEncryptionService.Encrypt(adjustment.Reason));
        command.Parameters.AddWithValue("$status", (int)adjustment.Status);
        command.Parameters.AddWithValue("$requestedBy", string.Empty);
        command.Parameters.AddWithValue("$requestedByCiphertext", fieldEncryptionService.Encrypt(adjustment.RequestedBy));
        command.Parameters.AddWithValue("$approvedBy", string.Empty);
        command.Parameters.AddWithValue("$approvedByCiphertext", fieldEncryptionService.Encrypt(adjustment.ApprovedBy));
        command.Parameters.AddWithValue("$approvedAt", DBNull.Value);
        command.Parameters.AddWithValue("$approvedAtCiphertext", fieldEncryptionService.Encrypt(ToCipherDate(adjustment.ApprovedAt)));
        command.Parameters.AddWithValue("$createdAt", adjustment.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", adjustment.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(adjustment.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", adjustment.RemoteId);
        command.Parameters.AddWithValue("$isSynced", adjustment.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", adjustment.Version);
    }

    private static PriceAdjustment Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var originalAmount = EncryptedColumnReader.ReadRequiredDecimal(reader, 5, fieldEncryptionService, "PriceAdjustments.OriginalAmountCiphertext");
        var adjustedAmount = EncryptedColumnReader.ReadRequiredDecimal(reader, 7, fieldEncryptionService, "PriceAdjustments.AdjustedAmountCiphertext");
        var reason = EncryptedColumnReader.ReadRequiredString(reader, 9, fieldEncryptionService, "PriceAdjustments.ReasonCiphertext");
        var requestedBy = EncryptedColumnReader.ReadRequiredString(reader, 12, fieldEncryptionService, "PriceAdjustments.RequestedByCiphertext");
        var approvedBy = EncryptedColumnReader.ReadRequiredString(reader, 14, fieldEncryptionService, "PriceAdjustments.ApprovedByCiphertext");
        var approvedAt = EncryptedColumnReader.ReadOptionalDateTime(reader, 16, fieldEncryptionService, "PriceAdjustments.ApprovedAtCiphertext");

        return new PriceAdjustment
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            OriginalAmount = originalAmount,
            AdjustedAmount = adjustedAmount,
            Reason = reason,
            Status = (PriceAdjustmentStatus)reader.GetInt32(10),
            RequestedBy = requestedBy,
            ApprovedBy = approvedBy,
            ApprovedAt = approvedAt,
            CreatedAt = DateTime.Parse(reader.GetString(17), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(18), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(19) ? null : DateTime.Parse(reader.GetString(19), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(20),
            IsSynced = reader.GetInt32(21) == 1,
            Version = reader.GetInt32(22)
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

    private static string ToCipherDate(DateTime? value)
    {
        return value is null ? string.Empty : value.Value.ToString("O");
    }
}
