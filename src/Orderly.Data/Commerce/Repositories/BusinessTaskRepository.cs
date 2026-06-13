using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;
using TaskStatus = Orderly.Core.Commerce.TaskStatus;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="BusinessTask"/> (table <c>CommerceBusinessTasks</c>).</summary>
public sealed class BusinessTaskRepository : CommerceRepositoryBase<BusinessTask>, IBusinessTaskRepository
{
    private const string Table = "CommerceBusinessTasks";

    public BusinessTaskRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Title", "Description", "Status", "DueDate", "CompletedAt", "CustomerId", "OrderId",
    };

    protected override void BindEntity(SqliteCommand command, BusinessTask entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Title", entity.Title);
        command.Parameters.AddWithValue("$Description", TextToDb(entity.Description));
        command.Parameters.AddWithValue("$Status", (int)entity.Status);
        command.Parameters.AddWithValue("$DueDate", DateTimeToDb(entity.DueDate));
        command.Parameters.AddWithValue("$CompletedAt", DateTimeToDb(entity.CompletedAt));
        command.Parameters.AddWithValue("$CustomerId", GuidToDb(entity.CustomerId));
        command.Parameters.AddWithValue("$OrderId", GuidToDb(entity.OrderId));
    }

    protected override BusinessTask MapEntity(SqliteDataReader reader)
    {
        return new BusinessTask
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Title = GetString(reader, "Title"),
            Description = GetStringNullable(reader, "Description"),
            Status = GetEnum<TaskStatus>(reader, "Status"),
            DueDate = GetDateTimeNullable(reader, "DueDate"),
            CompletedAt = GetDateTimeNullable(reader, "CompletedAt"),
            CustomerId = GetGuidNullable(reader, "CustomerId"),
            OrderId = GetGuidNullable(reader, "OrderId"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
