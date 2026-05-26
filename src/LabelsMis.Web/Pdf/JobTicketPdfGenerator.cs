using LabelsMis.Web.Services.Jobs;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LabelsMis.Web.Pdf;

public class JobOptions
{
    public const string SectionName = "Jobs";
    public string PdfStoragePath { get; set; } = "./data/pdfs/jobs";
    public string ShopName { get; set; } = "Labels MIS Print Shop";
}

public class JobTicketPdfGenerator(IOptions<JobOptions> options)
{
    static JobTicketPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GenerateAsync(JobTicketDetail detail, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.Letter);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text(settings.ShopName).Bold().FontSize(16);
                    col.Item().Text("Production job ticket").SemiBold().FontSize(14);

                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text($"Job: {detail.Job.JobNumber}").Bold().FontSize(18);
                            left.Item().Text($"Customer: {detail.CustomerName}");
                            left.Item().Text($"Product: {detail.ProductDescription}");
                            left.Item().Text(
                                $"Size: {detail.LabelAcrossIn}\" x {detail.LabelAroundIn}\"  " +
                                $"Substrate: {detail.SubstrateDescription}  Ink: {detail.InkSet}");
                            left.Item().Text($"Qty: {detail.Job.QuantityPlanned:N0} (ordered {detail.Job.QuantityOrdered:N0})");
                            left.Item().Text($"Due: {detail.Job.DueDate?.ToString("yyyy-MM-dd") ?? "—"}");
                        });

                        row.ConstantItem(180).Column(barcodeCol =>
                        {
                            barcodeCol.Item().Border(1).BorderColor(Colors.Black).Padding(8).AlignCenter()
                                .Text(detail.Job.JobNumber).Bold().FontSize(14).FontFamily("Courier New");
                            barcodeCol.Item().AlignCenter().Text("Scan job number").FontSize(8);
                        });
                    });

                    col.Item().PaddingTop(12).Text("Route").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40);
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("#");
                            header.Cell().Element(CellStyle).Text("Operation");
                        });

                        foreach (var step in detail.Route)
                        {
                            table.Cell().Element(CellStyle).Text(step.Sequence.ToString());
                            table.Cell().Element(CellStyle).Text(step.Description);
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(detail.Job.Notes))
                    {
                        col.Item().PaddingTop(10).Text("Notes").Bold();
                        col.Item().Text(detail.Job.Notes);
                    }
                });
            });
        });

        return await Task.Run(() => document.GeneratePdf(), cancellationToken);
    }

    private static IContainer CellStyle(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);
}
