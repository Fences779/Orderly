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

        if (!decimal.TryParse(decrypted, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is not a valid decimal.");
        }

        return value;
    }

    public static double? ReadOptionalDouble(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            return null;
        }

        if (!double.TryParse(decrypted, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is not a valid double.");
        }

        return value;
    }

    public static DateTime ReadRequiredDateTime(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is empty.");
        }

        if (!DateTime.TryParse(decrypted, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is not a valid DateTime.");
        }

        return value;
    }

    public static DateTime? ReadOptionalDateTime(SqliteDataReader reader, int cipherIndex, IFieldEncryptionService fieldEncryptionService, string fieldName)
    {
        var decrypted = DecryptRequired(reader, cipherIndex, fieldEncryptionService, fieldName);
        if (string.IsNullOrWhiteSpace(decrypted))
        {
            return null;
        }

        if (!DateTime.TryParse(decrypted, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} is not a valid DateTime.");
        }

        return value;
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

        try
        {
            return fieldEncryptionService.Decrypt(cipher);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Encrypted field {fieldName} cannot be decrypted.", ex);
        }
    }
}
