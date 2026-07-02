using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Orderly.Server.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    // Argon2id parameters per implementation plan.
    private const int MemorySizeKiB = 64 * 1024;
    private const int Iterations = 3;
    private const int Parallelism = 1;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = HashCore(password, salt);
        return $"$argon2id$v=19$m={MemorySizeKiB},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id") return false;

        var paramParts = parts[2].Split(',');
        if (paramParts.Length != 3) return false;

        var salt = Convert.FromBase64String(parts[3]);
        var expectedHash = Convert.FromBase64String(parts[4]);
        var actualHash = HashCore(password, salt);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static byte[] HashCore(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemorySizeKiB,
            Iterations = Iterations,
            DegreeOfParallelism = Parallelism
        };
        return argon2.GetBytes(HashLength);
    }
}
