using LabelsMis.Web.Services.Estimates;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LabelsMis.Web.Pdf;

public class EstimatePdfGenerator(IOptions<EstimateOptions> options)
{
    static EstimatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> GenerateAsync(EstimateDetail detail, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var fileName = $"{detail.Estimate.EstimateNumber.Replace('/', '-')}_rev{detail.Estimate.RevisionNumber}.pdf";
        var fullPath = Path.Combine(settings.PdfStoragePath, fileName);

        var billingAddress = detail.Customer.Addresses
            .FirstOrDefault(a => a.IsDefault)
            ?? detail.Customer.Addresses.FirstOrDefault();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.Letter);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(settings.ShopName).Bold().FontSize(18);
                    col.Item().Text($"Estimate {detail.Estimate.EstimateNumber} (Rev {detail.Estimate.RevisionNumber})")
                        .SemiBold().FontSize(14);
                    col.Item().Text($"Date: {detail.Estimate.CreatedAt:yyyy-MM-dd}");
                    if (detail.Estimate.ValidUntilDate.HasValue)
                    {
                        col.Item().Text($"Valid until: {detail.Estimate.ValidUntilDate:yyyy-MM-dd}");
                    }
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text("Customer").Bold();
                    col.Item().Text(detail.Customer.Name);
                    if (billingAddress is not null)
                    {
                        col.Item().Text(billingAddress.Street1);
                        if (!string.IsNullOrWhiteSpace(billingAddress.Street2))
                        {
                            col.Item().Text(billingAddress.Street2!);
                        }

                        col.Item().Text($"{billingAddress.City}, {billingAddress.State} {billingAddress.Zip}");
                    }

                    foreach (var line in detail.Estimate.Lines.OrderBy(l => l.LineNumber))
                    {
                        col.Item().PaddingTop(14).Text($"Line {line.LineNumber}: {line.ProductDescription}").Bold();
                        col.Item().Text(
                            $"Size: {line.LabelAcrossIn}\" x {line.LabelAroundIn}\"  " +
                            $"Substrate: {line.Substrate.Description}  Ink: {line.InkSet}");

                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Quantity");
                                header.Cell().Element(CellStyle).Text("Unit price");
                                header.Cell().Element(CellStyle).Text("Total");
                                header.Cell().Element(CellStyle).Text("Margin %");
                            });

                            foreach (var breakRow in line.QuantityBreaks.OrderBy(q => q.Quantity))
                            {
                                table.Cell().Element(CellStyle).Text(breakRow.Quantity.ToString("N0"));
                                table.Cell().Element(CellStyle).Text(breakRow.UnitPrice.ToString("C4"));
                                table.Cell().Element(CellStyle).Text(breakRow.TotalPrice.ToString("C2"));
                                table.Cell().Element(CellStyle).Text((breakRow.MarginPct * 100m).ToString("F1") + "%");
                            }
                        });

                        if (!string.IsNullOrWhiteSpace(line.LineNotes))
                        {
                            col.Item().Text($"Notes: {line.LineNotes}");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(detail.Estimate.Notes))
                    {
                        col.Item().PaddingTop(10).Text("Notes").Bold();
                        col.Item().Text(detail.Estimate.Notes);
                    }

                    col.Item().PaddingTop(20).Text("Terms").Bold();
                    col.Item().Text(settings.TermsText);

                    col.Item().PaddingTop(40).Text("Sales representative: ________________________________");
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

        await Task.Run(() => document.GeneratePdf(fullPath), cancellationToken);
        return fullPath;
    }

    private static IContainer CellStyle(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);
}
