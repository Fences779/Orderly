using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Repositories;

public sealed class AppSettingRepository : IAppSettingRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AppSettingRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings;";

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return new AppPreferences
        {
            MainHotkey = Get(settings, "MainHotkey", "Ctrl+Alt+O"),
            FloatingHotkey = Get(settings, "FloatingHotkey", "Ctrl+Alt+R"),
            ShowFloatingWindowOnStartup = bool.TryParse(Get(settings, "ShowFloatingWindowOnStartup", "false"), out var showFloating) && showFloating,
            StartMinimizedToTray = bool.TryParse(Get(settings, "StartMinimizedToTray", "false"), out var startMinimized) && startMinimized
        };
    }

    private static string Get(IReadOnlyDictionary<string, string> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) ? value : fallback;
    }
}
