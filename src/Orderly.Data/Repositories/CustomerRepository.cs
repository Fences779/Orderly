using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public CustomerRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, NameCiphertext, Status, Priority, SourcePlatform, Channel, ContactHandle, ContactHandleCiphertext, Phone, PhoneCiphertext, Remark, RemarkCiphertext, ExternalId, ExternalIdCiphertext, RawPayload, RawPayloadCiphertext,
                   LastContactAt, LastContactAtCiphertext, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM Customers
            WHERE DeletedAt IS NULL
            ORDER BY UpdatedAt DESC;
            """;

        var rows = new List<Customer>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, NameCiphertext, Status, Priority, SourcePlatform, Channel, ContactHandle, ContactHandleCiphertext, Phone, PhoneCiphertext, Remark, RemarkCiphertext, ExternalId, ExternalIdCiphertext, RawPayload, RawPayloadCiphertext,
                   LastContactAt, LastContactAtCiphertext, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM Customers
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (customer.CreatedAt == default)
        {
            customer.CreatedAt = now;
        }

        customer.UpdatedAt = now;
        customer.DeletedAt = null;
        customer.IsSynced = false;
        customer.Version = Math.Max(1, customer.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Customers (
                Name, NameCiphertext, Status, Priority, SourcePlatform, Channel, ContactHandle, ContactHandleCiphertext, Phone, PhoneCiphertext, Remark, RemarkCiphertext, ExternalId, ExternalIdCiphertext, RawPayload, RawPayloadCiphertext,
                LastContactAt, LastContactAtCiphertext, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $name, $nameCiphertext, $status, $priority, $sourcePlatform, $channel, $contactHandle, $contactHandleCiphertext, $phone, $phoneCiphertext, $remark, $remarkCiphertext, $externalId, $externalIdCiphertext, $rawPayload, $rawPayloadCiphertext,
                $lastContactAt, $lastContactAtCiphertext, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, customer, _fieldEncryptionService);
        customer.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return customer;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        customer.UpdatedAt = DateTime.Now;
        customer.IsSynced = false;
        customer.Version = Math.Max(1, customer.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Customers
            SET Name = $name,
                NameCiphertext = $nameCiphertext,
                Status = $status,
                Priority = $priority,
                SourcePlatform = $sourcePlatform,
                Channel = $channel,
                ContactHandle = $contactHandle,
                ContactHandleCiphertext = $contactHandleCiphertext,
                Phone = $phone,
                PhoneCiphertext = $phoneCiphertext,
                Remark = $remark,
                RemarkCiphertext = $remarkCiphertext,
                ExternalId = $externalId,
                ExternalIdCiphertext = $externalIdCiphertext,
                RawPayload = $rawPayload,
                RawPayloadCiphertext = $rawPayloadCiphertext,
                LastContactAt = $lastContactAt,
                LastContactAtCiphertext = $lastContactAtCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, customer, _fieldEncryptionService);
        command.Parameters.AddWithValue("$id", customer.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Customers
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

    internal static Customer Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService, int offset = 0)
    {
        var name = EncryptedColumnReader.ReadRequiredString(reader, offset + 2, fieldEncryptionService, "Customers.NameCiphertext");
        var contactHandle = EncryptedColumnReader.ReadRequiredString(reader, offset + 8, fieldEncryptionService, "Customers.ContactHandleCiphertext");
        var phone = EncryptedColumnReader.ReadRequiredString(reader, offset + 10, fieldEncryptionService, "Customers.PhoneCiphertext");
        var remark = EncryptedColumnReader.ReadRequiredString(reader, offset + 12, fieldEncryptionService, "Customers.RemarkCiphertext");
        var externalId = EncryptedColumnReader.ReadRequiredString(reader, offset + 14, fieldEncryptionService, "Customers.ExternalIdCiphertext");
        var rawPayload = EncryptedColumnReader.ReadRequiredString(reader, offset + 16, fieldEncryptionService, "Customers.RawPayloadCiphertext");
        var lastContactAt = EncryptedColumnReader.ReadOptionalDateTime(reader, offset + 18, fieldEncryptionService, "Customers.LastContactAtCiphertext");

        return new Customer
        {
            Id = reader.GetInt32(offset),
            Name = name,
            Status = (CustomerStatus)reader.GetInt32(offset + 3),
            Priority = (CustomerPriority)reader.GetInt32(offset + 4),
            SourcePlatform = reader.GetString(offset + 5),
            Channel = reader.GetString(offset + 6),
            ContactHandle = contactHandle,
            Phone = phone,
            Remark = remark,
            ExternalId = externalId,
            RawPayload = rawPayload,
            LastContactAt = lastContactAt,
            CreatedAt = DateTime.Parse(reader.GetString(offset + 19), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(offset + 20), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(offset + 21) ? null : DateTime.Parse(reader.GetString(offset + 21), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(offset + 22),
            IsSynced = reader.GetInt32(offset + 23) == 1,
            Version = reader.GetInt32(offset + 24)
        };
    }

    private static void AddParameters(SqliteCommand command, Customer customer, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$name", string.Empty);
        command.Parameters.AddWithValue("$nameCiphertext", fieldEncryptionService.Encrypt(customer.Name));
        command.Parameters.AddWithValue("$status", (int)customer.Status);
        command.Parameters.AddWithValue("$priority", (int)customer.Priority);
        command.Parameters.AddWithValue("$sourcePlatform", customer.SourcePlatform);
        command.Parameters.AddWithValue("$channel", customer.Channel);
        command.Parameters.AddWithValue("$contactHandle", string.Empty);
        command.Parameters.AddWithValue("$contactHandleCiphertext", fieldEncryptionService.Encrypt(customer.ContactHandle));
        command.Parameters.AddWithValue("$phone", string.Empty);
        command.Parameters.AddWithValue("$phoneCiphertext", fieldEncryptionService.Encrypt(customer.Phone));
        command.Parameters.AddWithValue("$remark", string.Empty);
        command.Parameters.AddWithValue("$remarkCiphertext", fieldEncryptionService.Encrypt(customer.Remark));
        command.Parameters.AddWithValue("$externalId", string.Empty);
        command.Parameters.AddWithValue("$externalIdCiphertext", fieldEncryptionService.Encrypt(customer.ExternalId));
        command.Parameters.AddWithValue("$rawPayload", string.Empty);
        command.Parameters.AddWithValue("$rawPayloadCiphertext", fieldEncryptionService.Encrypt(customer.RawPayload));
        command.Parameters.AddWithValue("$lastContactAt", DBNull.Value);
        command.Parameters.AddWithValue("$lastContactAtCiphertext", fieldEncryptionService.Encrypt(ToCipherDate(customer.LastContactAt)));
        command.Parameters.AddWithValue("$createdAt", customer.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", customer.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(customer.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", customer.RemoteId);
        command.Parameters.AddWithValue("$isSynced", customer.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", customer.Version);
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
