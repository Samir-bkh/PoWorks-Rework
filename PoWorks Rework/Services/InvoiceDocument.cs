using PoWorks_Rework.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// QuestPDF document that renders a formatted invoice for a bill entity.
    /// Includes header, line items table, totals, and footer with page numbers.
    /// </summary>
    public class InvoiceDocument : IDocument
    {
        private readonly BillEntity _bill;

        /// <summary>
        /// Initializes the invoice document with the bill data to render.
        /// </summary>
        /// <param name="bill">The bill entity containing the invoice data.</param>
        public InvoiceDocument(BillEntity bill)
        {
            _bill = bill;
        }

        /// <summary>
        /// Composes the invoice document layout with header, content, and footer sections.
        /// </summary>
        /// <param name="container">The document container to compose into.</param>
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

        /// <summary>
        /// Composes the invoice header with the invoice number, dates, and company information.
        /// </summary>
        /// <param name="container">The container to compose the header into.</param>
        private void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text("INVOICE").FontSize(28).SemiBold().FontColor(Colors.Blue.Darken2);
                    column.Item().Text($"Invoice Number: {_bill.BillNumber}").FontSize(12).SemiBold();
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

        /// <summary>
        /// Composes the invoice content including the billed-to section, line items table, and totals.
        /// </summary>
        /// <param name="container">The container to compose the content into.</param>
        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(20).Column(column =>
            {
                column.Item().PaddingBottom(20).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Billed To:").SemiBold().FontColor(Colors.Grey.Darken2);
                        col.Item().Text(_bill.TenantName ?? "Unknown Tenant").FontSize(14).SemiBold();
                    });
                });
                column.Item().Element(ComposeTable);
                column.Item().PaddingTop(25).Element(ComposeTotals);
            });
        }

        /// <summary>
        /// Composes the line items table with meter descriptions, consumption, unit prices, and line totals.
        /// </summary>
        /// <param name="container">The container to compose the table into.</param>
        private void ComposeTable(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3); 
                    columns.RelativeColumn(1); 
                    columns.RelativeColumn(1); 
                    columns.RelativeColumn(1); 
                });
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

        /// <summary>
        /// Composes the totals section with subtotal, tax, and grand total amounts.
        /// </summary>
        /// <param name="container">The container to compose the totals into.</param>
        private void ComposeTotals(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem(); 
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
                    table.Cell().BorderTop(1).BorderColor(Colors.Black).PaddingTop(5).AlignRight().Text($"RM {_bill.AmountInclTax:N2}").SemiBold().FontSize(14);
                });
            });
        }

        /// <summary>
        /// Composes the invoice footer with page numbers and generation attribution.
        /// </summary>
        /// <param name="container">The container to compose the footer into.</param>
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