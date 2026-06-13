using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="CustomFieldDefinition"/> (table <c>CommerceCustomFieldDefinitions</c>).</summary>
public sealed class CustomFieldDefinitionRepository : CommerceRepositoryBase<CustomFieldDefinition>, ICustomFieldDefinitionRepository
{
    private const string Table = "CommerceCustomFieldDefinitions";

    public CustomFieldDefinitionRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "TemplateId", "TargetEntityType", "DataType", "FieldKey", "DisplayName", "IsRequired", "SortOrder", "OptionsJson",
    };

    protected override void BindEntity(SqliteCommand command, CustomFieldDefinition entity)
    {
        command.Parameters.AddWithValue("$TemplateId", GuidToDb(entity.TemplateId));
        command.Parameters.AddWithValue("$TargetEntityType", (int)entity.TargetEntityType);
        command.Parameters.AddWithValue("$DataType", (int)entity.DataType);
        command.Parameters.AddWithValue("$FieldKey", entity.FieldKey);
        command.Parameters.AddWithValue("$DisplayName", entity.DisplayName);
        command.Parameters.AddWithValue("$IsRequired", entity.IsRequired ? 1 : 0);
        command.Parameters.AddWithValue("$SortOrder", entity.SortOrder);
        command.Parameters.AddWithValue("$OptionsJson", TextToDb(entity.OptionsJson));
    }

    protected override CustomFieldDefinition MapEntity(SqliteDataReader reader)
    {
        return new CustomFieldDefinition
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            TemplateId = GetGuid(reader, "TemplateId"),
            TargetEntityType = GetEnum<BusinessEntityType>(reader, "TargetEntityType"),
            DataType = GetEnum<CustomFieldDataType>(reader, "DataType"),
            FieldKey = GetString(reader, "FieldKey"),
            DisplayName = GetString(reader, "DisplayName"),
            IsRequired = GetBool(reader, "IsRequired"),
            SortOrder = GetInt(reader, "SortOrder"),
            OptionsJson = GetStringNullable(reader, "OptionsJson"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
