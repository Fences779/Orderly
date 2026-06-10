namespace Orderly.Data.Services;

public sealed class CredentialAttemptTracker
{
    private const int MaxFailuresBeforeCooldown = 5;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    public bool IsBlocked(string purpose, string identifier)
    {
        var key = BuildKey(purpose, identifier);
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
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
        }
    }

    public void ClearFailures(string purpose, string identifier)
    {
        var key = BuildKey(purpose, identifier);

        lock (_syncRoot)
        {
            _attempts.Remove(key);
        }
    }

    private static string BuildKey(string purpose, string identifier)
    {
        return purpose.Trim().ToLowerInvariant() + ":" + identifier.Trim().ToLowerInvariant();
    }

    private sealed class AttemptState
    {
        public int FailedCount { get; set; }

        public DateTimeOffset LockedUntil { get; set; }
    }
}
