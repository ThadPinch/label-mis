using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LabelsMis.Web.Pdf;

public class EstimatePdfGenerator(IOptions<EstimateOptions> options, GeneralSettingsService generalSettings)
{
    private const string Accent = "#005d66";
    private const string AccentSoft = "#e6eff0";

    static EstimatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Generates the PDF for a saved estimate, writes it to disk, and returns the path.</summary>
    public async Task<string> GenerateAsync(EstimateDetail detail, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var model = MapDetail(detail);
        var document = await BuildDocumentAsync(model, cancellationToken);

        var fileName = $"{model.EstimateNumber.Replace('/', '-')}_rev{model.RevisionNumber}.pdf";
        var fullPath = Path.Combine(settings.PdfStoragePath, fileName);
        await Task.Run(() => document.GeneratePdf(fullPath), cancellationToken);
        return fullPath;
    }

    /// <summary>Renders the PDF to a byte array without persisting (used for the in-browser preview).</summary>
    public async Task<byte[]> GenerateBytesAsync(EstimatePdfModel model, CancellationToken cancellationToken = default)
    {
        var document = await BuildDocumentAsync(model, cancellationToken);
        return await Task.Run(() => document.GeneratePdf(), cancellationToken);
    }

    /// <summary>Renders a saved estimate to a byte array (inline view of the official PDF).</summary>
    public Task<byte[]> GenerateBytesAsync(EstimateDetail detail, CancellationToken cancellationToken = default) =>
        GenerateBytesAsync(MapDetail(detail), cancellationToken);

    private static EstimatePdfModel MapDetail(EstimateDetail detail)
    {
        var billing = detail.Customer.Addresses.FirstOrDefault(a => a.IsDefault)
            ?? detail.Customer.Addresses.FirstOrDefault();

        var shipTo = detail.Estimate.ShippingAddress;

        return new EstimatePdfModel(
            detail.Estimate.EstimateNumber,
            detail.Estimate.RevisionNumber,
            detail.Estimate.CreatedAt,
            detail.Estimate.ValidUntilDate,
            detail.Customer.Name,
            billing is null
                ? null
                : new EstimatePdfAddress(null, billing.Street1, billing.Street2, billing.City, billing.State, billing.Zip),
            shipTo.HasAddress
                ? new EstimatePdfAddress(shipTo.RecipientName, shipTo.Street1, shipTo.Street2, shipTo.City, shipTo.State, shipTo.Zip)
                : null,
            detail.Estimate.Lines.OrderBy(l => l.LineNumber).Select(l => new EstimatePdfLine(
                l.LineNumber,
                l.ProductDescription,
                l.LabelAcrossIn,
                l.LabelAroundIn,
                l.Substrate.Description,
                l.InkSet.ToString(),
                l.LineNotes,
                l.QuantityBreaks.OrderBy(q => q.Quantity)
                    .Select(q => new EstimatePdfBreak(q.Quantity, q.UnitPrice, q.TotalPrice)).ToList())).ToList(),
            detail.Estimate.ShippingMethod?.Name,
            detail.Estimate.ShippingCost,
            detail.Estimate.ShippingMethodId is not null || detail.Estimate.ShippingCost > 0,
            detail.Estimate.Notes,
            detail.SalesRepName);
    }

