using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class FollowUpRepository : IFollowUpRepository
{
    private const int MaxTitleCharacters = 120;
    private const int MaxContentCharacters = 2000;
    private const int MaxRemoteIdCharacters = 160;

    private static readonly DateTime MinFollowUpDate = new(2000, 1, 1);
    private static readonly DateTime MaxFollowUpDate = new(2100, 1, 1);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public FollowUpRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<FollowUp> CreateAsync(FollowUp followUp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(followUp);

        NormalizeFollowUp(followUp);
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
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO FollowUps (
                CustomerId, DealId, OrderId, Title, TitleCiphertext, Content, ContentCiphertext, Status, ScheduledAt, ScheduledAtCiphertext, CompletedAt, CompletedAtCiphertext, ReminderAt, ReminderAtCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $title, $titleCiphertext, $content, $contentCiphertext, $status, $scheduledAt, $scheduledAtCiphertext, $completedAt, $completedAtCiphertext, $reminderAt, $reminderAtCiphertext,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, followUp, _fieldEncryptionService);
        followUp.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await UpdateEncryptedColumnsAsync(connection, transaction, followUp, _fieldEncryptionService, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
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
        ArgumentNullException.ThrowIfNull(followUp);

        NormalizeFollowUp(followUp);
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
                TitleCiphertext = $titleCiphertext,
                Content = $content,
                ContentCiphertext = $contentCiphertext,
                Status = $status,
                ScheduledAt = $scheduledAt,
                ScheduledAtCiphertext = $scheduledAtCiphertext,
                CompletedAt = $completedAt,
                CompletedAtCiphertext = $completedAtCiphertext,
                ReminderAt = $reminderAt,
                ReminderAtCiphertext = $reminderAtCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, followUp, _fieldEncryptionService);
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
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void NormalizeFollowUp(FollowUp followUp)
    {
        if (followUp.CustomerId <= 0)
        {
            throw new InvalidOperationException("跟进缺少有效客户。");
        }

        if (followUp.DealId is <= 0)
        {
            throw new InvalidOperationException("跟进成交标识无效。");
        }

        if (followUp.OrderId is <= 0)
        {
            throw new InvalidOperationException("跟进订单标识无效。");
        }

        if (!Enum.IsDefined(followUp.Status))
        {
            throw new InvalidOperationException("跟进状态无效。");
        }

        EnsureDateInRange(followUp.ScheduledAt, "跟进计划时间");
        EnsureOptionalDateInRange(followUp.CompletedAt, "跟进完成时间");
        EnsureOptionalDateInRange(followUp.ReminderAt, "跟进提醒时间");

        followUp.Title = NormalizeRequiredText(followUp.Title, MaxTitleCharacters, "跟进标题", allowLineBreaks: false);
        followUp.Content = NormalizeOptionalText(followUp.Content, MaxContentCharacters, "跟进内容", allowLineBreaks: true);
        followUp.RemoteId = NormalizeOptionalText(followUp.RemoteId, MaxRemoteIdCharacters, "跟进远端标识", allowLineBreaks: false);
    }

    private static void EnsureOptionalDateInRange(DateTime? value, string fieldName)
    {
        if (value is DateTime dateTime)
        {
            EnsureDateInRange(dateTime, fieldName);
        }
    }

    private static void EnsureDateInRange(DateTime value, string fieldName)
    {
        if (value < MinFollowUpDate || value > MaxFollowUpDate)
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
        SELECT Id, CustomerId, DealId, OrderId, Title, TitleCiphertext, Content, ContentCiphertext, Status, ScheduledAt, ScheduledAtCiphertext, CompletedAt, CompletedAtCiphertext, ReminderAt, ReminderAtCiphertext,
               CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM FollowUps
        """;

    private static void AddParameters(SqliteCommand command, FollowUp followUp, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", followUp.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(followUp.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(followUp.OrderId));
        command.Parameters.AddWithValue("$title", string.Empty);
        command.Parameters.AddWithValue("$titleCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, followUp.Title, "FollowUps.TitleCiphertext", followUp.Id));
        command.Parameters.AddWithValue("$content", string.Empty);
        command.Parameters.AddWithValue("$contentCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, followUp.Content, "FollowUps.ContentCiphertext", followUp.Id));
        command.Parameters.AddWithValue("$status", (int)followUp.Status);
        command.Parameters.AddWithValue("$scheduledAt", string.Empty);
        command.Parameters.AddWithValue("$scheduledAtCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, followUp.ScheduledAt.ToString("O"), "FollowUps.ScheduledAtCiphertext", followUp.Id));
        command.Parameters.AddWithValue("$completedAt", DBNull.Value);
        command.Parameters.AddWithValue("$completedAtCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, ToCipherDate(followUp.CompletedAt), "FollowUps.CompletedAtCiphertext", followUp.Id));
        command.Parameters.AddWithValue("$reminderAt", DBNull.Value);
        command.Parameters.AddWithValue("$reminderAtCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, ToCipherDate(followUp.ReminderAt), "FollowUps.ReminderAtCiphertext", followUp.Id));
        command.Parameters.AddWithValue("$createdAt", followUp.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", followUp.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(followUp.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", followUp.RemoteId);
        command.Parameters.AddWithValue("$isSynced", followUp.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", followUp.Version);
    }

    private static async Task UpdateEncryptedColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FollowUp followUp,
        IFieldEncryptionService fieldEncryptionService,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE FollowUps
            SET TitleCiphertext = $titleCiphertext,
                ContentCiphertext = $contentCiphertext,
                ScheduledAtCiphertext = $scheduledAtCiphertext,
                CompletedAtCiphertext = $completedAtCiphertext,
                ReminderAtCiphertext = $reminderAtCiphertext
            WHERE Id = $id;
            """;
        AddParameters(command, followUp, fieldEncryptionService);
        command.Parameters.AddWithValue("$id", followUp.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static FollowUp Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var title = EncryptedColumnReader.ReadRequiredString(reader, 5, fieldEncryptionService, "FollowUps.TitleCiphertext");
        var content = EncryptedColumnReader.ReadRequiredString(reader, 7, fieldEncryptionService, "FollowUps.ContentCiphertext");
        var scheduledAt = EncryptedColumnReader.ReadRequiredDateTime(reader, 10, fieldEncryptionService, "FollowUps.ScheduledAtCiphertext");
        var completedAt = EncryptedColumnReader.ReadOptionalDateTime(reader, 12, fieldEncryptionService, "FollowUps.CompletedAtCiphertext");
        var reminderAt = EncryptedColumnReader.ReadOptionalDateTime(reader, 14, fieldEncryptionService, "FollowUps.ReminderAtCiphertext");

        return new FollowUp
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Title = title,
            Content = content,
            Status = (FollowUpStatus)reader.GetInt32(8),
            ScheduledAt = scheduledAt,
            CompletedAt = completedAt,
            ReminderAt = reminderAt,
            CreatedAt = DateTime.Parse(reader.GetString(15), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(16), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(17) ? null : DateTime.Parse(reader.GetString(17), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(18),
            IsSynced = reader.GetInt32(19) == 1,
            Version = reader.GetInt32(20)
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
