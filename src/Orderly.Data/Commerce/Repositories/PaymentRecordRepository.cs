using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="PaymentRecord"/> (table <c>CommercePaymentRecords</c>).</summary>
public sealed class PaymentRecordRepository : CommerceRepositoryBase<PaymentRecord>, IPaymentRecordRepository
{
    private const string Table = "CommercePaymentRecords";

    public PaymentRecordRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "OrderId", "CashFlowEntryId", "Amount", "PaidAt", "Method", "BusinessKey",
    };

    protected override void BindEntity(SqliteCommand command, PaymentRecord entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$OrderId", GuidToDb(entity.OrderId));
        command.Parameters.AddWithValue("$CashFlowEntryId", GuidToDb(entity.CashFlowEntryId));
        command.Parameters.AddWithValue("$Amount", MoneyToDb(entity.Amount));
        command.Parameters.AddWithValue("$PaidAt", DateTimeToDb(entity.PaidAt));
        command.Parameters.AddWithValue("$Method", TextToDb(entity.Method));
        command.Parameters.AddWithValue("$BusinessKey", TextToDb(entity.BusinessKey));
    }

    protected override PaymentRecord MapEntity(SqliteDataReader reader)
    {
        return new PaymentRecord
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            OrderId = GetGuidNullable(reader, "OrderId"),
            CashFlowEntryId = GetGuidNullable(reader, "CashFlowEntryId"),
            Amount = GetMoney(reader, "Amount"),
            PaidAt = GetDateTime(reader, "PaidAt"),
            Method = GetStringNullable(reader, "Method"),
            BusinessKey = GetStringNullable(reader, "BusinessKey"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