    private async Task<IDocument> BuildDocumentAsync(EstimatePdfModel model, CancellationToken cancellationToken)
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
                            right.Item().AlignRight().Text("ESTIMATE").Bold().FontSize(22).FontColor(Accent);
                            right.Item().AlignRight().Text($"{model.EstimateNumber} · Rev {model.RevisionNumber}")
                                .SemiBold().FontSize(11);
                            right.Item().AlignRight().Text($"Date: {model.CreatedAt:MMM d, yyyy}").FontSize(9);
                            if (model.ValidUntilDate.HasValue)
                            {
                                right.Item().AlignRight().Text($"Valid until: {model.ValidUntilDate:MMM d, yyyy}").FontSize(9);
                            }
                        });
                    });

                    header.Item().PaddingTop(10).LineHorizontal(2).LineColor(Accent);
                });

                page.Content().PaddingVertical(18).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(bill =>
                        {
                            bill.Item().Text("Bill to").Bold().FontColor(Accent);
                            bill.Item().Text(model.CustomerName).SemiBold();
                            if (model.BillingAddress is { } billing)
                            {
                                if (!string.IsNullOrWhiteSpace(billing.Street1)) bill.Item().Text(billing.Street1!);
                                if (!string.IsNullOrWhiteSpace(billing.Street2)) bill.Item().Text(billing.Street2!);
                                bill.Item().Text($"{billing.City}, {billing.State} {billing.Zip}");
                            }
                        });

                        if (model.ShipToAddress is { } shipTo)
                        {
                            row.RelativeItem().Column(ship =>
                            {
                                ship.Item().Text("Ship to").Bold().FontColor(Accent);
                                if (!string.IsNullOrWhiteSpace(shipTo.RecipientName)) ship.Item().Text(shipTo.RecipientName!).SemiBold();
                                if (!string.IsNullOrWhiteSpace(shipTo.Street1)) ship.Item().Text(shipTo.Street1!);
                                if (!string.IsNullOrWhiteSpace(shipTo.Street2)) ship.Item().Text(shipTo.Street2!);
                                ship.Item().Text($"{shipTo.City}, {shipTo.State} {shipTo.Zip}");
                            });
                        }
                    });

                    foreach (var line in model.Lines)
                    {
                        col.Item().PaddingTop(14).Text($"Line {line.LineNumber}: {line.ProductDescription}")
                            .Bold().FontSize(11);
                        col.Item().Text(
                                $"Size: {line.LabelAcrossIn}\" × {line.LabelAroundIn}\"   •   " +
                                $"Substrate: {line.SubstrateDescription}   •   Ink: {line.InkSet}")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);

                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(headerRow =>
                            {
                                headerRow.Cell().Element(HeaderCell).Text("Quantity");
                                headerRow.Cell().Element(HeaderCell).AlignRight().Text("Unit price");
                                headerRow.Cell().Element(HeaderCell).AlignRight().Text("Total");
                            });

                            foreach (var breakRow in line.Breaks)
                            {
                                table.Cell().Element(BodyCell).Text(breakRow.Quantity.ToString("N0"));
                                table.Cell().Element(BodyCell).AlignRight().Text(breakRow.UnitPrice.ToString("C4"));
                                table.Cell().Element(BodyCell).AlignRight().Text(breakRow.TotalPrice.ToString("C2")).SemiBold();
                            }
                        });

                        if (!string.IsNullOrWhiteSpace(line.LineNotes))
                        {
                            col.Item().PaddingTop(2).Text($"Notes: {line.LineNotes}").FontSize(9).Italic();
                        }
                    }

                    if (model.HasShipping)
                    {
                        var methodName = model.ShippingMethodName ?? "Shipping";
                        col.Item().PaddingTop(14).Text(
                            $"Shipping ({methodName}): {model.ShippingCost.ToString("C2")}").SemiBold();
                        col.Item().Text("Shipping is added to each quantity total above.").FontSize(8).Italic()
                            .FontColor(Colors.Grey.Darken1);
                    }

                    if (!string.IsNullOrWhiteSpace(model.Notes))
                    {
                        col.Item().PaddingTop(12).Text("Notes").Bold().FontColor(Accent);
                        col.Item().Text(model.Notes);
                    }

                    col.Item().PaddingTop(18).Background(AccentSoft).Padding(10).Column(terms =>
                    {
                        terms.Item().Text("Terms").Bold().FontColor(Accent);
                        terms.Item().Text(termsText).FontSize(9);
                    });

                    if (!string.IsNullOrWhiteSpace(model.SalesRepName))
                    {
                        col.Item().PaddingTop(36).Text($"Sales representative: {model.SalesRepName}");
                    }
                    else
                    {
                        col.Item().PaddingTop(36).Text("Sales representative: ________________________________");
                    }
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
