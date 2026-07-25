using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Pdf;

public class EstimatePdfGenerator(IOptions<EstimateOptions> options, GeneralSettingsService generalSettings)
{
    /// <summary>Generates the PDF for a saved estimate, writes it to disk, and returns the path.</summary>
    public async Task<string> GenerateAsync(EstimateDetail detail, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var model = MapDetail(detail);
        var bytes = await GenerateBytesAsync(model, cancellationToken);

        var fileName = $"{model.EstimateNumber.Replace('/', '-')}_rev{model.RevisionNumber}.pdf";
        var fullPath = Path.Combine(settings.PdfStoragePath, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return fullPath;
    }

    /// <summary>Renders the PDF to a byte array without persisting (used for the in-browser preview).</summary>
    public async Task<byte[]> GenerateBytesAsync(EstimatePdfModel model, CancellationToken cancellationToken = default)
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

        return await Task.Run(() => Render(model, companyName, termsText, logo, contactLines), cancellationToken);
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

    private static byte[] Render(EstimatePdfModel model, string companyName, string termsText, byte[]? logo, IReadOnlyList<string> contactLines)
    {
        var metaLines = new List<string> { $"Date: {model.CreatedAt:MMM d, yyyy}" };
        if (model.ValidUntilDate.HasValue)
        {
            metaLines.Add($"Valid until: {model.ValidUntilDate:MMM d, yyyy}");
        }

        var chrome = new BrandedPageChrome(
            companyName, logo, contactLines,
            "ESTIMATE", 22f,
            $"{model.EstimateNumber} · Rev {model.RevisionNumber}",
            metaLines);

        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(
            pdf, PdfRect.Letter,
            BrandedPageChrome.PageMargin + chrome.HeaderHeight + 18f,
            BrandedPageChrome.PageMargin,
            BrandedPageChrome.PageMargin + 14f,
            BrandedPageChrome.PageMargin);

        document.Add(BuildAddressBlock(model));

        foreach (var line in model.Lines)
        {
            document.Add(Paragraph($"Line {line.LineNumber}: {line.ProductDescription}", StandardFont.HelveticaBold, 11f, marginTop: 14f));
            document.Add(Paragraph(
                $"Size: {line.LabelAcrossIn}\" × {line.LabelAroundIn}\"   •   " +
                $"Substrate: {line.SubstrateDescription}   •   Ink: {line.InkSet}",
                size: 9f, color: PdfStyle.GreyDarken1));

            document.Add(BuildBreaksTable(line));

            if (!string.IsNullOrWhiteSpace(line.LineNotes))
            {
                document.Add(Paragraph($"Notes: {line.LineNotes}", StandardFont.HelveticaOblique, 9f, marginTop: 2f));
            }
        }

        if (model.HasShipping)
        {
            var methodName = model.ShippingMethodName ?? "Shipping";
            document.Add(Paragraph($"Shipping ({methodName}): {model.ShippingCost:C2}", StandardFont.HelveticaBold, marginTop: 14f));
            document.Add(Paragraph("Shipping is added to each quantity total above.", StandardFont.HelveticaOblique, 8f, color: PdfStyle.GreyDarken1));
        }

        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            document.Add(Paragraph("Notes", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginTop: 12f));
            document.Add(Paragraph(model.Notes!));
        }

        document.Add(BuildTermsBox(termsText));

        document.Add(Paragraph(
            model.SalesRepName is { Length: > 0 } rep
                ? $"Sales representative: {rep}"
                : "Sales representative: ________________________________",
            marginTop: 36f));

        chrome.Stamp(pdf);
        return pdf.Save();
    }

