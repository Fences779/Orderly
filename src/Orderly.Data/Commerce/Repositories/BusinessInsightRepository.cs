using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="BusinessInsight"/> (table <c>CommerceBusinessInsights</c>).</summary>
public sealed class BusinessInsightRepository : CommerceRepositoryBase<BusinessInsight>, IBusinessInsightRepository
{
    private const string Table = "CommerceBusinessInsights";

    public BusinessInsightRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Severity", "Title", "Message", "Category", "IsAcknowledged", "GeneratedAt", "BusinessKey",
    };

    protected override void BindEntity(SqliteCommand command, BusinessInsight entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Severity", (int)entity.Severity);
        command.Parameters.AddWithValue("$Title", entity.Title);
        command.Parameters.AddWithValue("$Message", entity.Message);
        command.Parameters.AddWithValue("$Category", TextToDb(entity.Category));
        command.Parameters.AddWithValue("$IsAcknowledged", entity.IsAcknowledged ? 1 : 0);
        command.Parameters.AddWithValue("$GeneratedAt", DateTimeToDb(entity.GeneratedAt));
        command.Parameters.AddWithValue("$BusinessKey", TextToDb(entity.BusinessKey));
    }

    protected override BusinessInsight MapEntity(SqliteDataReader reader)
    {
        return new BusinessInsight
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Severity = GetEnum<InsightSeverity>(reader, "Severity"),
            Title = GetString(reader, "Title"),
            Message = GetString(reader, "Message"),
            Category = GetStringNullable(reader, "Category"),
            IsAcknowledged = GetBool(reader, "IsAcknowledged"),
            GeneratedAt = GetDateTime(reader, "GeneratedAt"),
            BusinessKey = GetStringNullable(reader, "BusinessKey"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
