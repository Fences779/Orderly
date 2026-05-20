using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IAppSettingRepository
{
    Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default);
    Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default);
}
