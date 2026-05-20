using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class FieldEncryptionService : IFieldEncryptionService
{
    private const string Prefix = "v1:";
    private readonly ISessionContextService _sessionContextService;

    public FieldEncryptionService(ISessionContextService sessionContextService)
    {
        _sessionContextService = sessionContextService;
    }

    public string Encrypt(string plaintext)
    {
        plaintext ??= string.Empty;
        var key = RequireCurrentDataKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        payload[0] = 1;
        Buffer.BlockCopy(nonce, 0, payload, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, 1 + nonce.Length + tag.Length, ciphertext.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string ciphertext)
    {
        ciphertext ??= string.Empty;
        if (ciphertext.Length == 0)
        {
            return string.Empty;
        }

        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ciphertext does not use a supported encryption prefix.");
        }

        var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        if (payload.Length < 1 + 12 + 16)
        {
            throw new InvalidOperationException("Ciphertext payload is invalid.");
        }

        if (payload[0] != 1)
        {
            throw new InvalidOperationException("Ciphertext version is not supported.");
        }

        var key = RequireCurrentDataKey();
        var nonce = payload[1..13];
        var tag = payload[13..29];
        var encryptedBytes = payload[29..];
        var plaintextBytes = new byte[encryptedBytes.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, encryptedBytes, tag, plaintextBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private byte[] RequireCurrentDataKey()
    {
        var dataKey = _sessionContextService.Current?.DataKey;
        if (dataKey is null || dataKey.Length == 0)
        {
            throw new InvalidOperationException("No signed-in session data key is available.");
        }

        return dataKey;
    }
}
