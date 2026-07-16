using PoWorks_Rework.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PoWorks_Rework.Services
{
    public class InvoiceDocument : IDocument
    {
        private readonly BillEntity _bill;

        public InvoiceDocument(BillEntity bill)
        {
            _bill = bill;
        }

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Margin(50);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text("INVOICE").FontSize(28).SemiBold().FontColor(Colors.Blue.Darken2);
                    column.Item().Text($"Invoice Number: {_bill.BillNumber}").FontSize(12).SemiBold();
                    // CORRECTION ICI : GeneratedAt
                    column.Item().Text($"Issue Date: {_bill.GeneratedAt.ToString("dd MMM yyyy")}");
                    column.Item().Text($"Billing Period: {_bill.PeriodStart.ToString("dd MMM yyyy")} to {_bill.PeriodEnd.ToString("dd MMM yyyy")}");
                });

                row.ConstantItem(250).AlignRight().Column(column =>
                {
                    column.Item().Text("PoWorks Energy Management").FontSize(14).SemiBold();
                    column.Item().Text("123 Business Avenue");
                    column.Item().Text("Petaling Jaya, Selangor, Malaysia");
                    column.Item().Text("Email: billing@poworks.com");
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(20).Column(column =>
            {
                // Billed To Section
                column.Item().PaddingBottom(20).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Billed To:").SemiBold().FontColor(Colors.Grey.Darken2);
                        col.Item().Text(_bill.TenantName ?? "Unknown Tenant").FontSize(14).SemiBold();
                    });
                });

                // Items Table
                column.Item().Element(ComposeTable);

                // Totals Section
                column.Item().PaddingTop(25).Element(ComposeTotals);
            });
        }

        private void ComposeTable(IContainer container)
        {
            container.Table(table =>
            {
                // Define columns
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3); // Meter Name
                    columns.RelativeColumn(1); // Consumption
                    columns.RelativeColumn(1); // Unit Price
                    columns.RelativeColumn(1); // Total
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("Meter / Description");
                    header.Cell().Element(CellStyle).AlignRight().Text("Consumption");
                    header.Cell().Element(CellStyle).AlignRight().Text("Unit Price");
                    header.Cell().Element(CellStyle).AlignRight().Text("Line Total");

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
                    }
                });

                // Rows
                foreach (var item in _bill.LineItems)
                {
                    table.Cell().Element(CellStyle).Text(item.MeterName);
                    table.Cell().Element(CellStyle).AlignRight().Text($"{item.Consumption:N2} {item.Unit}");
                    table.Cell().Element(CellStyle).AlignRight().Text($"RM {item.UnitPrice:N4}");
                    table.Cell().Element(CellStyle).AlignRight().Text($"RM {item.LineTotalExclTax:N2}");

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
                    }
                }
            });
        }

        private void ComposeTotals(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem(); // Empty space to push totals to the right
                row.ConstantItem(250).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().PaddingBottom(5).Text("Subtotal (Excl. Tax):");
                    table.Cell().PaddingBottom(5).AlignRight().Text($"RM {_bill.AmountExclTax:N2}");

                    table.Cell().PaddingBottom(5).Text("SST (8%):");
                    table.Cell().PaddingBottom(5).AlignRight().Text($"RM {_bill.TaxAmount:N2}");

                    table.Cell().BorderTop(1).BorderColor(Colors.Black).PaddingTop(5).Text("Grand Total:").SemiBold().FontSize(14);
                    // CORRECTION ICI : AmountInclTax
                    table.Cell().BorderTop(1).BorderColor(Colors.Black).PaddingTop(5).AlignRight().Text($"RM {_bill.AmountInclTax:N2}").SemiBold().FontSize(14);
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(x =>
            {
                x.Span("Page ");
                x.CurrentPageNumber();
                x.Span(" of ");
                x.TotalPages();
                x.Span(" | Generated by PoWorks ERP");
            });
        }
    }
}