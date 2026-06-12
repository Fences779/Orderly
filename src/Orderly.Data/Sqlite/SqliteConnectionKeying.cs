using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

/// <summary>
/// 统一的 SQLCipher 连接加密接入点。所有受保护连接在打开后立即下发 raw key（避免再次 KDF），
/// 然后启用外键约束。密钥提供者必须返回 32 字节原始密钥的「副本」，本类型在使用后会清零该副本。
/// </summary>
public static class SqliteConnectionKeying
{
    internal const int RawKeyByteLength = 32;

    /// <summary>
    /// 订阅连接的状态变更，在连接打开瞬间应用 SQLCipher raw key。
    /// </summary>
    public static void AttachKeyOnOpen(SqliteConnection connection, Func<byte[]?> keyProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(keyProvider);

        connection.StateChange += (sender, args) =>
        {
            if (args.CurrentState != ConnectionState.Open || sender is not SqliteConnection openedConnection)
            {
                return;
            }

            ApplyRawKey(openedConnection, keyProvider);
        };
    }

    /// <summary>
    /// 立即在已打开（或即将使用）的连接上应用 SQLCipher raw key 并启用外键。
    /// </summary>
    public static void ApplyRawKey(SqliteConnection connection, Func<byte[]?> keyProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(keyProvider);

        var key = keyProvider();
        try
        {
            ApplyRawKeyCore(connection, key);
        }
        finally
        {
            if (key is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>
    /// 使用调用方持有的 raw key 副本应用 SQLCipher key。调用方负责清零传入的密钥。
    /// </summary>
    public static void ApplyRawKey(SqliteConnection connection, byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ApplyRawKeyCore(connection, key);
    }

    private static void ApplyRawKeyCore(SqliteConnection connection, byte[]? key)
    {
        if (key is null || key.Length == 0)
        {
            throw new InvalidOperationException("数据库加密密钥不可用，已拒绝打开数据库连接。");
        }

        if (key.Length != RawKeyByteLength)
        {
            throw new InvalidOperationException("数据库加密密钥长度无效。");
        }

        // SQLCipher raw key 形式 x'<hex>'：直接使用 256 位密钥，盐由 DB 头管理，跳过口令 KDF。
        var hex = Convert.ToHexString(key);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA key = \"x'{hex}'\";\nPRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }
}
