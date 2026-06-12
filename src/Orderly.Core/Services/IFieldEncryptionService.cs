namespace Orderly.Core.Services;

public interface IFieldEncryptionService
{
    string Encrypt(string plaintext);
    string Encrypt(string plaintext, string associatedData);
    string Decrypt(string ciphertext);
    string Decrypt(string ciphertext, string associatedData);
    bool UsesAssociatedDataPayload(string ciphertext);
}
