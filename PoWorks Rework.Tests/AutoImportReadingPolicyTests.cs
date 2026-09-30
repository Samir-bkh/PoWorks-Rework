using PoWorks_Rework.Models;
using PoWorks_Rework.Services;
using Xunit;

namespace PoWorks_Rework.Tests;

public class AutoImportReadingPolicyTests
{
    private static readonly DateTime BaselineUtc = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

    private static TrendDataPoint Point(int minute, double value, string quality = "Good") => new()
    {
        Timestamp = BaselineUtc.AddMinutes(minute).ToString("O"),
        Value = value,
        Quality = quality
    };

    [Fact]
    public void FailedHttpResult_NeverTurnsCachedSamplesIntoNewReadings()
    {
        var failed = new VariableTrendResult
        {
            VariableName = "BacNet01.Light1",
            Success = false,
            ErrorMessage = "500 E_Fail",
            TrendData = new() { Point(1, 123) }
        };
        Assert.Empty(AutoImportReadingPolicy.SelectNewRealReadings(failed, BaselineUtc.ToLocalTime()));
        Assert.Empty(AutoImportReadingPolicy.SelectNewRealReadings(null, BaselineUtc.ToLocalTime()));
    }

    [Fact]
    public void EmptySuccessfulQuery_DoesNotCreatePadding()
    {
        var empty = new VariableTrendResult { VariableName = "Building.Light", Success = true };
        Assert.Empty(AutoImportReadingPolicy.SelectNewRealReadings(empty, BaselineUtc.ToLocalTime()));
    }

    [Fact]
    public void IncompleteHistory_MustNotBePersistedAsComplete()
    {
        var truncated = new VariableTrendResult
        {
            Success = true,
            MaxNumberExceeded = true,
            TrendData = new() { Point(1, 10) }
        };
        Assert.Empty(AutoImportReadingPolicy.SelectNewRealReadings(truncated, BaselineUtc.ToLocalTime()));
    }

    [Fact]
    public void SelectsOnlyRealNewGoodFiniteSamples_OrderedAndDeduplicated()
    {
        var result = new VariableTrendResult
        {
            Success = true,
            TrendData = new()
            {
                Point(2, 20),
                Point(0, 10),
                Point(3, 99, "Bad"),
                Point(1, double.NaN),
                Point(2, 21),
                Point(1, 11),
                Point(4, double.PositiveInfinity)
            }
        };

        var readings = AutoImportReadingPolicy.SelectNewRealReadings(result, BaselineUtc.ToLocalTime());
        Assert.Equal(2, readings.Count);
        Assert.Equal(BaselineUtc.AddMinutes(1).ToLocalTime(), readings[0].Timestamp);
        Assert.Equal(11m, readings[0].Value);
        Assert.Equal(BaselineUtc.AddMinutes(2).ToLocalTime(), readings[1].Timestamp);
        Assert.Equal(21m, readings[1].Value);
    }

    [Fact]
    public void OldRealReadings_AreNotReimported()
    {
        var result = new VariableTrendResult { Success = true, TrendData = new() { Point(-2, 10), Point(0, 12) } };
        Assert.Empty(AutoImportReadingPolicy.SelectNewRealReadings(result, BaselineUtc.ToLocalTime()));
    }

    [Fact]
    public void ProductionWorker_MustNeverWriteSyntheticCurrentTimeReadings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PoWorks Rework.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "PoWorks Rework", "Services", "AutoImportWorker.cs"));
        Assert.Contains("AutoImportReadingPolicy.SelectNewRealReadings", source);
        Assert.DoesNotContain("paddingAdded++", source);
        Assert.DoesNotContain("latestValue = lastReadings[meter.MeterId].Value", source);
        Assert.DoesNotContain("WriteAsync(endTime, NpgsqlDbType.Timestamp", source);
    }
}
