using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

/// <summary>
/// 将既有「明文」SQLite 库一次性迁移为 SQLCipher 全库加密库。
/// 迁移策略：检测明文 → 校验非链接 → 用 sqlcipher_export 导出到临时加密文件 →
/// 原始库改名为 .pre-encrypt.bak 备份 → 原子替换为加密库 → 清理陈旧 WAL/journal 旁路文件。
/// 任意环节失败均保留原始库（fail-safe），不丢数据。
/// </summary>
public static class SqliteDatabaseEncryptionMigrator
{
    private const string BackupSuffix = ".pre-encrypt.bak";

    public static void EnsureEncrypted(string databasePath, Func<byte[]?> keyProvider, string description)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        if (!File.Exists(databasePath))
        {
            // 全新库：将由带密钥的连接首次写入时创建为加密库，无需迁移。
            return;
        }

        LocalDataFileSecurity.EnsureFileIsNotLinked(databasePath, description);

        if (new FileInfo(databasePath).Length == 0)
        {
            // 0 字节占位文件：带密钥打开时初始化为加密库。
            return;
        }

        if (!IsPlaintextDatabase(databasePath))
        {
            // 已是加密库（或无法在无密钥下读取），无需迁移。
            return;
        }

        var key = keyProvider();
        if (key is null || key.Length != SqliteConnectionKeying.RawKeyByteLength)
        {
            if (key is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(key);
            }

            throw new InvalidOperationException($"{description}加密迁移所需密钥不可用或长度无效。");
        }

        try
        {
            MigrateToEncrypted(databasePath, key, description);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool IsPlaintextDatabase(string databasePath)
    {
        try
        {
            using var connection = new SqliteConnection(BuildUnpooledConnectionString(databasePath));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA schema_version;";
            command.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            // 加密库在无密钥下读取首个页会抛 SQLITE_NOTADB（26）。
            return false;
        }
    }

    private static void MigrateToEncrypted(string databasePath, byte[] key, string description)
    {
        var hex = Convert.ToHexString(key);
        var encryptedTempPath = databasePath + ".enc-" + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = databasePath + BackupSuffix;

        DeleteFileIfSafe(encryptedTempPath);

        try
        {
            using (var connection = new SqliteConnection(BuildUnpooledConnectionString(databasePath)))
            {
                connection.Open();
                TryExecute(connection, "PRAGMA wal_checkpoint(TRUNCATE);", ignoreErrors: true);

                var escapedTempPath = encryptedTempPath.Replace("'", "''", StringComparison.Ordinal);
                Execute(connection, $"ATTACH DATABASE '{escapedTempPath}' AS encrypted KEY \"x'{hex}'\";");
                Execute(connection, "SELECT sqlcipher_export('encrypted');");
                Execute(connection, "DETACH DATABASE encrypted;");
            }

            LocalDataFileSecurity.EnsureFileIsNotLinked(encryptedTempPath, description);
            if (!IsEncryptedDatabase(encryptedTempPath, key))
            {
                throw new InvalidOperationException($"{description}加密迁移校验失败：导出的加密库无法用目标密钥打开。");
            }

            LocalDataFileSecurity.HardenFile(encryptedTempPath);

            // 备份原始明文库（覆盖旧备份），随后原子替换为加密库。
            DeleteFileIfSafe(backupPath);
            File.Move(databasePath, backupPath);

            try
            {
                File.Move(encryptedTempPath, databasePath);
            }
            catch
            {
                // 替换失败：回滚，恢复原始明文库，避免数据不可访问。
                if (!File.Exists(databasePath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, databasePath);
                }

                throw;
            }

            LocalDataFileSecurity.HardenFile(databasePath);
            DeleteStaleSidecarFiles(databasePath);
        }
        catch
        {
            DeleteFileIfSafe(encryptedTempPath);
            throw;
        }
    }

    private static bool IsEncryptedDatabase(string databasePath, byte[] key)
    {
        try
        {
            using var connection = new SqliteConnection(BuildUnpooledConnectionString(databasePath));
            connection.Open();
            SqliteConnectionKeying.ApplyRawKey(connection, key.ToArray());
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master;";
            command.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void DeleteStaleSidecarFiles(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            DeleteFileIfSafe(databasePath + suffix);
        }
    }

    private static void DeleteFileIfSafe(string path)
    {
        try
        {
            if (File.Exists(path) && !LocalDataFileSecurity.IsReparsePoint(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void TryExecute(SqliteConnection connection, string sql, bool ignoreErrors)
    {
        try
        {
            Execute(connection, sql);
        }
        catch (SqliteException) when (ignoreErrors)
        {
        }
    }

    private static string BuildUnpooledConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
    }
}
