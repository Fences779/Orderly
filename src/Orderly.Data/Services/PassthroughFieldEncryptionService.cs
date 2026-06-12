using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class PassthroughFieldEncryptionService : IFieldEncryptionService
{
    public static PassthroughFieldEncryptionService Instance { get; } = new();

    public string Encrypt(string plaintext)
    {
        return plaintext ?? string.Empty;
    }

    public string Encrypt(string plaintext, string associatedData)
    {
        return Encrypt(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        return ciphertext ?? string.Empty;
    }

    public string Decrypt(string ciphertext, string associatedData)
    {
        return Decrypt(ciphertext);
    }

    public bool UsesAssociatedDataPayload(string ciphertext)
    {
        return false;
    }
}
