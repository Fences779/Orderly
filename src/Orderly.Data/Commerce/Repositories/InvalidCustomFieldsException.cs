namespace Orderly.Data.Commerce.Repositories;

/// <summary>
/// Thrown at the Commerce repository save boundary when an entity's <c>CustomFieldsJson</c> value is
/// non-null but is not well-formed JSON (Requirement 3.11, 3.12). The check runs <b>before</b> any
/// database connection is opened or any row is written, so a rejected save leaves all existing
/// persisted data unchanged (no partial write).
///
/// <para>The Commerce repositories surface expected failures as exceptions (matching the rest of
/// <see cref="CommerceRepositoryBase{TEntity}"/>); the Commerce_Service_Layer catches this and maps
/// it to the typed <c>InvalidCustomFields</c> failure result it returns to callers.</para>
/// </summary>
public sealed class InvalidCustomFieldsException : Exception
{
    private const string DefaultMessage =
        "无法保存：自定义字段内容不是有效的 JSON。"; // "Cannot save: custom-field content is not valid JSON."

    public InvalidCustomFieldsException()
        : base(DefaultMessage)
    {
    }

    public InvalidCustomFieldsException(string message)
        : base(message)
    {
    }

    public InvalidCustomFieldsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
