using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

public sealed class LauncherConnectionFactory
{
    private readonly string _connectionString;
    private readonly Func<byte[]?>? _keyProvider;

    public LauncherConnectionFactory(string launcherDatabasePath)
        : this(launcherDatabasePath, keyProvider: null)
    {
    }

    /// <summary>
    /// 受 SQLCipher 全库加密保护的启动器连接工厂。启动器库在登录前即需打开（用于列出账号与认证），
    /// 因此使用本机 DPAPI 保护密钥（<paramref name="keyProvider"/>），而非会话数据密钥。
    /// </summary>
    public LauncherConnectionFactory(string launcherDatabasePath, Func<byte[]?>? keyProvider)
    {
        DatabasePath = launcherDatabasePath;
        _keyProvider = keyProvider;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = launcherDatabasePath
        };

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
