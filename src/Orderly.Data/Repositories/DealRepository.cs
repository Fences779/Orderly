using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class DealRepository : IDealRepository
{
    private const int MaxTitleCharacters = 120;
    private const int MaxRequirementCharacters = 2000;
    private const int MaxShortFieldCharacters = 80;
    private const int MaxLostReasonCharacters = 1000;
    private const int MaxRemoteIdCharacters = 160;
    private const decimal MaxDealAmount = 100_000_000m;

    private static readonly DateTime MinDealDate = new(2000, 1, 1);
    private static readonly DateTime MaxDealDate = new(2100, 1, 1);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public DealRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<Deal> CreateAsync(Deal deal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deal);

        NormalizeDeal(deal);
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
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Deals (
                CustomerId, Title, TitleCiphertext, Stage, EstimatedAmount, EstimatedAmountCiphertext, Requirement, RequirementCiphertext, SourcePlatform, Channel,
                ExpectedCloseAt, ExpectedCloseAtCiphertext, ClosedAt, ClosedAtCiphertext, LostReason, LostReasonCiphertext, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $title, $titleCiphertext, $stage, $estimatedAmount, $estimatedAmountCiphertext, $requirement, $requirementCiphertext, $sourcePlatform, $channel,
                $expectedCloseAt, $expectedCloseAtCiphertext, $closedAt, $closedAtCiphertext, $lostReason, $lostReasonCiphertext, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, deal, _fieldEncryptionService);
        deal.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await UpdateEncryptedColumnsAsync(connection, transaction, deal, _fieldEncryptionService, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
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
        ArgumentNullException.ThrowIfNull(deal);

        NormalizeDeal(deal);
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
                TitleCiphertext = $titleCiphertext,
                Stage = $stage,
                EstimatedAmount = $estimatedAmount,
                EstimatedAmountCiphertext = $estimatedAmountCiphertext,
                Requirement = $requirement,
                RequirementCiphertext = $requirementCiphertext,
                SourcePlatform = $sourcePlatform,
                Channel = $channel,
                ExpectedCloseAt = $expectedCloseAt,
                ExpectedCloseAtCiphertext = $expectedCloseAtCiphertext,
                ClosedAt = $closedAt,
                ClosedAtCiphertext = $closedAtCiphertext,
                LostReason = $lostReason,
                LostReasonCiphertext = $lostReasonCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, deal, _fieldEncryptionService);
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
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void NormalizeDeal(Deal deal)
    {
        if (deal.CustomerId <= 0)
        {
            throw new InvalidOperationException("成交机会缺少有效客户。");
        }

        if (!Enum.IsDefined(deal.Stage))
        {
            throw new InvalidOperationException("成交阶段无效。");
        }

        if (deal.EstimatedAmount < 0 || deal.EstimatedAmount > MaxDealAmount)
        {
            throw new InvalidOperationException("成交金额超出允许范围。");
        }

        EnsureOptionalDateInRange(deal.ExpectedCloseAt, "预计成交时间");
        EnsureOptionalDateInRange(deal.ClosedAt, "成交关闭时间");

        deal.Title = NormalizeRequiredText(deal.Title, MaxTitleCharacters, "成交标题", allowLineBreaks: false);
        deal.Requirement = NormalizeOptionalText(deal.Requirement, MaxRequirementCharacters, "成交需求", allowLineBreaks: true);
        deal.SourcePlatform = NormalizeOptionalText(deal.SourcePlatform, MaxShortFieldCharacters, "成交来源平台", allowLineBreaks: false);
        deal.Channel = NormalizeOptionalText(deal.Channel, MaxShortFieldCharacters, "成交渠道", allowLineBreaks: false);
        deal.LostReason = NormalizeOptionalText(deal.LostReason, MaxLostReasonCharacters, "丢单原因", allowLineBreaks: true);
        deal.RemoteId = NormalizeOptionalText(deal.RemoteId, MaxRemoteIdCharacters, "成交远端标识", allowLineBreaks: false);
    }

    private static void EnsureOptionalDateInRange(DateTime? value, string fieldName)
    {
        if (value is not DateTime dateTime)
        {
            return;
        }

        if (dateTime < MinDealDate || dateTime > MaxDealDate)
        {
            throw new InvalidOperationException($"{fieldName}超出允许范围。");
        }
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

    private const string SelectSql = """
        SELECT Id, CustomerId, Title, TitleCiphertext, Stage, EstimatedAmount, EstimatedAmountCiphertext, Requirement, RequirementCiphertext, SourcePlatform, Channel,
               ExpectedCloseAt, ExpectedCloseAtCiphertext, ClosedAt, ClosedAtCiphertext, LostReason, LostReasonCiphertext, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM Deals
        """;

    private static void AddParameters(SqliteCommand command, Deal deal, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", deal.CustomerId);
        command.Parameters.AddWithValue("$title", string.Empty);
        command.Parameters.AddWithValue("$titleCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, deal.Title, "Deals.TitleCiphertext", deal.Id));
        command.Parameters.AddWithValue("$stage", (int)deal.Stage);
        command.Parameters.AddWithValue("$estimatedAmount", 0);
        command.Parameters.AddWithValue("$estimatedAmountCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, deal.EstimatedAmount.ToString(CultureInfo.InvariantCulture), "Deals.EstimatedAmountCiphertext", deal.Id));
        command.Parameters.AddWithValue("$requirement", string.Empty);
        command.Parameters.AddWithValue("$requirementCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, deal.Requirement, "Deals.RequirementCiphertext", deal.Id));
        command.Parameters.AddWithValue("$sourcePlatform", deal.SourcePlatform);
        command.Parameters.AddWithValue("$channel", deal.Channel);
        command.Parameters.AddWithValue("$expectedCloseAt", DBNull.Value);
        command.Parameters.AddWithValue("$expectedCloseAtCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, ToCipherDate(deal.ExpectedCloseAt), "Deals.ExpectedCloseAtCiphertext", deal.Id));
        command.Parameters.AddWithValue("$closedAt", DBNull.Value);
        command.Parameters.AddWithValue("$closedAtCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, ToCipherDate(deal.ClosedAt), "Deals.ClosedAtCiphertext", deal.Id));
        command.Parameters.AddWithValue("$lostReason", string.Empty);
        command.Parameters.AddWithValue("$lostReasonCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, deal.LostReason, "Deals.LostReasonCiphertext", deal.Id));
        command.Parameters.AddWithValue("$createdAt", deal.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", deal.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(deal.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", deal.RemoteId);
        command.Parameters.AddWithValue("$isSynced", deal.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", deal.Version);
    }

    private static async Task UpdateEncryptedColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Deal deal,
        IFieldEncryptionService fieldEncryptionService,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Deals
            SET TitleCiphertext = $titleCiphertext,
                EstimatedAmountCiphertext = $estimatedAmountCiphertext,
                RequirementCiphertext = $requirementCiphertext,
                ExpectedCloseAtCiphertext = $expectedCloseAtCiphertext,
                ClosedAtCiphertext = $closedAtCiphertext,
                LostReasonCiphertext = $lostReasonCiphertext
            WHERE Id = $id;
            """;
        AddParameters(command, deal, fieldEncryptionService);
        command.Parameters.AddWithValue("$id", deal.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Deal Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var title = EncryptedColumnReader.ReadRequiredString(reader, 3, fieldEncryptionService, "Deals.TitleCiphertext");
        var requirement = EncryptedColumnReader.ReadRequiredString(reader, 8, fieldEncryptionService, "Deals.RequirementCiphertext");
        var lostReason = EncryptedColumnReader.ReadRequiredString(reader, 16, fieldEncryptionService, "Deals.LostReasonCiphertext");
        var estimatedAmount = EncryptedColumnReader.ReadRequiredDecimal(reader, 6, fieldEncryptionService, "Deals.EstimatedAmountCiphertext");
        var expectedCloseAt = EncryptedColumnReader.ReadOptionalDateTime(reader, 12, fieldEncryptionService, "Deals.ExpectedCloseAtCiphertext");
        var closedAt = EncryptedColumnReader.ReadOptionalDateTime(reader, 14, fieldEncryptionService, "Deals.ClosedAtCiphertext");

        return new Deal
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            Title = title,
            Stage = (DealStage)reader.GetInt32(4),
            EstimatedAmount = estimatedAmount,
            Requirement = requirement,
            SourcePlatform = reader.GetString(9),
            Channel = reader.GetString(10),
            ExpectedCloseAt = expectedCloseAt,
            ClosedAt = closedAt,
            LostReason = lostReason,
            CreatedAt = DateTime.Parse(reader.GetString(17), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(18), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(19) ? null : DateTime.Parse(reader.GetString(19), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(20),
            IsSynced = reader.GetInt32(21) == 1,
            Version = reader.GetInt32(22)
        };
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
