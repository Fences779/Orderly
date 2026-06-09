using System.Security.Cryptography;

namespace Orderly.Data.Services;

internal static class SensitiveBuffer
{
    public static void Clear(params byte[][] arrays)
    {
        foreach (var array in arrays)
        {
            if (array.Length > 0)
            {
                CryptographicOperations.ZeroMemory(array);
            }
        }
    }

    public static void ClearWrappedDataKey((byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedDataKey)
    {
        Clear(wrappedDataKey.Ciphertext, wrappedDataKey.Nonce, wrappedDataKey.Tag);
    }
}
