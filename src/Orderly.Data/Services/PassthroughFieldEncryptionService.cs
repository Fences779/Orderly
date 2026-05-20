using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class PassthroughFieldEncryptionService : IFieldEncryptionService
{
    public static PassthroughFieldEncryptionService Instance { get; } = new();

    public string Encrypt(string plaintext)
    {
        return plaintext ?? string.Empty;
    }

    public string Decrypt(string ciphertext)
    {
        return ciphertext ?? string.Empty;
    }
}
