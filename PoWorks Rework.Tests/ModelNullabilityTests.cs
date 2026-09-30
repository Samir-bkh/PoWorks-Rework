using PoWorks_Rework.Models;
using Xunit;

namespace PoWorks_Rework.Tests;

public class ModelNullabilityTests
{
    [Fact]
    public void PaymentModels_HaveSafeEmptyDisplayDefaults()
    {
        var payment = new PaymentEntity();
        var invoice = new InvoiceLookupOption();

        Assert.Equal(string.Empty, payment.PaymentMethod);
        Assert.Equal(string.Empty, payment.Reference);
        Assert.Equal(string.Empty, payment.Notes);
        Assert.Equal(string.Empty, payment.RecordedBy);
        Assert.Equal(string.Empty, payment.TenantName);
        Assert.Equal(string.Empty, payment.BillStatus);
        Assert.Equal(string.Empty, invoice.BillNumber);
        Assert.Equal(string.Empty, invoice.TenantName);
    }

    [Fact]
    public void EmptySqlConnectionCollection_ModelsMissingLookupsAsNull()
    {
        var collection = new SqlServerConnectionCollection();

        Assert.Null(collection.GetDefaultConnection());
        Assert.Null(collection.GetConnection("missing"));
    }

    [Fact]
    public void MeterReadingFilter_DefaultsAreValidAndHaveNoValidationError()
    {
        var filter = new MeterReadingsFilter();

        Assert.True(filter.IsValid());
        Assert.Null(filter.GetValidationError());
    }

    [Fact]
    public void MeterReadingFilter_BlankViewTypeFailsClosedInsteadOfThrowing()
    {
        var filter = new MeterReadingsFilter { ViewType = string.Empty };

        Assert.False(filter.IsValid());
        Assert.Equal("View type is required", filter.GetValidationError());
    }
}
