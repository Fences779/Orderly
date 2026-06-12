using Orderly.Core.Services;

namespace Orderly.Data.Services;

internal static class EncryptedFieldScope
{
    public static string Encrypt(IFieldEncryptionService fieldEncryptionService, string plaintext, string legacyFieldName, long rowId)
    {
        // Fail-closed 兜底：若写入路径被注入空操作加密器，绝不回写明文（脱敏占位符替代），避免敏感字段明文落盘。
        return FieldEncryptionGuard.ProtectFieldValue(
            fieldEncryptionService,
            () => fieldEncryptionService.Encrypt(plaintext, Build(legacyFieldName, rowId)));
    }

    public static string EncryptOrEmpty(IFieldEncryptionService fieldEncryptionService, string plaintext, string legacyFieldName, long rowId)
    {
        return rowId <= 0 ? string.Empty : Encrypt(fieldEncryptionService, plaintext, legacyFieldName, rowId);
    }

    public static string Build(string table, string cipherColumn, long rowId)
    {
        return Build(BuildLegacyName(table, cipherColumn), rowId);
    }

    public static string Build(string legacyFieldName, long rowId)
    {
        if (rowId <= 0)
        {
            throw new InvalidOperationException("Encrypted field row scope requires a persisted row id.");
        }

        return $"{legacyFieldName}|row:{rowId}";
    }

    public static string BuildLegacyName(string table, string cipherColumn)
    {
        return $"{table}.{cipherColumn}";
    }
}
