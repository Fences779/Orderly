using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private const int MaxTitleCharacters = 120;
    private const int MaxRequirementCharacters = 2000;
    private const int MaxShortFieldCharacters = 80;
    private const int MaxExternalIdCharacters = 160;
    private const int MaxRawPayloadCharacters = 4096;
    private const int MaxRemoteIdCharacters = 160;
    private const decimal MaxOrderAmount = 100_000_000m;

    private static readonly DateTime MinOrderDate = new(2000, 1, 1);
    private static readonly DateTime MaxOrderDate = new(2100, 1, 1);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public OrderRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<IReadOnlyList<MerchantOrder>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        return await QueryOrdersAsync(
            """
            WHERE o.DeletedAt IS NULL AND c.DeletedAt IS NULL
            ORDER BY o.UpdatedAt DESC
            """,
            configure: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<MerchantOrder>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await QueryOrdersAsync(
            """
            WHERE o.CustomerId = $customerId AND o.DeletedAt IS NULL AND c.DeletedAt IS NULL
            ORDER BY o.UpdatedAt DESC
            """,
            configure: command => command.Parameters.AddWithValue("$customerId", customerId),
            cancellationToken);
    }

    public async Task<MerchantOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await QueryOrdersAsync(
            "WHERE o.Id = $id AND o.DeletedAt IS NULL AND c.DeletedAt IS NULL",
            configure: command => command.Parameters.AddWithValue("$id", id),
            cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<MerchantOrder> CreateAsync(MerchantOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        NormalizeOrder(order);
        var now = DateTime.Now;
        if (order.CreatedAt == default)
        {
            order.CreatedAt = now;
        }

        order.UpdatedAt = now;
        order.DeletedAt = null;
        order.IsSynced = false;
        order.Version = Math.Max(1, order.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Orders (
                CustomerId, DealId, Title, TitleCiphertext, Status, Amount, AmountCiphertext, Requirement, RequirementCiphertext, SourcePlatform, Channel,
                ExternalId, ExternalIdCiphertext, RawPayload, RawPayloadCiphertext, NextFollowUpAt, NextFollowUpAtCiphertext, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $title, $titleCiphertext, $status, $amount, $amountCiphertext, $requirement, $requirementCiphertext, $sourcePlatform, $channel,
                $externalId, $externalIdCiphertext, $rawPayload, $rawPayloadCiphertext, $nextFollowUpAt, $nextFollowUpAtCiphertext, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, order, _fieldEncryptionService);
        order.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetByIdAsync(order.Id, cancellationToken) ?? order;
    }

    public async Task UpdateAsync(MerchantOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        NormalizeOrder(order);
        order.UpdatedAt = DateTime.Now;
        order.IsSynced = false;
        order.Version = Math.Max(1, order.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders
            SET CustomerId = $customerId,
                DealId = $dealId,
                Title = $title,
                TitleCiphertext = $titleCiphertext,
                Status = $status,
                Amount = $amount,
                AmountCiphertext = $amountCiphertext,
                Requirement = $requirement,
                RequirementCiphertext = $requirementCiphertext,
                SourcePlatform = $sourcePlatform,
                Channel = $channel,
                ExternalId = $externalId,
                ExternalIdCiphertext = $externalIdCiphertext,
                RawPayload = $rawPayload,
                RawPayloadCiphertext = $rawPayloadCiphertext,
                NextFollowUpAt = $nextFollowUpAt,
                NextFollowUpAtCiphertext = $nextFollowUpAtCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, order, _fieldEncryptionService);
        command.Parameters.AddWithValue("$id", order.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders
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

    private async Task<IReadOnlyList<MerchantOrder>> QueryOrdersAsync(
        string whereClause,
        Action<SqliteCommand>? configure,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{BaseSelectSql}\n{whereClause};";
        configure?.Invoke(command);

        var rows = new List<MerchantOrder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapWithCustomer(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void NormalizeOrder(MerchantOrder order)
    {
        if (order.CustomerId <= 0)
        {
            throw new InvalidOperationException("订单缺少有效客户。");
        }

        if (order.DealId is <= 0)
        {
            throw new InvalidOperationException("订单成交标识无效。");
        }

        if (!Enum.IsDefined(order.Status))
        {
            throw new InvalidOperationException("订单状态无效。");
        }

        if (order.Amount < 0 || order.Amount > MaxOrderAmount)
        {
            throw new InvalidOperationException("订单金额超出允许范围。");
        }

        if (order.NextFollowUpAt is DateTime nextFollowUpAt
            && (nextFollowUpAt < MinOrderDate || nextFollowUpAt > MaxOrderDate))
        {
            throw new InvalidOperationException("订单下次跟进时间超出允许范围。");
        }

        order.Title = NormalizeRequiredText(order.Title, MaxTitleCharacters, "订单标题", allowLineBreaks: false);
        order.Requirement = NormalizeOptionalText(order.Requirement, MaxRequirementCharacters, "订单需求", allowLineBreaks: true);
        order.SourcePlatform = NormalizeOptionalText(order.SourcePlatform, MaxShortFieldCharacters, "订单来源平台", allowLineBreaks: false);
        order.Channel = NormalizeOptionalText(order.Channel, MaxShortFieldCharacters, "订单渠道", allowLineBreaks: false);
        order.ExternalId = NormalizeOptionalText(order.ExternalId, MaxExternalIdCharacters, "订单外部标识", allowLineBreaks: false);
        order.RawPayload = NormalizeOptionalText(order.RawPayload, MaxRawPayloadCharacters, "订单原始载荷", allowLineBreaks: true);
        order.RemoteId = NormalizeOptionalText(order.RemoteId, MaxRemoteIdCharacters, "订单远端标识", allowLineBreaks: false);
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName, allowLineBreaks);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(ch => char.IsControl(ch) && !(allowLineBreaks && ch is '\r' or '\n' or '\t')))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static MerchantOrder MapWithCustomer(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var title = EncryptedColumnReader.ReadRequiredString(reader, 4, fieldEncryptionService, "Orders.TitleCiphertext");
        var requirement = EncryptedColumnReader.ReadRequiredString(reader, 9, fieldEncryptionService, "Orders.RequirementCiphertext");
        var externalId = EncryptedColumnReader.ReadRequiredString(reader, 13, fieldEncryptionService, "Orders.ExternalIdCiphertext");
        var rawPayload = EncryptedColumnReader.ReadRequiredString(reader, 15, fieldEncryptionService, "Orders.RawPayloadCiphertext");
        var amount = EncryptedColumnReader.ReadRequiredDecimal(reader, 7, fieldEncryptionService, "Orders.AmountCiphertext");
        var nextFollowUpAt = EncryptedColumnReader.ReadOptionalDateTime(reader, 17, fieldEncryptionService, "Orders.NextFollowUpAtCiphertext");

        return new MerchantOrder
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            Title = title,
            Status = (OrderStatus)reader.GetInt32(5),
            Amount = amount,
            Requirement = requirement,
            SourcePlatform = reader.GetString(10),
            Channel = reader.GetString(11),
            ExternalId = externalId,
            RawPayload = rawPayload,
            NextFollowUpAt = nextFollowUpAt,
            CreatedAt = DateTime.Parse(reader.GetString(18), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(19), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(20) ? null : DateTime.Parse(reader.GetString(20), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(21),
            IsSynced = reader.GetInt32(22) == 1,
            Version = reader.GetInt32(23),
            Customer = CustomerRepository.Map(reader, fieldEncryptionService, 24)
        };
    }

    private static void AddParameters(SqliteCommand command, MerchantOrder order, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", order.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(order.DealId));
        command.Parameters.AddWithValue("$title", string.Empty);
        command.Parameters.AddWithValue("$titleCiphertext", fieldEncryptionService.Encrypt(order.Title, "Orders.TitleCiphertext"));
        command.Parameters.AddWithValue("$status", (int)order.Status);
        command.Parameters.AddWithValue("$amount", 0);
        command.Parameters.AddWithValue("$amountCiphertext", fieldEncryptionService.Encrypt(order.Amount.ToString(CultureInfo.InvariantCulture), "Orders.AmountCiphertext"));
        command.Parameters.AddWithValue("$requirement", string.Empty);
        command.Parameters.AddWithValue("$requirementCiphertext", fieldEncryptionService.Encrypt(order.Requirement, "Orders.RequirementCiphertext"));
        command.Parameters.AddWithValue("$sourcePlatform", order.SourcePlatform);
        command.Parameters.AddWithValue("$channel", order.Channel);
        command.Parameters.AddWithValue("$externalId", string.Empty);
        command.Parameters.AddWithValue("$externalIdCiphertext", fieldEncryptionService.Encrypt(order.ExternalId, "Orders.ExternalIdCiphertext"));
        command.Parameters.AddWithValue("$rawPayload", string.Empty);
        command.Parameters.AddWithValue("$rawPayloadCiphertext", fieldEncryptionService.Encrypt(order.RawPayload, "Orders.RawPayloadCiphertext"));
        command.Parameters.AddWithValue("$nextFollowUpAt", DBNull.Value);
        command.Parameters.AddWithValue("$nextFollowUpAtCiphertext", fieldEncryptionService.Encrypt(ToCipherDate(order.NextFollowUpAt), "Orders.NextFollowUpAtCiphertext"));
        command.Parameters.AddWithValue("$createdAt", order.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", order.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(order.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", order.RemoteId);
        command.Parameters.AddWithValue("$isSynced", order.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", order.Version);
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

    private const string BaseSelectSql = """
        SELECT
            o.Id, o.CustomerId, o.DealId, o.Title, o.TitleCiphertext, o.Status, o.Amount, o.AmountCiphertext, o.Requirement, o.RequirementCiphertext, o.SourcePlatform, o.Channel,
            o.ExternalId, o.ExternalIdCiphertext, o.RawPayload, o.RawPayloadCiphertext, o.NextFollowUpAt, o.NextFollowUpAtCiphertext, o.CreatedAt, o.UpdatedAt, o.DeletedAt, o.RemoteId, o.IsSynced, o.Version,
            c.Id, c.Name, c.NameCiphertext, c.Status, c.Priority, c.SourcePlatform, c.Channel, c.ContactHandle, c.ContactHandleCiphertext, c.Phone, c.PhoneCiphertext, c.Remark, c.RemarkCiphertext, c.ExternalId, c.ExternalIdCiphertext, c.RawPayload, c.RawPayloadCiphertext,
            c.LastContactAt, c.LastContactAtCiphertext, c.CreatedAt, c.UpdatedAt, c.DeletedAt, c.RemoteId, c.IsSynced, c.Version
        FROM Orders o
        INNER JOIN Customers c ON c.Id = o.CustomerId
        """;
}
