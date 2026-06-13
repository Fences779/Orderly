using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="CashFlowEntry"/> (table <c>CommerceCashFlowEntries</c>).</summary>
public sealed class CashFlowEntryRepository : CommerceRepositoryBase<CashFlowEntry>, ICashFlowEntryRepository
{
    private const string Table = "CommerceCashFlowEntries";

    public CashFlowEntryRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Direction", "Amount", "SettledAmount", "SettlementStatus", "OccurredAt", "DueDate",
        "CategoryName", "OrderId", "PaymentRecordId", "ImportBatchId", "SourceRowKey", "BusinessKey",
    };

    protected override void BindEntity(SqliteCommand command, CashFlowEntry entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Direction", (int)entity.Direction);
        command.Parameters.AddWithValue("$Amount", MoneyToDb(entity.Amount));
        command.Parameters.AddWithValue("$SettledAmount", MoneyToDb(entity.SettledAmount));
        command.Parameters.AddWithValue("$SettlementStatus", (int)entity.SettlementStatus);
        command.Parameters.AddWithValue("$OccurredAt", DateTimeToDb(entity.OccurredAt));
        command.Parameters.AddWithValue("$DueDate", DateTimeToDb(entity.DueDate));
        command.Parameters.AddWithValue("$CategoryName", TextToDb(entity.CategoryName));
        command.Parameters.AddWithValue("$OrderId", GuidToDb(entity.OrderId));
        command.Parameters.AddWithValue("$PaymentRecordId", GuidToDb(entity.PaymentRecordId));
        command.Parameters.AddWithValue("$ImportBatchId", TextToDb(entity.ImportBatchId));
        command.Parameters.AddWithValue("$SourceRowKey", TextToDb(entity.SourceRowKey));
        command.Parameters.AddWithValue("$BusinessKey", TextToDb(entity.BusinessKey));
    }

    protected override CashFlowEntry MapEntity(SqliteDataReader reader)
    {
        return new CashFlowEntry
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Direction = GetEnum<CashFlowDirection>(reader, "Direction"),
            Amount = GetMoney(reader, "Amount"),
            SettledAmount = GetMoney(reader, "SettledAmount"),
            SettlementStatus = GetEnum<CashFlowSettlementStatus>(reader, "SettlementStatus"),
            OccurredAt = GetDateTime(reader, "OccurredAt"),
            DueDate = GetDateTimeNullable(reader, "DueDate"),
            CategoryName = GetStringNullable(reader, "CategoryName"),
            OrderId = GetGuidNullable(reader, "OrderId"),
            PaymentRecordId = GetGuidNullable(reader, "PaymentRecordId"),
            ImportBatchId = GetStringNullable(reader, "ImportBatchId"),
            SourceRowKey = GetStringNullable(reader, "SourceRowKey"),
            BusinessKey = GetStringNullable(reader, "BusinessKey"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
