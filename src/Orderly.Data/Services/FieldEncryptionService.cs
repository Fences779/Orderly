using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class FieldEncryptionService : IFieldEncryptionService
{
    private const string Prefix = "v1:";
    private const int PrefixLength = 3;
    private const int DataKeyByteLength = 32;
    private const int NonceByteLength = 12;
    private const int TagByteLength = 16;
    private const int HeaderByteLength = 1 + NonceByteLength + TagByteLength;
    private const int MaxPayloadByteLength = 16 * 1024 * 1024;
    private const int MaxPlaintextByteLength = MaxPayloadByteLength - HeaderByteLength;
    private const int MaxEncodedPayloadLength = ((MaxPayloadByteLength + 2) / 3) * 4;
    private const int MaxCiphertextLength = PrefixLength + MaxEncodedPayloadLength;
    private const int LegacyPayloadVersion = 1;
    private const int AssociatedDataPayloadVersion = 2;
    private const int MaxAssociatedDataByteLength = 256;

    private readonly ISessionContextService _sessionContextService;

    public FieldEncryptionService(ISessionContextService sessionContextService)
    {
        _sessionContextService = sessionContextService;
    }

    public string Encrypt(string plaintext)
    {
        return EncryptCore(plaintext, associatedData: string.Empty, useAssociatedData: false);
    }

    public string Encrypt(string plaintext, string associatedData)
    {
        return EncryptCore(plaintext, associatedData, useAssociatedData: true);
    }

    public string Decrypt(string ciphertext)
    {
        return DecryptCore(ciphertext, associatedData: string.Empty, allowAssociatedDataPayload: false);
    }

    public string Decrypt(string ciphertext, string associatedData)
    {
        return DecryptCore(ciphertext, associatedData, allowAssociatedDataPayload: true);
    }

    private string EncryptCore(string plaintext, string associatedData, bool useAssociatedData)
    {
        plaintext ??= string.Empty;
        var key = RequireCurrentDataKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var associatedDataBytes = useAssociatedData
            ? BuildAssociatedDataBytes(associatedData)
            : Array.Empty<byte>();
        if (plaintextBytes.Length > MaxPlaintextByteLength)
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(associatedDataBytes);
            throw new InvalidOperationException("Plaintext is too large to encrypt.");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceByteLength);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagByteLength];
        var payload = Array.Empty<byte>();

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedDataBytes);

            payload = new byte[HeaderByteLength + ciphertext.Length];
            payload[0] = (byte)(useAssociatedData ? AssociatedDataPayloadVersion : LegacyPayloadVersion);
            Buffer.BlockCopy(nonce, 0, payload, 1, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, 1 + nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, payload, 1 + nonce.Length + tag.Length, ciphertext.Length);
            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(associatedDataBytes);
        }
    }

    private string DecryptCore(string ciphertext, string associatedData, bool allowAssociatedDataPayload)
    {
        ciphertext ??= string.Empty;
        if (ciphertext.Length == 0)
        {
            return string.Empty;
        }

        if (ciphertext.Length > MaxCiphertextLength
            || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ciphertext payload is invalid.");
        }

        var encodedPayload = ciphertext[Prefix.Length..];
        if (encodedPayload.Length == 0 || encodedPayload.Length > MaxEncodedPayloadLength)
        {
            throw new InvalidOperationException("Ciphertext payload is invalid.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(encodedPayload);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Ciphertext payload is invalid.");
        }

        if (payload.Length < HeaderByteLength || payload.Length > MaxPayloadByteLength)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidOperationException("Ciphertext payload is invalid.");
        }

        var payloadVersion = payload[0];
        var usesAssociatedData = payloadVersion == AssociatedDataPayloadVersion;
        if (payloadVersion != LegacyPayloadVersion && payloadVersion != AssociatedDataPayloadVersion)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidOperationException("Ciphertext version is not supported.");
        }

        if (usesAssociatedData && !allowAssociatedDataPayload)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidOperationException("Ciphertext requires associated data.");
        }

        var key = RequireCurrentDataKey();
        var nonce = payload.AsSpan(1, NonceByteLength);
        var tag = payload.AsSpan(1 + NonceByteLength, TagByteLength);
        var encryptedBytes = payload.AsSpan(HeaderByteLength);
        var associatedDataBytes = Array.Empty<byte>();
        var plaintextBytes = Array.Empty<byte>();

        try
        {
            associatedDataBytes = usesAssociatedData
                ? BuildAssociatedDataBytes(associatedData)
                : Array.Empty<byte>();
            plaintextBytes = new byte[encryptedBytes.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, encryptedBytes, tag, plaintextBytes, associatedDataBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Ciphertext authentication failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(associatedDataBytes);
        }
    }

    private byte[] RequireCurrentDataKey()
    {
        var dataKey = _sessionContextService.Current?.DataKey;
        if (dataKey is null || dataKey.Length != DataKeyByteLength)
        {
            throw new InvalidOperationException("No signed-in session data key is available.");
        }

        return dataKey;
    }

    private static byte[] BuildAssociatedDataBytes(string associatedData)
    {
        if (string.IsNullOrWhiteSpace(associatedData))
        {
            throw new InvalidOperationException("Associated data is required.");
        }

        var normalized = associatedData.Trim();
        if (normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException("Associated data contains invalid characters.");
        }

        var bytes = Encoding.UTF8.GetBytes(normalized);
        if (bytes.Length > MaxAssociatedDataByteLength)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidOperationException("Associated data is too long.");
        }

        return bytes;
    }
}
