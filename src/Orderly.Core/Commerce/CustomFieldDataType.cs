namespace Orderly.Core.Commerce;

/// <summary>
/// The data type of a user-defined custom field stored in an entity's <c>CustomFieldsJson</c>.
/// </summary>
public enum CustomFieldDataType
{
    Text = 0,
    Number = 1,
    Decimal = 2,
    Boolean = 3,
    Date = 4,
    DateTime = 5,
    SingleSelect = 6,
    MultiSelect = 7,
    Currency = 8
}
