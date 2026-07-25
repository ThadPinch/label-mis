using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Domain.Entities;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;

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
    /// <summary>Generates the PO PDF, writes it to disk, and returns the path (used for email attachments).</summary>
    public async Task<string> GenerateAsync(PurchaseOrder po, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var bytes = await GenerateBytesAsync(po, cancellationToken);
        var fileName = $"{po.PoNumber.Replace('/', '-')}.pdf";
        var fullPath = Path.Combine(settings.PdfStoragePath, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return fullPath;
    }

    /// <summary>Renders the PO PDF to a byte array without persisting (used for the in-browser view).</summary>
    public async Task<byte[]> GenerateBytesAsync(PurchaseOrder po, CancellationToken cancellationToken = default)
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

        return await Task.Run(() => Render(po, companyName, termsText, logo, contactLines), cancellationToken);
    }

    private static byte[] Render(PurchaseOrder po, string companyName, string termsText, byte[]? logo, IReadOnlyList<string> contactLines)
    {
        var lines = po.Lines.OrderBy(l => l.LineNumber).ToList();
        var orderTotal = lines.Sum(l => l.LineTotal);

        var metaLines = new List<string> { $"Date: {po.OrderedAt:MMM d, yyyy}" };
        if (po.ExpectedAt.HasValue)
        {
            metaLines.Add($"Expected: {po.ExpectedAt:MMM d, yyyy}");
        }

        metaLines.Add($"Status: {po.Status}");

        var chrome = new BrandedPageChrome(
            companyName, logo, contactLines,
            "PURCHASE ORDER", 20f,
            po.PoNumber,
            metaLines);

        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(
            pdf, PdfRect.Letter,
            BrandedPageChrome.PageMargin + chrome.HeaderHeight + 18f,
            BrandedPageChrome.PageMargin,
            BrandedPageChrome.PageMargin + 14f,
            BrandedPageChrome.PageMargin);

        document.Add(BuildAddressBlock(po, companyName, contactLines));
        document.Add(BuildLinesTable(lines));

        document.Add(new FlowParagraph($"Order total: {orderTotal:C2}")
        {
            Alignment = TextHorizontalAlignment.Right,
            DefaultFont = StandardFont.HelveticaBold,
            DefaultFontSize = 12f,
            DefaultColor = PdfStyle.Accent,
            MarginTop = 8f
        });

        if (!string.IsNullOrWhiteSpace(po.Notes))
        {
            document.Add(Paragraph("Notes", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginTop: 12f));
            document.Add(Paragraph(po.Notes!));
        }

        document.Add(BuildTermsBox(termsText));

        chrome.Stamp(pdf);
        return pdf.Save();
    }

    private static FlowTable BuildAddressBlock(PurchaseOrder po, string companyName, IReadOnlyList<string> contactLines)
    {
        var table = new FlowTable(new[] { 1f, 1f });

        var vendor = new FlowCell { NoBorder = true, Padding = 0f };
        vendor.Add(Paragraph("Vendor", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
        vendor.Add(Paragraph(po.Supplier.Name, StandardFont.HelveticaBold));
        if (!string.IsNullOrWhiteSpace(po.Supplier.Code)) vendor.Add(Paragraph($"Code: {po.Supplier.Code}"));
        if (!string.IsNullOrWhiteSpace(po.Supplier.AccountNumber)) vendor.Add(Paragraph($"Account #: {po.Supplier.AccountNumber}"));
        if (!string.IsNullOrWhiteSpace(po.Supplier.Terms)) vendor.Add(Paragraph($"Terms: {po.Supplier.Terms}"));
        table.AddCell(vendor);

        var ship = new FlowCell { NoBorder = true, Padding = 0f };
        ship.Add(Paragraph("Ship to", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
        ship.Add(Paragraph(companyName, StandardFont.HelveticaBold));
        foreach (var contactLine in contactLines.Take(3))
        {
            ship.Add(Paragraph(contactLine, size: 9f));
        }

        table.AddCell(ship);
        return table;
    }

    private static FlowTable BuildLinesTable(IReadOnlyList<PurchaseOrderLine> lines)
    {
        var table = new FlowTable(new[] { 0.55f, 3f, 1f, 1f, 1f }) { MarginTop = 10f, MarginBottom = 6f };
        table.AddHeaderCell(HeaderCell("#"));
        table.AddHeaderCell(HeaderCell("Stock"));
        table.AddHeaderCell(HeaderCell("Qty (LF)", TextHorizontalAlignment.Right));
        table.AddHeaderCell(HeaderCell("Unit cost", TextHorizontalAlignment.Right));
        table.AddHeaderCell(HeaderCell("Total", TextHorizontalAlignment.Right));

        foreach (var line in lines)
        {
            table.AddCell(BodyCell(line.LineNumber.ToString()));
            table.AddCell(BodyCell($"{line.Stock.Code} — {line.Stock.Description}"));
            table.AddCell(BodyCell(line.QuantityLf.ToString("N2"), TextHorizontalAlignment.Right));
            table.AddCell(BodyCell(line.UnitCost.ToString("C4"), TextHorizontalAlignment.Right));
            table.AddCell(BodyCell(line.LineTotal.ToString("C2"), TextHorizontalAlignment.Right, StandardFont.HelveticaBold));
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
