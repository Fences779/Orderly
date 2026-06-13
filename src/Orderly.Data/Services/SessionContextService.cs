using Orderly.Core.Models;
using Orderly.Core.Services;
using System.Security.Cryptography;

namespace Orderly.Data.Services;

public sealed class SessionContextService : ISessionContextService
{
    private const int DataKeyByteLength = 32;
    private static readonly byte[] ProtectionEntropy =
        SHA256.HashData("Orderly.SessionLock.DataKey.v1"u8);

    private LocalSessionContext? _current;
    private byte[] _protectedDataKey = [];
    private SessionPermissionMode _permissionMode = SessionPermissionMode.Normal;

    public event EventHandler? SessionChanged;

    public LocalSessionContext? Current => _current;

    public bool IsSignedIn => _current is not null;

    public bool IsDataKeyAvailable =>
        _current?.DataKey is { Length: DataKeyByteLength }
        && _protectedDataKey.Length == 0;

    public SessionPermissionMode CurrentPermissionMode => _permissionMode;

    public bool IsRestrictedPermissionMode =>
        _permissionMode == SessionPermissionMode.Restricted_Permission;

    public void SetPermissionMode(SessionPermissionMode mode)
    {
        if (_permissionMode == mode)
        {
            return;
        }

        _permissionMode = mode;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrent(LocalSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var copiedDataKey = session.DataKey.ToArray();
        ClearCurrentDataKey();
        ClearProtectedDataKey();

        _current = CopySession(session, copiedDataKey);
        _permissionMode = SessionPermissionMode.Normal;

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SuspendDataKey()
    {
        var session = _current;
        if (session is null || _protectedDataKey.Length > 0)
        {
            return;
        }

        if (session.DataKey.Length != DataKeyByteLength)
        {
            throw new InvalidOperationException("Current session data key is unavailable.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Session data-key suspension requires Windows DPAPI.");
        }

        var protectedDataKey = ProtectedData.Protect(
            session.DataKey,
            ProtectionEntropy,
            DataProtectionScope.CurrentUser);

        _protectedDataKey = protectedDataKey;
        CryptographicOperations.ZeroMemory(session.DataKey);
        _current = CopySession(session, []);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryRestoreDataKey(string accountId)
    {
        var session = _current;
        if (session is null
            || !string.Equals(session.AccountId, accountId, StringComparison.Ordinal)
            || _protectedDataKey.Length == 0)
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        byte[] dataKey = [];
        try
        {
            dataKey = ProtectedData.Unprotect(
                _protectedDataKey,
                ProtectionEntropy,
                DataProtectionScope.CurrentUser);
            if (dataKey.Length != DataKeyByteLength)
            {
                return false;
            }

            _current = CopySession(session, dataKey);
            dataKey = [];
            ClearProtectedDataKey();
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public void Clear()
    {
        ClearCurrentDataKey();
        ClearProtectedDataKey();
        _current = null;
        _permissionMode = SessionPermissionMode.Normal;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearCurrentDataKey()
    {
        if (_current?.DataKey is { Length: > 0 } key)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private void ClearProtectedDataKey()
    {
        CryptographicOperations.ZeroMemory(_protectedDataKey);
        _protectedDataKey = [];
    }

    private static LocalSessionContext CopySession(LocalSessionContext session, byte[] dataKey)
    {
        return new LocalSessionContext
        {
            AccountId = session.AccountId,
            Username = session.Username,
            DisplayName = session.DisplayName,
            Role = session.Role,
            DatabasePath = session.DatabasePath,
            DataKey = dataKey,
            SignedInAt = session.SignedInAt
        };
    }
}
