namespace PoWorks_Rework.Services;

/// <summary>
/// Ensures the application's shared polling cycle respects each workspace's
/// selected Web Service connection interval.
/// </summary>
public sealed class AutoImportSchedule
{
    private readonly Dictionary<int, (string ConnectionId, DateTimeOffset StartedAt)> _lastAttempt = new();

    public bool TryStart(int companyId, string connectionId, int intervalMinutes, DateTimeOffset now)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(intervalMinutes, 1, 1440));
        if (_lastAttempt.TryGetValue(companyId, out var previous) &&
            previous.ConnectionId == connectionId &&
            now - previous.StartedAt < interval)
            return false;

        _lastAttempt[companyId] = (connectionId, now);
        return true;
    }

    public void Forget(int companyId) => _lastAttempt.Remove(companyId);
}
