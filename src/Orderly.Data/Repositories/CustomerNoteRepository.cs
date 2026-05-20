using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class CustomerNoteRepository : ICustomerNoteRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public CustomerNoteRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<CustomerNote> CreateAsync(CustomerNote note, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (note.CreatedAt == default)
        {
            note.CreatedAt = now;
        }

        note.UpdatedAt = now;
        note.DeletedAt = null;
        note.IsSynced = false;
        note.Version = Math.Max(1, note.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CustomerNotes (
                CustomerId, DealId, OrderId, Type, Content, ContentCiphertext, IsPinned, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $type, $content, $contentCiphertext, $isPinned, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, note, _fieldEncryptionService);
        note.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return note;
    }

    public async Task<CustomerNote?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE DeletedAt IS NULL AND Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public Task<IReadOnlyList<CustomerNote>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY IsPinned DESC, UpdatedAt DESC", cancellationToken);
    }

    public Task<IReadOnlyList<CustomerNote>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND CustomerId = $customerId ORDER BY IsPinned DESC, UpdatedAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public async Task UpdateAsync(CustomerNote note, CancellationToken cancellationToken = default)
    {
        note.UpdatedAt = DateTime.Now;
        note.IsSynced = false;
        note.Version = Math.Max(1, note.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CustomerNotes
            SET CustomerId = $customerId,
                DealId = $dealId,
                OrderId = $orderId,
                Type = $type,
                Content = $content,
                ContentCiphertext = $contentCiphertext,
                IsPinned = $isPinned,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, note, _fieldEncryptionService);
        command.Parameters.AddWithValue("$id", note.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CustomerNotes
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

    private async Task<IReadOnlyList<CustomerNote>> QueryAsync(string whereClause, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {whereClause};";
        configure?.Invoke(command);

        var rows = new List<CustomerNote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private const string SelectSql = """
        SELECT Id, CustomerId, DealId, OrderId, Type, Content, ContentCiphertext, IsPinned, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM CustomerNotes
        """;

    private static void AddParameters(SqliteCommand command, CustomerNote note, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", note.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(note.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(note.OrderId));
        command.Parameters.AddWithValue("$type", (int)note.Type);
        command.Parameters.AddWithValue("$content", string.Empty);
        command.Parameters.AddWithValue("$contentCiphertext", fieldEncryptionService.Encrypt(note.Content));
        command.Parameters.AddWithValue("$isPinned", note.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", note.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", note.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(note.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", note.RemoteId);
        command.Parameters.AddWithValue("$isSynced", note.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", note.Version);
    }

    private static CustomerNote Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var content = EncryptedColumnReader.ReadRequiredString(reader, 6, fieldEncryptionService, "CustomerNotes.ContentCiphertext");

        return new CustomerNote
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Type = (NoteType)reader.GetInt32(4),
            Content = content,
            IsPinned = reader.GetInt32(7) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(11),
            IsSynced = reader.GetInt32(12) == 1,
            Version = reader.GetInt32(13)
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
