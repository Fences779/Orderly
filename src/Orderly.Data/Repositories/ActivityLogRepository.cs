using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;
using Orderly.Data.Services;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class ActivityLogRepository : IActivityLogRepository
{
    private const int MaxRecentActivityCount = 500;
    private const int MaxTitleCharacters = 160;
    private const int MaxDescriptionCharacters = 2000;
    private const int MaxOperatorCharacters = 80;
    private const int MaxMetadataJsonCharacters = 8192;
    private const int MaxRemoteIdCharacters = 160;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public ActivityLogRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<ActivityLog> CreateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityLog);

        NormalizeActivityLog(activityLog);
        var now = DateTime.Now;
        if (activityLog.CreatedAt == default)
        {
            activityLog.CreatedAt = now;
        }

        activityLog.UpdatedAt = now;
        activityLog.DeletedAt = null;
        activityLog.IsSynced = false;
        activityLog.Version = Math.Max(1, activityLog.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        activityLog.MetadataJson = await EnsureQaMetadataAsync(connection, activityLog, cancellationToken);
        activityLog.MetadataJson = NormalizeOptionalText(activityLog.MetadataJson, MaxMetadataJsonCharacters, "活动日志元数据");
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ActivityLogs (
                Type, CustomerId, DealId, OrderId, Title, TitleCiphertext, Description, DescriptionCiphertext, Operator, OperatorCiphertext, MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $type, $customerId, $dealId, $orderId, $title, $titleCiphertext, $description, $descriptionCiphertext, $operator, $operatorCiphertext, $metadataJson, $metadataJsonCiphertext,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, activityLog, _fieldEncryptionService);
        activityLog.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await UpdateEncryptedColumnsAsync(connection, transaction, activityLog, _fieldEncryptionService, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return activityLog;
    }

    public async Task<ActivityLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE DeletedAt IS NULL AND Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public Task<IReadOnlyList<ActivityLog>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY CreatedAt DESC", cancellationToken);
    }

    public Task<IReadOnlyList<ActivityLog>> ListRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL ORDER BY CreatedAt DESC LIMIT $count", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$count", Math.Clamp(count, 1, MaxRecentActivityCount));
        });
    }

    public Task<IReadOnlyList<ActivityLog>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync("WHERE DeletedAt IS NULL AND CustomerId = $customerId ORDER BY CreatedAt DESC", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public async Task<int> SoftDeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var cutoffText = cutoff.ToString("O");
        var now = DateTime.Now.ToString("O");

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ActivityLogs
            SET DeletedAt = $deletedAt,
                UpdatedAt = $updatedAt,
                IsSynced = 0,
                Version = Version + 1
            WHERE DeletedAt IS NULL
              AND CreatedAt < $cutoff;
            """;
        command.Parameters.AddWithValue("$deletedAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$cutoff", cutoffText);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityLog);

        NormalizeActivityLog(activityLog);
        activityLog.UpdatedAt = DateTime.Now;
        activityLog.IsSynced = false;
        activityLog.Version = Math.Max(1, activityLog.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        activityLog.MetadataJson = await EnsureQaMetadataAsync(connection, activityLog, cancellationToken);
        activityLog.MetadataJson = NormalizeOptionalText(activityLog.MetadataJson, MaxMetadataJsonCharacters, "活动日志元数据");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ActivityLogs
            SET Type = $type,
                CustomerId = $customerId,
                DealId = $dealId,
                OrderId = $orderId,
                Title = $title,
                TitleCiphertext = $titleCiphertext,
                Description = $description,
                DescriptionCiphertext = $descriptionCiphertext,
                Operator = $operator,
                OperatorCiphertext = $operatorCiphertext,
                MetadataJson = $metadataJson,
                MetadataJsonCiphertext = $metadataJsonCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, activityLog, _fieldEncryptionService);
        command.Parameters.AddWithValue("$id", activityLog.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ActivityLogs
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

    private async Task<IReadOnlyList<ActivityLog>> QueryAsync(string whereClause, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {whereClause};";
        configure?.Invoke(command);

        var rows = new List<ActivityLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static async Task<string> EnsureQaMetadataAsync(SqliteConnection connection, ActivityLog activityLog, CancellationToken cancellationToken)
    {
        if (!await IsQaScopedAsync(connection, activityLog, cancellationToken))
        {
            return activityLog.MetadataJson;
        }

        return QaDataScope.EnsureActivityMetadataTagged(activityLog.MetadataJson, "runtime");
    }

    private static void NormalizeActivityLog(ActivityLog activityLog)
    {
        if (!Enum.IsDefined(activityLog.Type))
        {
            throw new InvalidOperationException("活动日志类型无效。");
        }

        if (activityLog.CustomerId is <= 0)
        {
            throw new InvalidOperationException("活动日志客户标识无效。");
        }

        if (activityLog.DealId is <= 0)
        {
            throw new InvalidOperationException("活动日志成交标识无效。");
        }

        if (activityLog.OrderId is <= 0)
        {
            throw new InvalidOperationException("活动日志订单标识无效。");
        }

        activityLog.Title = NormalizeRequiredText(activityLog.Title, MaxTitleCharacters, "活动日志标题");
        activityLog.Description = NormalizeOptionalText(activityLog.Description, MaxDescriptionCharacters, "活动日志描述");
        activityLog.Operator = NormalizeOptionalText(activityLog.Operator, MaxOperatorCharacters, "活动日志操作人");
        activityLog.MetadataJson = NormalizeOptionalText(activityLog.MetadataJson, MaxMetadataJsonCharacters, "活动日志元数据");
        activityLog.RemoteId = NormalizeOptionalText(activityLog.RemoteId, MaxRemoteIdCharacters, "活动日志远端标识");
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static async Task<bool> IsQaScopedAsync(SqliteConnection connection, ActivityLog activityLog, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            SELECT CASE
                WHEN $orderId IS NOT NULL AND EXISTS (
                    SELECT 1
                    FROM Orders
                    WHERE Id = $orderId
                      AND {{QaDataScope.BuildOrderAssociationPredicate("Orders")}}
                ) THEN 1
                WHEN $dealId IS NOT NULL AND EXISTS (
                    SELECT 1
                    FROM Deals
                    WHERE Id = $dealId
                      AND {{QaDataScope.BuildDealAssociationPredicate("Deals")}}
                ) THEN 1
                WHEN $customerId IS NOT NULL AND EXISTS (
                    SELECT 1
                    FROM Customers
                    WHERE Id = $customerId
                      AND {{QaDataScope.BuildCustomerAssociationPredicate("Customers")}}
                ) THEN 1
                ELSE 0
            END;
            """;
        command.Parameters.AddWithValue("$customerId", ToDbInt(activityLog.CustomerId));
        command.Parameters.AddWithValue("$dealId", ToDbInt(activityLog.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(activityLog.OrderId));
        QaDataScope.AddScopeParameters(command);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    private const string SelectSql = """
        SELECT Id, Type, CustomerId, DealId, OrderId, Title, TitleCiphertext, Description, DescriptionCiphertext, Operator, OperatorCiphertext, MetadataJson, MetadataJsonCiphertext,
               CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
        FROM ActivityLogs
        """;

    private static void AddParameters(SqliteCommand command, ActivityLog activityLog, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$type", (int)activityLog.Type);
        command.Parameters.AddWithValue("$customerId", ToDbInt(activityLog.CustomerId));
        command.Parameters.AddWithValue("$dealId", ToDbInt(activityLog.DealId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(activityLog.OrderId));
        command.Parameters.AddWithValue("$title", string.Empty);
        command.Parameters.AddWithValue("$titleCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, activityLog.Title, "ActivityLogs.TitleCiphertext", activityLog.Id));
        command.Parameters.AddWithValue("$description", string.Empty);
        command.Parameters.AddWithValue("$descriptionCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, activityLog.Description, "ActivityLogs.DescriptionCiphertext", activityLog.Id));
        command.Parameters.AddWithValue("$operator", string.Empty);
        command.Parameters.AddWithValue("$operatorCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, activityLog.Operator, "ActivityLogs.OperatorCiphertext", activityLog.Id));
        command.Parameters.AddWithValue("$metadataJson", string.Empty);
        command.Parameters.AddWithValue("$metadataJsonCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, activityLog.MetadataJson, "ActivityLogs.MetadataJsonCiphertext", activityLog.Id));
        command.Parameters.AddWithValue("$createdAt", activityLog.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", activityLog.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(activityLog.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", activityLog.RemoteId);
        command.Parameters.AddWithValue("$isSynced", activityLog.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", activityLog.Version);
    }

    private static async Task UpdateEncryptedColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ActivityLog activityLog,
        IFieldEncryptionService fieldEncryptionService,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ActivityLogs
            SET TitleCiphertext = $titleCiphertext,
                DescriptionCiphertext = $descriptionCiphertext,
                OperatorCiphertext = $operatorCiphertext,
                MetadataJsonCiphertext = $metadataJsonCiphertext
            WHERE Id = $id;
            """;
        AddParameters(command, activityLog, fieldEncryptionService);
        command.Parameters.AddWithValue("$id", activityLog.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ActivityLog Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var title = EncryptedColumnReader.ReadRequiredString(reader, 6, fieldEncryptionService, "ActivityLogs.TitleCiphertext");
        var description = EncryptedColumnReader.ReadRequiredString(reader, 8, fieldEncryptionService, "ActivityLogs.DescriptionCiphertext");
        var @operator = EncryptedColumnReader.ReadRequiredString(reader, 10, fieldEncryptionService, "ActivityLogs.OperatorCiphertext");
        var metadataJson = EncryptedColumnReader.ReadRequiredString(reader, 12, fieldEncryptionService, "ActivityLogs.MetadataJsonCiphertext");

        return new ActivityLog
        {
            Id = reader.GetInt32(0),
            Type = (ActivityType)reader.GetInt32(1),
            CustomerId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DealId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            OrderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Title = title,
            Description = description,
            Operator = @operator,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.Parse(reader.GetString(13), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(14), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(16),
            IsSynced = reader.GetInt32(17) == 1,
            Version = reader.GetInt32(18)
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
