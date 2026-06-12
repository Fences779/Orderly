using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly Func<byte[]?>? _keyProvider;

    /// <summary>
    /// 不加密连接工厂。仅用于测试或显式无加密场景；生产路径必须使用带密钥提供者的重载。
    /// </summary>
    public SqliteConnectionFactory(string databasePath)
        : this(databasePath, keyProvider: null)
    {
    }

    /// <summary>
    /// 受 SQLCipher 全库加密保护的连接工厂。<paramref name="keyProvider"/> 必须返回 32 字节
    /// raw key 的副本（每次调用返回新副本），打开连接后会立即应用并清零该副本。
    /// </summary>
    public SqliteConnectionFactory(string databasePath, Func<byte[]?>? keyProvider)
    {
        DatabasePath = databasePath;
        _keyProvider = keyProvider;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        };

        // 加密连接的外键约束在应用 PRAGMA key 之后再启用，避免 MDS 在密钥下发前对加密库执行任何语句。
        if (keyProvider is null)
        {
            builder.ForeignKeys = true;
        }

        _connectionString = builder.ToString();
    }

    public string DatabasePath { get; }

    public bool IsEncrypted => _keyProvider is not null;

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        if (_keyProvider is not null)
        {
            SqliteConnectionKeying.AttachKeyOnOpen(connection, _keyProvider);
        }

        return connection;
    }
}
