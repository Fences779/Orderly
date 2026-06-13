using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="BusinessMetricSnapshot"/> (table <c>CommerceBusinessMetricSnapshots</c>).</summary>
public sealed class BusinessMetricSnapshotRepository : CommerceRepositoryBase<BusinessMetricSnapshot>, IBusinessMetricSnapshotRepository
{
    private const string Table = "CommerceBusinessMetricSnapshots";

    public BusinessMetricSnapshotRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "MetricKey", "CapturedAt", "NumericValue", "MoneyValue", "BusinessKey",
    };

    protected override void BindEntity(SqliteCommand command, BusinessMetricSnapshot entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$MetricKey", entity.MetricKey);
        command.Parameters.AddWithValue("$CapturedAt", DateTimeToDb(entity.CapturedAt));
        command.Parameters.AddWithValue("$NumericValue", DecimalToDb(entity.NumericValue));
        command.Parameters.AddWithValue("$MoneyValue", MoneyToDb(entity.MoneyValue));
        command.Parameters.AddWithValue("$BusinessKey", TextToDb(entity.BusinessKey));
    }

    protected override BusinessMetricSnapshot MapEntity(SqliteDataReader reader)
    {
        return new BusinessMetricSnapshot
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            MetricKey = GetString(reader, "MetricKey"),
            CapturedAt = GetDateTime(reader, "CapturedAt"),
            NumericValue = GetDecimal(reader, "NumericValue"),
            MoneyValue = GetMoneyNullable(reader, "MoneyValue"),
            BusinessKey = GetStringNullable(reader, "BusinessKey"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