    private static FlowTable BuildAddressBlock(EstimatePdfModel model)
    {
        var table = new FlowTable(new[] { 1f, 1f });

        var bill = new FlowCell { NoBorder = true, Padding = 0f };
        bill.Add(Paragraph("Bill to", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
        bill.Add(Paragraph(model.CustomerName, StandardFont.HelveticaBold));
        if (model.BillingAddress is { } billing)
        {
            if (!string.IsNullOrWhiteSpace(billing.Street1)) bill.Add(Paragraph(billing.Street1!));
            if (!string.IsNullOrWhiteSpace(billing.Street2)) bill.Add(Paragraph(billing.Street2!));
            bill.Add(Paragraph($"{billing.City}, {billing.State} {billing.Zip}"));
        }

        table.AddCell(bill);

        var ship = new FlowCell { NoBorder = true, Padding = 0f };
        if (model.ShipToAddress is { } shipTo)
        {
            ship.Add(Paragraph("Ship to", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
            if (!string.IsNullOrWhiteSpace(shipTo.RecipientName)) ship.Add(Paragraph(shipTo.RecipientName!, StandardFont.HelveticaBold));
            if (!string.IsNullOrWhiteSpace(shipTo.Street1)) ship.Add(Paragraph(shipTo.Street1!));
            if (!string.IsNullOrWhiteSpace(shipTo.Street2)) ship.Add(Paragraph(shipTo.Street2!));
            ship.Add(Paragraph($"{shipTo.City}, {shipTo.State} {shipTo.Zip}"));
        }

        table.AddCell(ship);
        return table;
    }

    private static FlowTable BuildBreaksTable(EstimatePdfLine line)
    {
        var table = new FlowTable(new[] { 2f, 1f, 1f }) { MarginTop = 6f, MarginBottom = 6f };
        table.AddHeaderCell(HeaderCell("Quantity"));
        table.AddHeaderCell(HeaderCell("Unit price", TextHorizontalAlignment.Right));
        table.AddHeaderCell(HeaderCell("Total", TextHorizontalAlignment.Right));

        foreach (var breakRow in line.Breaks)
        {
            table.AddCell(BodyCell(breakRow.Quantity.ToString("N0")));
            table.AddCell(BodyCell(breakRow.UnitPrice.ToString("C4"), TextHorizontalAlignment.Right));
            table.AddCell(BodyCell(breakRow.TotalPrice.ToString("C2"), TextHorizontalAlignment.Right, StandardFont.HelveticaBold));
        }

        return table;
    }

    private static FlowTable BuildTermsBox(string termsText)
    {
        var cell = new FlowCell { NoBorder = true, Background = PdfStyle.AccentSoft, Padding = 10f };
        cell.Add(Paragraph("Terms", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
        cell.Add(Paragraph(termsText, size: 9f));
        return new FlowTable(new[] { 1f }) { MarginTop = 18f }.AddCell(cell);
    }

    private static FlowCell HeaderCell(string text, TextHorizontalAlignment alignment = TextHorizontalAlignment.Left)
    {
        return new FlowCell
        {
            NoBorder = true,
            Background = PdfStyle.Accent,
            Padding = 5f,
            TextAlignment = alignment
        }.Add(Paragraph(text, StandardFont.HelveticaBold, 9f, color: PdfStyle.White));
    }

    private static FlowCell BodyCell(string text, TextHorizontalAlignment alignment = TextHorizontalAlignment.Left, PdfFont? font = null)
    {
        return new FlowCell
        {
            Border = (PdfStyle.GreyLighten2, 0.5f),
            Padding = 5f,
            TextAlignment = alignment
        }.Add(Paragraph(text, font, 9.5f));
    }

    private static FlowParagraph Paragraph(
        string text,
        PdfFont? font = null,
        float size = 10f,
        (float R, float G, float B)? color = null,
        float marginTop = 0f,
        float marginBottom = 0f)
    {
        return new FlowParagraph(text)
        {
            DefaultFont = font ?? StandardFont.Helvetica,
            DefaultFontSize = size,
            DefaultColor = color ?? PdfStyle.GreyDarken3,
            MarginTop = marginTop,
            MarginBottom = marginBottom > 0f ? marginBottom : 2f
        };
    }
}
