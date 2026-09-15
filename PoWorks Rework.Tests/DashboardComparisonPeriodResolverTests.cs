using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class DashboardComparisonPeriodResolverTests
{
    [Fact]
    public void TenDayPrimaryRange_ProducesTenDayComparison()
    {
        var result = DashboardComparisonPeriodResolver.Resolve(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 10, 23, 59, 59),
            new DateTime(2026, 3, 17));

        Assert.Equal(10, result.DurationDays);
        Assert.Equal(new DateTime(2026, 3, 17), result.StartDate);
        Assert.Equal(new DateTime(2026, 3, 26, 23, 59, 59, 999).AddTicks(9999), result.EndDate);
    }

    [Fact]
    public void SingleDayPrimaryRange_ProducesSingleFullDayComparison()
    {
        var result = DashboardComparisonPeriodResolver.Resolve(
            new DateTime(2026, 9, 15),
            new DateTime(2026, 9, 15, 23, 59, 59),
            new DateTime(2025, 9, 15));

        Assert.Equal(1, result.DurationDays);
        Assert.Equal(new DateTime(2025, 9, 15), result.StartDate);
        Assert.Equal(new DateTime(2025, 9, 15).AddDays(1).AddTicks(-1), result.EndDate);
    }

    [Fact]
    public void LeapYearComparison_PreservesDurationRatherThanCalendarGuessing()
    {
        var result = DashboardComparisonPeriodResolver.Resolve(
            new DateTime(2028, 2, 28),
            new DateTime(2028, 3, 1, 23, 59, 59),
            new DateTime(2027, 2, 28));

        Assert.Equal(3, result.DurationDays);
        Assert.Equal(new DateTime(2027, 3, 2).AddDays(1).AddTicks(-1), result.EndDate);
    }

    [Fact]
    public void InvalidPrimaryRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DashboardComparisonPeriodResolver.Resolve(
                new DateTime(2026, 9, 16),
                new DateTime(2026, 9, 15),
                new DateTime(2026, 8, 1)));
    }
}
