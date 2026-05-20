using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ISessionContextService
{
    event EventHandler? SessionChanged;

    LocalSessionContext? Current { get; }
    bool IsSignedIn { get; }

    void SetCurrent(LocalSessionContext session);
    void Clear();
}
