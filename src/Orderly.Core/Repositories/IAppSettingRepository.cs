using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IAppSettingRepository
{
    Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default);
}
