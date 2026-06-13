using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="BusinessTemplate"/> (table <c>CommerceBusinessTemplates</c>).</summary>
public sealed class BusinessTemplateRepository : CommerceRepositoryBase<BusinessTemplate>, IBusinessTemplateRepository
{
    private const string Table = "CommerceBusinessTemplates";

    public BusinessTemplateRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "TemplateKey", "WorkspaceId", "IsBuiltIn", "DisplayName", "ConfigJson",
    };

    protected override void BindEntity(SqliteCommand command, BusinessTemplate entity)
    {
        command.Parameters.AddWithValue("$TemplateKey", entity.TemplateKey);
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$IsBuiltIn", entity.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$DisplayName", entity.DisplayName);
        command.Parameters.AddWithValue("$ConfigJson", TextToDb(entity.ConfigJson));
    }

    protected override BusinessTemplate MapEntity(SqliteDataReader reader)
    {
        return new BusinessTemplate
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            TemplateKey = GetString(reader, "TemplateKey"),
            WorkspaceId = GetGuidNullable(reader, "WorkspaceId"),
            IsBuiltIn = GetBool(reader, "IsBuiltIn"),
            DisplayName = GetString(reader, "DisplayName"),
            ConfigJson = GetStringNullable(reader, "ConfigJson"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
