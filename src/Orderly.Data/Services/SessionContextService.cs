using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class SessionContextService : ISessionContextService
{
    private LocalSessionContext? _current;

    public event EventHandler? SessionChanged;

    public LocalSessionContext? Current => _current;

    public bool IsSignedIn => _current is not null;

    public void SetCurrent(LocalSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var copiedDataKey = session.DataKey.ToArray();
        _current = new LocalSessionContext
        {
            AccountId = session.AccountId,
            Username = session.Username,
            DisplayName = session.DisplayName,
            Role = session.Role,
            DatabasePath = session.DatabasePath,
            DataKey = copiedDataKey,
            SignedInAt = session.SignedInAt
        };

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_current?.DataKey is { Length: > 0 } key)
        {
            Array.Clear(key, 0, key.Length);
        }

        _current = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
