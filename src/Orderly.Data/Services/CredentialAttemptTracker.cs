using Orderly.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class CredentialAttemptTracker
{
    private const int MaxFailuresBeforeCooldown = 5;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    private const string StateFileName = "credential-attempts.json";

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);
    private readonly string _stateFilePath;

    public CredentialAttemptTracker()
        : this(Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), StateFileName))
    {
    }

    internal CredentialAttemptTracker(string stateFilePath)
    {
        if (string.IsNullOrWhiteSpace(stateFilePath))
        {
            throw new ArgumentException("Credential attempt state path cannot be empty.", nameof(stateFilePath));
        }

        _stateFilePath = Path.GetFullPath(stateFilePath);
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "凭据尝试状态目录");
        }

        LocalDataFileSecurity.EnsureFileIsNotLinked(_stateFilePath, "凭据尝试状态文件");
        LoadAttempts();
    }

    public bool IsBlocked(string purpose, string identifier)
    {
        var key = BuildKey(purpose, identifier);
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            LoadAttempts();
            if (!_attempts.TryGetValue(key, out var state))
            {
                return false;
            }

            if (state.LockedUntil > now)
            {
                return true;
            }

            if (state.LockedUntil != default)
            {
                _attempts.Remove(key);
                SaveAttempts();
            }

            return false;
        }
    }

    public void RecordResult(string purpose, string identifier, bool success)
    {
        if (success)
        {
            ClearFailures(purpose, identifier);
            return;
        }

        RecordFailure(purpose, identifier);
    }

    public void RecordFailure(string purpose, string identifier)
    {
        var key = BuildKey(purpose, identifier);
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            LoadAttempts();
            if (_attempts.TryGetValue(key, out var state) && state.LockedUntil > now)
            {
                return;
            }

            if (state is null || state.LockedUntil != default)
            {
                state = new AttemptState();
                _attempts[key] = state;
            }

            state.FailedCount++;
            if (state.FailedCount >= MaxFailuresBeforeCooldown)
            {
                state.FailedCount = 0;
                state.LockedUntil = now.Add(Cooldown);
            }

            SaveAttempts();
        }
    }

    public void ClearFailures(string purpose, string identifier)
    {
        var key = BuildKey(purpose, identifier);

        lock (_syncRoot)
        {
            LoadAttempts();
            if (_attempts.Remove(key))
            {
                SaveAttempts();
            }
        }
    }

    private static string BuildKey(string purpose, string identifier)
    {
        var rawKey = purpose.Trim().ToLowerInvariant() + ":" + identifier.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void LoadAttempts()
    {
        _attempts.Clear();

        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        try
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(_stateFilePath, "凭据尝试状态文件");
            var json = File.ReadAllText(_stateFilePath);
            var persisted = JsonSerializer.Deserialize<Dictionary<string, AttemptState>>(json);
            if (persisted is null)
            {
                return;
            }

            foreach (var (key, state) in persisted)
            {
                if (IsValidPersistedKey(key) && state.FailedCount >= 0)
                {
                    _attempts[key] = state;
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidOperationException("无法读取凭据尝试状态，已拒绝认证。", ex);
        }
    }

    private void SaveAttempts()
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "凭据尝试状态目录");
        }

        var tempPath = _stateFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(_attempts);
            File.WriteAllText(tempPath, json);
            LocalDataFileSecurity.HardenFile(tempPath);
            File.Move(tempPath, _stateFilePath, overwrite: true);
            LocalDataFileSecurity.HardenFile(_stateFilePath);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidOperationException("无法保存凭据尝试状态，已拒绝认证。", ex);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool IsValidPersistedKey(string key)
    {
        return key.Length == 64 && key.All(static ch => char.IsAsciiHexDigit(ch));
    }

    private sealed class AttemptState
    {
        public int FailedCount { get; set; }

        public DateTimeOffset LockedUntil { get; set; }
    }
}
