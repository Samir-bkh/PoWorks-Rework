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
        Assert.Equal(TimeSpan.FromMinutes(1),
            schedule.UntilNextDue(start.AddMinutes(2), TimeSpan.FromMinutes(5)));
        Assert.True(schedule.TryStart(1, "original", 3, start.AddMinutes(3)));
        Assert.True(schedule.TryStart(1, "replacement", 3, start.AddMinutes(3)));

        schedule.Forget(1);
        Assert.True(schedule.TryStart(1, "replacement", 3, start.AddMinutes(3)));
    }

    [Fact]
    public void FiveMinuteWorkspace_IsNotSampledOnOneMinuteGlobalChecks()
    {
        var schedule = new AutoImportSchedule();
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.TryStart(1, "five-minute", 5, start));
        for (var minute = 1; minute < 5; minute++)
            Assert.False(schedule.TryStart(1, "five-minute", 5, start.AddMinutes(minute)));
        Assert.True(schedule.TryStart(1, "five-minute", 5, start.AddMinutes(5)));
    }

    [Fact]
    public void OneMinuteGlobalChecks_WakeAtExactTwoMinuteDueTime()
    {
        var schedule = new AutoImportSchedule();
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, 500, TimeSpan.Zero);
        Assert.True(schedule.TryStart(1, "two-minute", 2, start));

        var check = start.AddMinutes(1).AddSeconds(-1);
        Assert.Equal(TimeSpan.FromMinutes(1),
            schedule.UntilNextDue(check, TimeSpan.FromMinutes(1)));
        check = check.AddMinutes(1);
        Assert.False(schedule.TryStart(1, "two-minute", 2, check));
        Assert.Equal(TimeSpan.FromSeconds(1),
            schedule.UntilNextDue(check, TimeSpan.FromMinutes(1)));
        Assert.True(schedule.TryStart(1, "two-minute", 2, check.AddSeconds(1)));
    }
}
