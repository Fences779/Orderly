using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Orderly.Data.Repositories;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Tests for the removal of the deleted startup pages (订单履约 / 异常处理) from navigation and the
/// startup-section persistence whitelist, and the remapping of any saved value that still points at
/// them so an upgraded user never starts on — or navigates into — a blank main content area.
///
/// Mapping: 订单履约 → 订单, 异常处理 → 经营建议, any other unrecognized value → 工作台.
/// The only valid pages are the nine current top-level sections.
/// </summary>
public class StartupSectionMappingTests
{
    [Theory]
    [InlineData("订单履约", "订单")]
    [InlineData("异常处理", "经营建议")]
    [InlineData("不存在的页面", "工作台")]
    [InlineData("工作台", "工作台")]
    [InlineData("现金流", "现金流")]
    public void Stored_startup_default_section_is_mapped_to_a_current_page(string stored, string expected)
    {
        WithSettingsDatabase(path =>
        {
            SeedSetting(path, AppSettingKeys.StartupDefaultSection, stored);

            var repository = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences preferences = repository.GetPreferencesAsync().GetAwaiter().GetResult();

            Assert.Equal(expected, preferences.StartupDefaultSection);
        });
    }

    [Theory]
    [InlineData("订单履约", "订单")]
    [InlineData("异常处理", "经营建议")]
    [InlineData("话术库", "工作台")]
    public void Stored_last_section_is_mapped_to_a_current_page(string stored, string expected)
    {
        WithSettingsDatabase(path =>
        {
            SeedSetting(path, AppSettingKeys.LastSection, stored);

            var repository = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences preferences = repository.GetPreferencesAsync().GetAwaiter().GetResult();

            Assert.Equal(expected, preferences.LastSection);
        });
    }

    [Fact]
    public void Saving_a_legacy_startup_section_persists_the_mapped_value()
    {
        WithSettingsDatabase(path =>
        {
            var repository = new AppSettingRepository(new SqliteConnectionFactory(path));
            AppPreferences preferences = repository.GetPreferencesAsync().GetAwaiter().GetResult();
            preferences.StartupDefaultSection = "异常处理";
            preferences.LastSection = "订单履约";

            repository.SavePreferencesAsync(preferences).GetAwaiter().GetResult();

            Assert.Equal("经营建议", ReadSetting(path, AppSettingKeys.StartupDefaultSection));
            Assert.Equal("订单", ReadSetting(path, AppSettingKeys.LastSection));
        });
    }

    [Theory]
    [InlineData("订单履约", "订单")]
    [InlineData("异常处理", "经营建议")]
    [InlineData("客户/订单", "工作台")]
    [InlineData("", "工作台")]
    [InlineData("库存管理", "库存管理")]
    public void NormalizeSection_routes_legacy_and_invalid_values_to_a_current_page(string input, string expected)
    {
        // NormalizeSection is the single routing chokepoint in MainViewModel; a relocated or invalid
        // section must always resolve to a real page so the content area is never blank.
        MethodInfo normalize = typeof(MainViewModel).GetMethod(
            "NormalizeSection",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var actual = (string)normalize.Invoke(null, new object?[] { input })!;

        Assert.Equal(expected, actual);
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

    private static string? ReadSetting(string path, string key)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void WithSettingsDatabase(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-startup-section-{Guid.NewGuid():N}.db");
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
