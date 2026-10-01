namespace PoWorks_Rework.Services;

/// <summary>
/// Ensures the application's shared polling cycle respects each workspace's
/// selected Web Service connection interval.
/// </summary>
public sealed class AutoImportSchedule
{
    private readonly Dictionary<int, (string ConnectionId, DateTimeOffset StartedAt, int IntervalMinutes)> _lastAttempt = new();

    public bool TryStart(int companyId, string connectionId, int intervalMinutes, DateTimeOffset now)
    {
        var clampedMinutes = Math.Clamp(intervalMinutes, 1, 1440);
        var interval = TimeSpan.FromMinutes(clampedMinutes);
        if (_lastAttempt.TryGetValue(companyId, out var previous) &&
            previous.ConnectionId == connectionId &&
            now - previous.StartedAt < interval)
        {
            _lastAttempt[companyId] = (connectionId, previous.StartedAt, clampedMinutes);
            return false;
        }

        _lastAttempt[companyId] = (connectionId, now, clampedMinutes);
        return true;
    }

    public TimeSpan UntilNextDue(DateTimeOffset now, TimeSpan maximumDelay)
    {
        var delay = maximumDelay;
        foreach (var attempt in _lastAttempt.Values)
        {
            var remaining = attempt.StartedAt.AddMinutes(attempt.IntervalMinutes) - now;
            if (remaining < delay) delay = remaining;
        }

        // A manual import may have blocked this cycle. Never busy-loop on an
        // already overdue company while the shared import lock is still held.
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    public void Forget(int companyId) => _lastAttempt.Remove(companyId);
}
