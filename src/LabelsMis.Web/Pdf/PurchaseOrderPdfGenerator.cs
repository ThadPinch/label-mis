using LabelsMis.Domain.Entities;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LabelsMis.Web.Pdf;

public class PurchaseOrderOptions
{
    public const string SectionName = "PurchaseOrders";
    public string PdfStoragePath { get; set; } = "./data/pdfs/purchase-orders";
    public string TermsText { get; set; } = "Please confirm receipt of this purchase order and notify us of any pricing or lead-time discrepancies.";
    public string ShopName { get; set; } = "Labels MIS Print Shop";
}

public class PurchaseOrderPdfGenerator(IOptions<PurchaseOrderOptions> options, GeneralSettingsService generalSettings)
{
    private const string Accent = "#005d66";
    private const string AccentSoft = "#e6eff0";

    static PurchaseOrderPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Generates the PO PDF, writes it to disk, and returns the path (used for email attachments).</summary>
    public async Task<string> GenerateAsync(PurchaseOrder po, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var document = await BuildDocumentAsync(po, cancellationToken);
        var fileName = $"{po.PoNumber.Replace('/', '-')}.pdf";
        var fullPath = Path.Combine(settings.PdfStoragePath, fileName);
        await Task.Run(() => document.GeneratePdf(fullPath), cancellationToken);
        return fullPath;
    }

    /// <summary>Renders the PO PDF to a byte array without persisting (used for the in-browser view).</summary>
    public async Task<byte[]> GenerateBytesAsync(PurchaseOrder po, CancellationToken cancellationToken = default)
    {
        var document = await BuildDocumentAsync(po, cancellationToken);
        return await Task.Run(() => document.GeneratePdf(), cancellationToken);
    }

    private async Task<IDocument> BuildDocumentAsync(PurchaseOrder po, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var branding = await generalSettings.GetAsync(cancellationToken);
        var companyName = !string.IsNullOrWhiteSpace(branding?.CompanyName) ? branding!.CompanyName : settings.ShopName;
        var termsText = !string.IsNullOrWhiteSpace(branding?.TermsText) ? branding!.TermsText! : settings.TermsText;
        var logo = branding is { HasLogo: true } ? branding.LogoBytes : null;
        var contactLines = new[] { branding?.AddressLine1, branding?.AddressLine2, branding?.CityStateZip, branding?.Phone, branding?.Email, branding?.Website }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!)
            .ToList();

        var lines = po.Lines.OrderBy(l => l.LineNumber).ToList();
        var orderTotal = lines.Sum(l => l.LineTotal);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.Letter);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            if (logo is not null)
                            {
                                left.Item().Height(56).AlignLeft().Image(logo).FitHeight();
                                if (!string.IsNullOrWhiteSpace(companyName))
                                {
                                    left.Item().PaddingTop(6).Text(companyName).SemiBold().FontSize(12);
                                }
                            }
                            else
                            {
                                left.Item().Text(companyName).Bold().FontSize(20).FontColor(Accent);
                            }

                            foreach (var contactLine in contactLines)
                            {
                                left.Item().Text(contactLine).FontSize(9).FontColor(Colors.Grey.Darken1);
                            }
                        });

                        row.ConstantItem(200).Column(right =>
                        {
                            right.Item().AlignRight().Text("PURCHASE ORDER").Bold().FontSize(20).FontColor(Accent);
                            right.Item().AlignRight().Text(po.PoNumber).SemiBold().FontSize(11);
                            right.Item().AlignRight().Text($"Date: {po.OrderedAt:MMM d, yyyy}").FontSize(9);
                            if (po.ExpectedAt.HasValue)
                            {
                                right.Item().AlignRight().Text($"Expected: {po.ExpectedAt:MMM d, yyyy}").FontSize(9);
                            }
                            right.Item().AlignRight().Text($"Status: {po.Status}").FontSize(9);
                        });
                    });

                    header.Item().PaddingTop(10).LineHorizontal(2).LineColor(Accent);
                });

                page.Content().PaddingVertical(18).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(vendor =>
                        {
                            vendor.Item().Text("Vendor").Bold().FontColor(Accent);
                            vendor.Item().Text(po.Supplier.Name).SemiBold();
                            if (!string.IsNullOrWhiteSpace(po.Supplier.Code)) vendor.Item().Text($"Code: {po.Supplier.Code}");
                            if (!string.IsNullOrWhiteSpace(po.Supplier.AccountNumber)) vendor.Item().Text($"Account #: {po.Supplier.AccountNumber}");
                            if (!string.IsNullOrWhiteSpace(po.Supplier.Terms)) vendor.Item().Text($"Terms: {po.Supplier.Terms}");
                        });

                        row.RelativeItem().Column(ship =>
                        {
                            ship.Item().Text("Ship to").Bold().FontColor(Accent);
                            ship.Item().Text(companyName).SemiBold();
                            foreach (var contactLine in contactLines.Take(3))
                            {
                                ship.Item().Text(contactLine).FontSize(9);
                            }
                        });
                    });

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Header(headerRow =>
                        {
                            headerRow.Cell().Element(HeaderCell).Text("#");
                            headerRow.Cell().Element(HeaderCell).Text("Stock");
                            headerRow.Cell().Element(HeaderCell).AlignRight().Text("Qty (LF)");
                            headerRow.Cell().Element(HeaderCell).AlignRight().Text("Unit cost");
                            headerRow.Cell().Element(HeaderCell).AlignRight().Text("Total");
                        });

                        foreach (var line in lines)
                        {
                            table.Cell().Element(BodyCell).Text(line.LineNumber.ToString());
                            table.Cell().Element(BodyCell).Text($"{line.Stock.Code} — {line.Stock.Description}");
                            table.Cell().Element(BodyCell).AlignRight().Text(line.QuantityLf.ToString("N2"));
                            table.Cell().Element(BodyCell).AlignRight().Text(line.UnitCost.ToString("C4"));
                            table.Cell().Element(BodyCell).AlignRight().Text(line.LineTotal.ToString("C2")).SemiBold();
                        }
                    });

                    col.Item().PaddingTop(8).AlignRight().Text($"Order total: {orderTotal.ToString("C2")}")
                        .Bold().FontSize(12).FontColor(Accent);

                    if (!string.IsNullOrWhiteSpace(po.Notes))
                    {
                        col.Item().PaddingTop(12).Text("Notes").Bold().FontColor(Accent);
                        col.Item().Text(po.Notes);
                    }

                    col.Item().PaddingTop(18).Background(AccentSoft).Padding(10).Column(terms =>
                    {
                        terms.Item().Text("Terms").Bold().FontColor(Accent);
                        terms.Item().Text(termsText).FontSize(9);
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
                    text.Span(companyName + "   •   Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Accent).PaddingVertical(5).PaddingHorizontal(6)
            .DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold().FontSize(9));

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(6);
}
