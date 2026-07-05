using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Cloud;

public interface ILocalImportPackageBuilder
{
    Task<LocalImportDryRunRequest> BuildDryRunRequestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads the local SQLite database and recomputes the source fingerprint.
    /// This must be called immediately before Commit so the server can verify the data
    /// has not changed since DryRun.
    /// </summary>
    Task<string> ComputeCurrentFingerprintAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalImportPackageBuilder : ILocalImportPackageBuilder
{
    private const string SourceInstanceStateKey = "LocalImportSourceInstanceId";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly string _databasePath;

    public LocalImportPackageBuilder(SqliteConnectionFactory connectionFactory, string databasePath)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _databasePath = string.IsNullOrWhiteSpace(databasePath) ? "orderly-local-db" : databasePath;
    }

    public async Task<LocalImportDryRunRequest> BuildDryRunRequestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var sourceInstanceId = await GetOrCreateSourceInstanceIdAsync(connection, cancellationToken);
        var package = await ReadPackageAsync(connection, cancellationToken);

        return new LocalImportDryRunRequest
        {
            SourceInstanceId = sourceInstanceId,
            SourceFingerprint = ComputeFingerprint(package),
            Package = package
        };
    }

    public async Task<string> ComputeCurrentFingerprintAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var package = await ReadPackageAsync(connection, cancellationToken);
        return ComputeFingerprint(package);
    }

    private static async Task<LocalImportPackage> ReadPackageAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return new LocalImportPackage
        {
            Products = await ReadProductsAsync(connection, cancellationToken),
            Customers = await ReadCustomersAsync(connection, cancellationToken),
            InventoryItems = await ReadInventoryItemsAsync(connection, cancellationToken),
            Orders = await ReadOrdersAsync(connection, cancellationToken),
            OrderItems = await ReadOrderItemsAsync(connection, cancellationToken),
            PaymentRecords = await ReadPaymentRecordsAsync(connection, cancellationToken),
            CashFlowEntries = await ReadCashFlowEntriesAsync(connection, cancellationToken)
        };
    }

    private static async Task<List<LocalProductDto>> ReadProductsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalProductDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommerceProducts", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalProductDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                Name = ReadString(reader, "Name") ?? string.Empty,
                Code = ReadString(reader, "Code"),
                ProductType = (ProductType)ReadInt(reader, "ProductType"),
                Description = ReadString(reader, "Description"),
                DefaultUnitId = ReadGuid(reader, "DefaultUnitId"),
                SupplierId = ReadGuid(reader, "SupplierId"),
                DefaultPrice = ReadDecimal(reader, "DefaultPrice"),
                DefaultCost = ReadDecimal(reader, "DefaultCost"),
                CreatedAtUtc = ReadDateTime(reader, "CreatedAt"),
                UpdatedAtUtc = ReadDateTime(reader, "UpdatedAt")
            });
        }

        return rows;
    }

    private static async Task<List<LocalCustomerDto>> ReadCustomersAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalCustomerDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommerceCustomers", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalCustomerDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                Name = ReadString(reader, "Name") ?? string.Empty,
                Phone = ReadString(reader, "Phone"),
                WeChat = ReadString(reader, "WeChat"),
                Email = ReadString(reader, "Email"),
                LastOrderAtUtc = ReadDateTimeNullable(reader, "LastOrderAt"),
                CompletedOrderCount = ReadInt(reader, "CompletedOrderCount"),
                TotalSpend = ReadDecimal(reader, "TotalSpend"),
                CreatedAtUtc = ReadDateTime(reader, "CreatedAt"),
                UpdatedAtUtc = ReadDateTime(reader, "UpdatedAt")
            });
        }

        return rows;
    }

    private static async Task<List<LocalInventoryItemDto>> ReadInventoryItemsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalInventoryItemDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommerceInventoryItems", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalInventoryItemDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                Name = ReadString(reader, "Name") ?? string.Empty,
                Sku = ReadString(reader, "Sku"),
                SourceProductLocalEntityId = ReadGuidText(reader, "ProductId"),
                ProductId = null,
                ProductVariantId = ReadGuid(reader, "ProductVariantId"),
                UnitId = ReadGuid(reader, "UnitId"),
                QuantityAvailable = ReadDecimal(reader, "QuantityAvailable"),
                ReorderThreshold = ReadDecimal(reader, "ReorderThreshold"),
                UnitCost = ReadDecimal(reader, "UnitCost"),
                CreatedAtUtc = ReadDateTime(reader, "CreatedAt"),
                UpdatedAtUtc = ReadDateTime(reader, "UpdatedAt")
            });
        }

        return rows;
    }

    private static async Task<List<LocalOrderDto>> ReadOrdersAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalOrderDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommerceOrders", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalOrderDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                OrderNo = ReadString(reader, "OrderNo") ?? string.Empty,
                SourceCustomerLocalEntityId = ReadGuidText(reader, "CustomerId"),
                CustomerId = null,
                SalesStage = (OrderSalesStage)ReadInt(reader, "SalesStage"),
                PaymentStage = (OrderPaymentStage)ReadInt(reader, "PaymentStage"),
                FulfillmentStage = (OrderFulfillmentStage)ReadInt(reader, "FulfillmentStage"),
                Subtotal = ReadDecimal(reader, "Subtotal"),
                Total = ReadDecimal(reader, "Total"),
                Cost = ReadDecimal(reader, "Cost"),
                GrossProfit = ReadDecimal(reader, "GrossProfit"),
                GrossMargin = ReadDecimal(reader, "GrossMargin"),
                PaidAmount = ReadDecimal(reader, "PaidAmount"),
                ReceivableAmount = ReadDecimal(reader, "ReceivableAmount"),
                OrderedAtUtc = ReadDateTime(reader, "OrderedAt"),
                Note = ReadString(reader, "Note"),
                CreatedAtUtc = ReadDateTime(reader, "CreatedAt"),
                UpdatedAtUtc = ReadDateTime(reader, "UpdatedAt")
            });
        }

        return rows;
    }

    private static async Task<List<LocalOrderItemDto>> ReadOrderItemsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalOrderItemDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommerceOrderItems", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalOrderItemDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                SourceOrderLocalEntityId = ReadGuidText(reader, "OrderId") ?? string.Empty,
                SourceProductLocalEntityId = ReadGuidText(reader, "ProductId"),
                SourceInventoryItemLocalEntityId = ReadGuidText(reader, "InventoryItemId"),
                ProductId = null,
                ProductVariantId = ReadGuid(reader, "ProductVariantId"),
                InventoryItemId = null,
                UnitId = ReadGuid(reader, "UnitId"),
                Description = ReadString(reader, "Description") ?? string.Empty,
                Quantity = ReadDecimal(reader, "Quantity"),
                UnitPrice = ReadDecimal(reader, "UnitPrice"),
                UnitCost = ReadDecimal(reader, "UnitCost"),
                LineTotal = ReadDecimal(reader, "LineTotal")
            });
        }

        return rows;
    }

    private static async Task<List<LocalPaymentRecordDto>> ReadPaymentRecordsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalPaymentRecordDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommercePaymentRecords", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalPaymentRecordDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                SourceOrderLocalEntityId = ReadGuidText(reader, "OrderId"),
                SourceCashFlowEntryLocalEntityId = ReadGuidText(reader, "CashFlowEntryId"),
                OrderId = null,
                CashFlowEntryId = null,
                Amount = ReadDecimal(reader, "Amount"),
                PaidAtUtc = ReadDateTime(reader, "PaidAt"),
                Method = ReadIntOrDefault(reader, "Method"),
                BusinessKey = ReadString(reader, "BusinessKey")
            });
        }

        return rows;
    }

    private static async Task<List<LocalCashFlowEntryDto>> ReadCashFlowEntriesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LocalCashFlowEntryDto>();
        await using var reader = await ExecuteActiveQueryAsync(connection, "CommerceCashFlowEntries", cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalCashFlowEntryDto
            {
                SourceLocalEntityId = ReadSourceId(reader),
                Direction = (CashFlowDirection)ReadInt(reader, "Direction"),
                Amount = ReadDecimal(reader, "Amount"),
                SettledAmount = ReadDecimal(reader, "SettledAmount"),
                SettlementStatus = (CashFlowSettlementStatus)ReadInt(reader, "SettlementStatus"),
                OccurredAtUtc = ReadDateTime(reader, "OccurredAt"),
                DueDateUtc = ReadDateTimeNullable(reader, "DueDate"),
                CategoryName = ReadString(reader, "CategoryName") ?? string.Empty,
                SourceOrderLocalEntityId = ReadGuidText(reader, "OrderId"),
                OrderId = null,
                BusinessKey = ReadString(reader, "BusinessKey")
            });
        }

        return rows;
    }

    private static async Task<SqliteDataReader> ExecuteActiveQueryAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT *
            FROM "{tableName}"
            WHERE DeletedAt IS NULL AND Lifecycle = 0
            ORDER BY CreatedAt, Id;
            """;
        return await command.ExecuteReaderAsync(cancellationToken);
    }

    private static string ReadSourceId(SqliteDataReader reader) => reader.GetString(reader.GetOrdinal("Id"));

    private static string? ReadGuidText(SqliteDataReader reader, string column)
    {
        var value = ReadString(reader, column);
        return Guid.TryParse(value, out var guid) ? guid.ToString("N") : null;
    }

    private static Guid? ReadGuid(SqliteDataReader reader, string column)
    {
        var value = ReadString(reader, column);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static string? ReadString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static int ReadInt(SqliteDataReader reader, string column)
        => reader.GetInt32(reader.GetOrdinal(column));

    private static int ReadIntOrDefault(SqliteDataReader reader, string column)
    {
        var value = ReadString(reader, column);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static decimal ReadDecimal(SqliteDataReader reader, string column)
    {
        var value = ReadString(reader, column);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    private static DateTime ReadDateTime(SqliteDataReader reader, string column)
        => NormalizeUtc(ReadDateTimeNullable(reader, column) ?? DateTime.UtcNow);

    private static DateTime? ReadDateTimeNullable(SqliteDataReader reader, string column)
    {
        var value = ReadString(reader, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? NormalizeUtc(parsed)
            : null;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static async Task<Guid> GetOrCreateSourceInstanceIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var ensureCommand = connection.CreateCommand())
        {
            ensureCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS CloudClientState (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """;
            await ensureCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.CommandText = """
                SELECT Value
                FROM CloudClientState
                WHERE Key = $key;
                """;
            readCommand.Parameters.AddWithValue("$key", SourceInstanceStateKey);
            var existing = await readCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (Guid.TryParse(existing, out var parsed))
            {
                return parsed;
            }
        }

        var sourceInstanceId = Guid.NewGuid();
        await using (var writeCommand = connection.CreateCommand())
        {
            writeCommand.CommandText = """
                INSERT INTO CloudClientState (Key, Value, UpdatedAtUtc)
                VALUES ($key, $value, $updatedAtUtc)
                ON CONFLICT(Key) DO UPDATE SET
                    Value = excluded.Value,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            writeCommand.Parameters.AddWithValue("$key", SourceInstanceStateKey);
            writeCommand.Parameters.AddWithValue("$value", sourceInstanceId.ToString("N"));
            writeCommand.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
            await writeCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return sourceInstanceId;
    }

    private static string ComputeFingerprint(LocalImportPackage package)
    {
        var json = JsonSerializer.Serialize(package, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
