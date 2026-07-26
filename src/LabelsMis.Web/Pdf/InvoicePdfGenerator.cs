using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Pdf;

public class InvoicePdfGenerator(IOptions<InvoiceOptions> options, GeneralSettingsService generalSettings)
{
    /// <summary>Generates the PDF for an invoice, writes it to disk, and returns the path.</summary>
    public async Task<string> GenerateAsync(InvoiceDetail detail, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        var bytes = await GenerateBytesAsync(detail, cancellationToken);
        var fileName = $"{detail.Invoice.InvoiceNumber.Replace('/', '-')}.pdf";
        var fullPath = Path.Combine(settings.PdfStoragePath, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return fullPath;
    }

    /// <summary>Renders the invoice PDF to a byte array without persisting.</summary>
    public async Task<byte[]> GenerateBytesAsync(InvoiceDetail detail, CancellationToken cancellationToken = default)
    {
        var branding = await generalSettings.GetAsync(cancellationToken);
        var companyName = !string.IsNullOrWhiteSpace(branding?.CompanyName) ? branding!.CompanyName : "Labels MIS Print Shop";
        var termsText = branding?.TermsText;
        var logo = branding is { HasLogo: true } ? branding.LogoBytes : null;
        var contactLines = new[] { branding?.AddressLine1, branding?.AddressLine2, branding?.CityStateZip, branding?.Phone, branding?.Email, branding?.Website }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!)
            .ToList();

        return await Task.Run(() => Render(detail, companyName, termsText, logo, contactLines), cancellationToken);
    }

    private static byte[] Render(InvoiceDetail detail, string companyName, string? termsText, byte[]? logo, IReadOnlyList<string> contactLines)
    {
        var invoice = detail.Invoice;
        var metaLines = new List<string>
        {
            $"Date: {invoice.InvoiceDate:MMM d, yyyy}",
            $"Due: {invoice.DueDate:MMM d, yyyy}",
            $"Terms: {invoice.Customer.Terms.Label()}",
            $"Order: {detail.OrderNumber}"
        };
        if (!string.IsNullOrWhiteSpace(invoice.SalesOrder.CustomerPoNumber))
        {
            metaLines.Add($"Customer PO: {invoice.SalesOrder.CustomerPoNumber}");
        }

        var chrome = new BrandedPageChrome(
            companyName, logo, contactLines,
            "INVOICE", 22f,
            invoice.InvoiceNumber,
            metaLines);

        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(
            pdf, PdfRect.Letter,
            BrandedPageChrome.PageMargin + chrome.HeaderHeight + 18f,
            BrandedPageChrome.PageMargin,
            BrandedPageChrome.PageMargin + 14f,
            BrandedPageChrome.PageMargin);

        document.Add(BuildBillToBlock(detail));
        document.Add(BuildLinesTable(invoice));
        document.Add(BuildTotalsTable(invoice));

        if (invoice.Payments.Count > 0)
        {
            document.Add(Paragraph("Payments received", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginTop: 14f));
            document.Add(BuildPaymentsTable(invoice));
        }

        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            document.Add(Paragraph("Notes", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginTop: 12f));
            document.Add(Paragraph(invoice.Notes!));
        }

