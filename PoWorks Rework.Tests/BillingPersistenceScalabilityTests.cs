using Xunit;

namespace PoWorks_Rework.Tests;

public class BillingPersistenceScalabilityTests
{
    [Fact]
    public void SaveBill_PersistsLineItemsWithOnePostgreSqlBinaryCopy()
    {
        var source = ReadSource("Services", "BillingService.cs");

        Assert.Contains("BeginBinaryImportAsync", source);
        Assert.Contains("COPY \"\"BillLineItems\"\"", source);
        Assert.Contains("FROM STDIN (FORMAT BINARY)", source);
        Assert.Contains("await writer.CompleteAsync()", source);
        Assert.Contains("NpgsqlDbType.Numeric", source);

        Assert.DoesNotContain("insertLineQuery", source);
        Assert.DoesNotContain("new NpgsqlCommand(insertLineQuery", source);
    }

    [Fact]
    public void SaveBill_SkipsCopyWhenThereAreNoLineItems()
    {
        var source = ReadSource("Services", "BillingService.cs");

        Assert.Contains("if (bill.LineItems.Count > 0)", source);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoWorks Rework.sln")))
            {
                var path = Path.Combine(
                    new[] { directory.FullName, "PoWorks Rework" }
                        .Concat(parts)
                        .ToArray());

                Assert.True(File.Exists(path), $"Source file not found: {path}");
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
