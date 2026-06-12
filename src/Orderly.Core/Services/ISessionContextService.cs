using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ISessionContextService
{
    event EventHandler? SessionChanged;

    LocalSessionContext? Current { get; }
    bool IsSignedIn { get; }
    bool IsDataKeyAvailable { get; }

    void SetCurrent(LocalSessionContext session);
    void SuspendDataKey();
    bool TryRestoreDataKey(string accountId);
    void Clear();
}
