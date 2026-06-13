using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Repositories;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// 偏好读写往返测试，针对新增的可空字段 <see cref="AppPreferences.AvatarReference"/>。
///
/// AvatarReference 经现有 KV upsert 通道读写，不引入 schema 迁移；其语义为
/// 「null 表示使用默认头像」，因此缺失该键的旧数据与写入空白值均应读回 null。
///
/// Validates: Requirements 11.1, 11.3
/// </summary>
public class AvatarReferencePersistenceTests
{
    [Fact]
    public void Saved_avatar_reference_round_trips_back_to_the_same_value()
    {
        WithSettingsDatabase(path =>
        {
            const string reference = "avatars/account-1.png";

            var writer = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences preferences = writer.GetPreferencesAsync().GetAwaiter().GetResult();
            preferences.AvatarReference = reference;
            writer.SavePreferencesAsync(preferences).GetAwaiter().GetResult();

            // 用一个全新的仓储实例读取，确保走真实的持久化往返而非内存残留。
            var reader = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences reloaded = reader.GetPreferencesAsync().GetAwaiter().GetResult();

            Assert.Equal(reference, reloaded.AvatarReference);
        });
    }

    [Fact]
    public void Legacy_data_without_the_avatar_key_reads_back_null()
    {
        WithSettingsDatabase(path =>
        {
            // 模拟升级前的旧库：仅有其它设置键，从未写入过 AvatarReference。
            SeedSetting(path, AppSettingKeys.StartupDefaultSection, "工作台");

            var repository = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences preferences = repository.GetPreferencesAsync().GetAwaiter().GetResult();

            Assert.Null(preferences.AvatarReference);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_avatar_reference_round_trips_back_to_null(string blank)
    {
        WithSettingsDatabase(path =>
        {
            var writer = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences preferences = writer.GetPreferencesAsync().GetAwaiter().GetResult();
            preferences.AvatarReference = blank;
            writer.SavePreferencesAsync(preferences).GetAwaiter().GetResult();

            var reader = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences reloaded = reader.GetPreferencesAsync().GetAwaiter().GetResult();

            Assert.Null(reloaded.AvatarReference);
        });
    }

    // --- Helpers ---

    private static void SeedSetting(string path, string key, string value)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO AppSettings (Key, Value) VALUES ($key, $value) " +
            "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void WithSettingsDatabase(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-avatar-pref-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS AppSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }

            action(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
