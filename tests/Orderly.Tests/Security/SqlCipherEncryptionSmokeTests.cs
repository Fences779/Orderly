using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Security;

// Runtime regression tests for SQLCipher full-database encryption.
// Validates real encryption, key isolation, and plaintext->encrypted migration.
public sealed class SqlCipherEncryptionSmokeTests : IDisposable
{
    private readonly string _dir;

    public SqlCipherEncryptionSmokeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "orderly-sqlcipher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] Key(byte seed) => Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray();

    private static void Seed(SqliteConnectionFactory factory)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE Secret(Id INTEGER PRIMARY KEY, Body TEXT); INSERT INTO Secret(Body) VALUES('TOP_SECRET_MARKER_42');";
        cmd.ExecuteNonQuery();
    }

    private static string ReadBody(SqliteConnectionFactory factory)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Body FROM Secret LIMIT 1;";
        return (string)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void EncryptedDb_RoundTrips_WithCorrectKey()
    {
        var path = Path.Combine(_dir, "rt.db");
        Seed(new SqliteConnectionFactory(path, () => Key(1)));
        Assert.Equal("TOP_SECRET_MARKER_42", ReadBody(new SqliteConnectionFactory(path, () => Key(1))));
    }

    [Fact]
    public void EncryptedDb_RawBytes_DoNotContainPlaintext()
    {
        var path = Path.Combine(_dir, "raw.db");
        Seed(new SqliteConnectionFactory(path, () => Key(2)));
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.DoesNotContain("TOP_SECRET_MARKER_42", text);
        // 加密库不应有标准 SQLite "SQLite format 3" 文件头明文。
        Assert.DoesNotContain("SQLite format 3", text);
    }

    [Fact]
    public void EncryptedDb_WrongKey_Fails()
    {
        var path = Path.Combine(_dir, "wrong.db");
        Seed(new SqliteConnectionFactory(path, () => Key(3)));
        SqliteConnection.ClearAllPools();
        Assert.ThrowsAny<Exception>(() => ReadBody(new SqliteConnectionFactory(path, () => Key(9))));
    }

    [Fact]
    public void EncryptedDb_NoKey_Fails()
    {
        var path = Path.Combine(_dir, "nokey.db");
        Seed(new SqliteConnectionFactory(path, () => Key(4)));
        SqliteConnection.ClearAllPools();
        Assert.ThrowsAny<Exception>(() => ReadBody(new SqliteConnectionFactory(path)));
    }

    [Fact]
    public void Migrator_ConvertsPlaintext_PreservesData_AndBacksUp()
    {
        var path = Path.Combine(_dir, "legacy.db");
        // 1) 建立明文库
        Seed(new SqliteConnectionFactory(path));
        SqliteConnection.ClearAllPools();
        var plaintextBytes = File.ReadAllBytes(path);
        Assert.Contains("SQLite format 3", System.Text.Encoding.ASCII.GetString(plaintextBytes));

        // 2) 迁移为加密库
        var key = Key(5);
        SqliteDatabaseEncryptionMigrator.EnsureEncrypted(path, () => (byte[])key.Clone(), "测试库");
        SqliteConnection.ClearAllPools();

        // 3) 备份存在且为原明文库
        Assert.True(File.Exists(path + ".pre-encrypt.bak"));
        Assert.Contains("SQLite format 3", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path + ".pre-encrypt.bak")));

        // 4) 现库已加密且数据保留
        var nowBytes = File.ReadAllBytes(path);
        Assert.DoesNotContain("SQLite format 3", System.Text.Encoding.ASCII.GetString(nowBytes));
        Assert.Equal("TOP_SECRET_MARKER_42", ReadBody(new SqliteConnectionFactory(path, () => (byte[])key.Clone())));

        // 5) 对已加密库再次迁移应为 no-op（不再生成新备份内容差异）
        SqliteDatabaseEncryptionMigrator.EnsureEncrypted(path, () => (byte[])key.Clone(), "测试库");
        SqliteConnection.ClearAllPools();
        Assert.Equal("TOP_SECRET_MARKER_42", ReadBody(new SqliteConnectionFactory(path, () => (byte[])key.Clone())));
    }
}
