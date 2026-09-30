using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AutoImportScheduleTests
{
    [Fact]
    public void FasterWorkspaceCycle_DoesNotImportSlowerWorkspaceEarly()
    {
        var schedule = new AutoImportSchedule();
        var start = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.TryStart(1, "pcvue-a", 2, start));
        Assert.True(schedule.TryStart(2, "pcvue-b", 1, start));

        Assert.False(schedule.TryStart(1, "pcvue-a", 2, start.AddMinutes(1)));
        Assert.True(schedule.TryStart(2, "pcvue-b", 1, start.AddMinutes(1)));
        Assert.False(schedule.TryStart(1, "pcvue-a", 2, start.AddMinutes(1).AddSeconds(59)));
        Assert.True(schedule.TryStart(1, "pcvue-a", 2, start.AddMinutes(2)));
    }

    [Fact]
    public void SettingAndSelectedConnectionChanges_UpdateTheSchedule()
    {
        var schedule = new AutoImportSchedule();
        var start = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.TryStart(1, "original", 2, start));
        Assert.False(schedule.TryStart(1, "original", 3, start.AddMinutes(2)));
        Assert.True(schedule.TryStart(1, "original", 3, start.AddMinutes(3)));
        Assert.True(schedule.TryStart(1, "replacement", 3, start.AddMinutes(3)));

        schedule.Forget(1);
        Assert.True(schedule.TryStart(1, "replacement", 3, start.AddMinutes(3)));
    }
}
