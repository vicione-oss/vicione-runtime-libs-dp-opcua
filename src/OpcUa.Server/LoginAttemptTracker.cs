using System;
using System.Collections.Concurrent;
using System.Linq;

namespace ViciOne.Suite.DataPort;

internal sealed class LoginAttemptTracker(TimeProvider? timeProvider = null)
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationSeconds = 1800;
    private const int MaxTrackedUsers = 10_000;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (int FailCount, DateTimeOffset LastAttempt)> _failedAttempts = new();

    internal bool IsLockedOut(string username)
    {
        if (!_failedAttempts.TryGetValue(username, out var record))
            return false;

        if (record.FailCount < MaxFailedAttempts)
            return false;

        if (_timeProvider.GetUtcNow() - record.LastAttempt > TimeSpan.FromSeconds(LockoutDurationSeconds))
        {
            _failedAttempts.TryRemove(username, out _);
            return false;
        }

        return true;
    }

    internal void RecordFailure(string username)
    {
        var now = _timeProvider.GetUtcNow();

        _failedAttempts.AddOrUpdate(
            username,
            _ => (1, now),
            (_, existing) => (existing.FailCount + 1, now));

        if (_failedAttempts.Count > MaxTrackedUsers)
            Evict(now);
    }

    internal void ResetFailures(string username)
        => _failedAttempts.TryRemove(username, out _);

    internal DateTimeOffset? GetLockoutEnd(string username)
    {
        if (!_failedAttempts.TryGetValue(username, out var record))
            return null;

        if (record.FailCount < MaxFailedAttempts)
            return null;

        return record.LastAttempt + TimeSpan.FromSeconds(LockoutDurationSeconds);
    }

    internal int GetFailCount(string username)
    {
        if (_failedAttempts.TryGetValue(username, out var record))
            return record.FailCount;

        return 0;
    }

    internal TimeSpan GetRemainingDelay(string username)
    {
        if (!_failedAttempts.TryGetValue(username, out var record))
            return TimeSpan.Zero;

        var delayMs = Math.Min(Math.Pow(2, record.FailCount) * 100, 30_000);
        var required = TimeSpan.FromMilliseconds(delayMs);
        var elapsed = _timeProvider.GetUtcNow() - record.LastAttempt;
        var remaining = required - elapsed;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void Evict(DateTimeOffset now)
    {
        var lockoutExpiry = TimeSpan.FromSeconds(LockoutDurationSeconds);

        // First pass: remove expired entries (lockout elapsed)
        foreach (var kvp in _failedAttempts)
        {
            if (now - kvp.Value.LastAttempt > lockoutExpiry)
                _failedAttempts.TryRemove(kvp.Key, out _);
        }

        // If still over the limit, evict the oldest entries
        if (_failedAttempts.Count <= MaxTrackedUsers)
            return;

        var excess = _failedAttempts.Count - MaxTrackedUsers;
        var oldest = _failedAttempts
            .OrderBy(kvp => kvp.Value.LastAttempt)
            .Take(excess)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
            _failedAttempts.TryRemove(key, out _);
    }
}
