using Microsoft.Data.Sqlite;
using Orderly.Core.Services;
using System.Globalization;

namespace Orderly.Data.Repositories;

internal static class EncryptedColumnReader
{
    public static string ReadRequiredString(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        return DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
    }

    public static decimal ReadRequiredDecimal(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is empty.");
        }

        return decimal.Parse(decrypted, CultureInfo.InvariantCulture);
    }

    public static double? ReadOptionalDouble(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            return null;
        }

        return double.Parse(decrypted, CultureInfo.InvariantCulture);
    }

    public static DateTime ReadRequiredDateTime(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is empty.");
        }

        return DateTime.Parse(decrypted, null, DateTimeStyles.RoundtripKind);
    }

    public static DateTime? ReadOptionalDateTime(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            return null;
        }

        return DateTime.Parse(decrypted, null, DateTimeStyles.RoundtripKind);
    }

    private static string DecryptRequired(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        if (reader.IsDBNull(cipherIndex))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is missing.");
        }

        var cipher = reader.GetString(cipherIndex);
        if (string.IsNullOrWhiteSpace(cipher))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is empty.");
        }

        return fieldEncryptionService.Decrypt(cipher);
    }
}
