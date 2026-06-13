using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="BusinessWorkspace"/> (table <c>CommerceBusinessWorkspaces</c>).</summary>
public sealed class BusinessWorkspaceRepository : CommerceRepositoryBase<BusinessWorkspace>, IBusinessWorkspaceRepository
{
    private const string Table = "CommerceBusinessWorkspaces";

    public BusinessWorkspaceRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "Name", "ActiveTemplateId", "DefaultCurrencyCode",
    };

    protected override void BindEntity(SqliteCommand command, BusinessWorkspace entity)
    {
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$ActiveTemplateId", GuidToDb(entity.ActiveTemplateId));
        command.Parameters.AddWithValue("$DefaultCurrencyCode", TextToDb(entity.DefaultCurrencyCode));
    }

    protected override BusinessWorkspace MapEntity(SqliteDataReader reader)
    {
        return new BusinessWorkspace
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            Name = GetString(reader, "Name"),
            ActiveTemplateId = GetGuidNullable(reader, "ActiveTemplateId"),
            DefaultCurrencyCode = GetStringNullable(reader, "DefaultCurrencyCode"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