        if (!string.IsNullOrWhiteSpace(termsText))
        {
            var cell = new FlowCell { NoBorder = true, Background = PdfStyle.AccentSoft, Padding = 10f };
            cell.Add(Paragraph("Terms", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
            cell.Add(Paragraph(termsText!, size: 9f));
            document.Add(new FlowTable(new[] { 1f }) { MarginTop = 18f }.AddCell(cell));
        }

        chrome.Stamp(pdf);
        return pdf.Save();
    }

    private static FlowTable BuildBillToBlock(InvoiceDetail detail)
    {
        var invoice = detail.Invoice;
        var billing = invoice.Customer.Addresses.FirstOrDefault(a => a.IsDefault)
            ?? invoice.Customer.Addresses.FirstOrDefault();

        var table = new FlowTable(new[] { 1f, 1f });

        var bill = new FlowCell { NoBorder = true, Padding = 0f };
        bill.Add(Paragraph("Bill to", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
        bill.Add(Paragraph(invoice.Customer.Name, StandardFont.HelveticaBold));
        if (billing is not null)
        {
            if (!string.IsNullOrWhiteSpace(billing.Street1)) bill.Add(Paragraph(billing.Street1));
            if (!string.IsNullOrWhiteSpace(billing.Street2)) bill.Add(Paragraph(billing.Street2!));
            bill.Add(Paragraph($"{billing.City}, {billing.State} {billing.Zip}"));
        }
        table.AddCell(bill);

        var status = new FlowCell { NoBorder = true, Padding = 0f, TextAlignment = TextHorizontalAlignment.Right };
        status.Add(Paragraph("Balance due", StandardFont.HelveticaBold, color: PdfStyle.Accent, marginBottom: 2f));
        status.Add(Paragraph(invoice.BalanceDue.ToString("C2"), StandardFont.HelveticaBold, 16f));
        if (invoice.BalanceDue <= 0)
        {
            status.Add(Paragraph("PAID IN FULL", StandardFont.HelveticaBold, 10f, PdfStyle.GreyDarken1));
        }
        table.AddCell(status);
        return table;
    }

    private static FlowTable BuildLinesTable(Domain.Entities.Invoice invoice)
    {
        var table = new FlowTable(new[] { 0.6f, 4f, 1f, 1.2f, 1.2f }) { MarginTop = 14f, MarginBottom = 6f };
        table.AddHeaderCell(HeaderCell("#"));
        table.AddHeaderCell(HeaderCell("Description"));
        table.AddHeaderCell(HeaderCell("Qty", TextHorizontalAlignment.Right));
        table.AddHeaderCell(HeaderCell("Unit price", TextHorizontalAlignment.Right));
        table.AddHeaderCell(HeaderCell("Total", TextHorizontalAlignment.Right));

        foreach (var line in invoice.Lines.OrderBy(l => l.LineNumber))
        {
            table.AddCell(BodyCell(line.LineNumber.ToString()));
            table.AddCell(BodyCell(line.Description));
            table.AddCell(BodyCell(line.Quantity.ToString("N0"), TextHorizontalAlignment.Right));
            table.AddCell(BodyCell(line.UnitPrice.ToString("C4"), TextHorizontalAlignment.Right));
            table.AddCell(BodyCell(line.LineTotal.ToString("C2"), TextHorizontalAlignment.Right, StandardFont.HelveticaBold));
        }

        return table;
    }

    private static FlowTable BuildTotalsTable(Domain.Entities.Invoice invoice)
    {
        var amountPaid = invoice.Total - invoice.BalanceDue;
        var table = new FlowTable(new[] { 5f, 1.6f, 1.4f }) { MarginTop = 2f };

        void Row(string label, string value, bool bold = false, bool accent = false)
        {
            table.AddCell(new FlowCell { NoBorder = true, Padding = 2f });
            table.AddCell(new FlowCell { NoBorder = true, Padding = 3f, TextAlignment = TextHorizontalAlignment.Right }
                .Add(Paragraph(label, bold ? StandardFont.HelveticaBold : StandardFont.Helvetica, 9.5f, accent ? PdfStyle.Accent : PdfStyle.GreyDarken1)));
            table.AddCell(new FlowCell { NoBorder = true, Padding = 3f, TextAlignment = TextHorizontalAlignment.Right }
                .Add(Paragraph(value, bold ? StandardFont.HelveticaBold : StandardFont.Helvetica, bold ? 10.5f : 9.5f, accent ? PdfStyle.Accent : PdfStyle.GreyDarken3)));
        }

        Row("Subtotal", invoice.Subtotal.ToString("C2"));
        if (invoice.TaxAmount != 0)
        {
            Row("Tax", invoice.TaxAmount.ToString("C2"));
        }
        if (invoice.ShippingAmount != 0)
        {
            Row("Shipping", invoice.ShippingAmount.ToString("C2"));
        }
        Row("Total", invoice.Total.ToString("C2"), bold: true);
        if (amountPaid > 0)
        {
            Row("Paid", (-amountPaid).ToString("C2"));
        }
        Row("Balance due", invoice.BalanceDue.ToString("C2"), bold: true, accent: true);
        return table;
    }

    private static FlowTable BuildPaymentsTable(Domain.Entities.Invoice invoice)
    {
        var table = new FlowTable(new[] { 1.2f, 1.2f, 2f, 1.2f }) { MarginTop = 4f };
        table.AddHeaderCell(HeaderCell("Date"));
        table.AddHeaderCell(HeaderCell("Method"));
        table.AddHeaderCell(HeaderCell("Reference"));
        table.AddHeaderCell(HeaderCell("Amount", TextHorizontalAlignment.Right));

        foreach (var payment in invoice.Payments.OrderBy(p => p.PaymentDate))
        {
            table.AddCell(BodyCell(payment.PaymentDate.ToString("MMM d, yyyy")));
            table.AddCell(BodyCell(payment.Method.ToString()));
            table.AddCell(BodyCell(payment.Reference ?? "—"));
            table.AddCell(BodyCell(payment.Amount.ToString("C2"), TextHorizontalAlignment.Right));
        }

        return table;
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
