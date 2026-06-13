using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="UnitDefinition"/> (table <c>CommerceUnitDefinitions</c>).</summary>
public sealed class UnitDefinitionRepository : CommerceRepositoryBase<UnitDefinition>, IUnitDefinitionRepository
{
    private const string Table = "CommerceUnitDefinitions";

    public UnitDefinitionRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "TemplateId", "Code", "IsBuiltIn", "DisplayName",
    };

    protected override void BindEntity(SqliteCommand command, UnitDefinition entity)
    {
        command.Parameters.AddWithValue("$TemplateId", GuidToDb(entity.TemplateId));
        command.Parameters.AddWithValue("$Code", entity.Code);
        command.Parameters.AddWithValue("$IsBuiltIn", entity.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$DisplayName", entity.DisplayName);
    }

    protected override UnitDefinition MapEntity(SqliteDataReader reader)
    {
        return new UnitDefinition
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            TemplateId = GetGuidNullable(reader, "TemplateId"),
            Code = GetString(reader, "Code"),
            IsBuiltIn = GetBool(reader, "IsBuiltIn"),
            DisplayName = GetString(reader, "DisplayName"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
