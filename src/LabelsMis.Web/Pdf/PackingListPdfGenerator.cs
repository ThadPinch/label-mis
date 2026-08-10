using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Domain.Entities;
using LabelsMis.Web.Services.Settings;

namespace LabelsMis.Web.Pdf;

/// <summary>
/// Renders the packing list that travels with an order: business logo top-left, customer name,
/// project/method/date fill-in rows, the item table, and a received-by signature section.
/// </summary>
public class PackingListPdfGenerator(GeneralSettingsService generalSettings)
{
    /// <summary>Renders the packing list PDF to a byte array without persisting.</summary>
    public async Task<byte[]> GenerateBytesAsync(SalesOrder order, DateOnly shipDate, int boxCount, CancellationToken cancellationToken = default)
    {
        var branding = await generalSettings.GetAsync(cancellationToken);
        var companyName = !string.IsNullOrWhiteSpace(branding?.CompanyName) ? branding!.CompanyName : "Labels MIS Print Shop";
        var logo = branding is { HasLogo: true } ? branding.LogoBytes : null;

        return await Task.Run(() => Render(order, shipDate, boxCount, companyName, logo), cancellationToken);
    }

    private const float Margin = 48f;
    private const float LogoMaxWidth = 250f;
    private const float LogoMaxHeight = 85f;
    /// <summary>Fixed header band: logo box, then the Company Name block underneath.</summary>
    private const float HeaderHeight = 142f;

    private static readonly (float R, float G, float B) HeaderBand = (0xc9 / 255f, 0xdf / 255f, 0xe0 / 255f);

    private static byte[] Render(SalesOrder order, DateOnly shipDate, int boxCount, string companyName, byte[]? logoBytes)
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(
            pdf, PdfRect.Letter,
            Margin + HeaderHeight + 34f,
            Margin,
            Margin + 20f,
            Margin);

        document.Add(BuildItemsTable(order));
        document.Add(BuildReceivedByBlock(boxCount));

        StampChrome(pdf, order, shipDate, companyName, logoBytes);
        return pdf.Save();
    }

    private static FlowTable BuildItemsTable(SalesOrder order)
    {
        var table = new FlowTable(new[] { 1.0f, 3.6f, 1.0f });
        table.AddHeaderCell(HeaderCell("Item:"));
        table.AddHeaderCell(HeaderCell("Order Description:"));
        table.AddHeaderCell(HeaderCell("QTY:", TextHorizontalAlignment.Right));

        var row = 0;
        foreach (var line in order.Lines.OrderBy(l => l.LineNumber))
        {
            row++;
            AddItemRow(table, row, line.Description ?? line.Product.Description, line.Quantity.ToString("N0"));
        }

        return table;
    }

    private static void AddItemRow(FlowTable table, int number, string description, string quantity)
    {
        table.AddCell(ItemCell($"{number}.", StandardFont.HelveticaBold, color: PdfStyle.Black));
        table.AddCell(ItemCell(description, StandardFont.Helvetica));
        table.AddCell(ItemCell(quantity, StandardFont.Helvetica, TextHorizontalAlignment.Right));
    }

    private static FlowTable BuildReceivedByBlock(int boxCount)
    {
        // A single borderless table row so the whole block page-breaks as one unit.
        var cell = new FlowCell { NoBorder = true, Padding = 0f };
        cell.Add(new FlowParagraph("Received By:")
        {
            DefaultFont = StandardFont.HelveticaBold,
            DefaultFontSize = 13f,
            DefaultColor = PdfStyle.Black,
            MarginBottom = 10f
        });
        cell.Add(new SignatureLine("Name:"));
        cell.Add(new SignatureLine("Signature:"));
        cell.Add(new SignatureLine("Number of Boxes:", boxCount > 0 ? boxCount.ToString() : null));
        return new FlowTable(new[] { 1f }) { MarginTop = 46f }.AddCell(cell);
    }

    private static void StampChrome(PdfDocument pdf, SalesOrder order, DateOnly shipDate, string companyName, byte[]? logoBytes)
    {
        var projectNumber = order.OrderNumber.Contains('-')
            ? order.OrderNumber[(order.OrderNumber.LastIndexOf('-') + 1)..]
            : order.OrderNumber;
        var metaRows = new (string Label, string Value)[]
        {
            ("Project:", projectNumber),
            ("Method:", order.ShippingMethod?.Name ?? string.Empty),
            ("Date:", shipDate.ToString("M/d/yy"))
        };

        var logo = PdfStyle.TryCreateImage(pdf, logoBytes);
        var total = pdf.PageCount;
        for (var i = 0; i < total; i++)
        {
            var page = pdf.Pages[i];
            var canvas = PdfStyle.OverlayCanvas(pdf, page);
            DrawHeader(canvas, page.Size, order.Customer.Name, companyName, logo, metaRows);
            if (total > 1)
            {
                PdfStyle.WriteLine(canvas, $"Page {i + 1} of {total}", StandardFont.Helvetica, 8f, PdfStyle.GreyMedium,
                    page.Size.Width / 2f, 34f, TextHorizontalAlignment.Center);
            }
        }
    }

    private static void DrawHeader(
        PdfCanvas canvas,
        PdfRect pageSize,
        string customerName,
        string companyName,
        PdfImageXObject? logo,
        (string Label, string Value)[] metaRows)
    {
        var left = Margin;
        var right = pageSize.Width - Margin;
        var top = pageSize.Height - Margin;

        if (logo is not null)
        {
            var scale = Math.Min(LogoMaxWidth / Math.Max(1, logo.Width), LogoMaxHeight / Math.Max(1, logo.Height));
            var width = logo.Width * scale;
            var height = logo.Height * scale;
            canvas.AddImageFittedIntoRectangle(logo, new PdfRect(left, top - height, width, height));
        }
        else
        {
            PdfStyle.WriteLine(canvas, companyName, StandardFont.HelveticaBold, 22f, PdfStyle.Accent, left, top, TextHorizontalAlignment.Left);
        }

        var y = top - LogoMaxHeight - 20f;
        y = PdfStyle.WriteLine(canvas, "Company Name:", StandardFont.HelveticaBold, 13f, PdfStyle.Black, left, y, TextHorizontalAlignment.Left);
        y -= 2f;
        PdfStyle.WriteLine(canvas, customerName, StandardFont.Helvetica, 11.5f, PdfStyle.GreyDarken3, left, y, TextHorizontalAlignment.Left);

        var metaX = 340f;
        var rowTop = top - 24f;
        foreach (var (label, value) in metaRows)
        {
            var baseline = rowTop - 13f;
            canvas.SaveState();
            canvas.SetFillRgb(PdfStyle.Black.R, PdfStyle.Black.G, PdfStyle.Black.B);
            canvas.ShowTextAligned(StandardFont.HelveticaBold, 13f, label, metaX, baseline, TextHorizontalAlignment.Left);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var valueX = metaX + StandardFont.HelveticaBold.GetWidth(label, 13f) + 8f;
                canvas.ShowTextAligned(StandardFont.Helvetica, 11.5f, value, valueX, baseline, TextHorizontalAlignment.Left);
            }

            canvas.SetStrokeRgb(PdfStyle.Black.R, PdfStyle.Black.G, PdfStyle.Black.B);
            canvas.SetLineWidth(1.2f);
            canvas.MoveTo(metaX, baseline - 4.5f);
            canvas.LineTo(right, baseline - 4.5f);
            canvas.Stroke();
            canvas.RestoreState();
            rowTop -= 33f;
        }
    }

    private static FlowCell HeaderCell(string text, TextHorizontalAlignment alignment = TextHorizontalAlignment.Left)
    {
        return new FlowCell
        {
            NoBorder = true,
            Background = HeaderBand,
            Padding = 10f,
            TextAlignment = alignment
        }.Add(new FlowParagraph(text)
        {
            DefaultFont = StandardFont.HelveticaBold,
            DefaultFontSize = 12.5f,
            DefaultColor = PdfStyle.Black
        });
    }

    private static FlowCell ItemCell(
        string text,
        PdfFont font,
        TextHorizontalAlignment alignment = TextHorizontalAlignment.Left,
        (float R, float G, float B)? color = null)
    {
        return new FlowCell
        {
            NoBorder = true,
            Padding = 10f,
            TextAlignment = alignment
        }.Add(new FlowParagraph(text)
        {
            DefaultFont = font,
            DefaultFontSize = 11f,
            DefaultColor = color ?? PdfStyle.GreyDarken3
        });
    }

    /// <summary>A "Label: ________" fill-in row, drawn as one element so label and line move together.</summary>
    private sealed class SignatureLine(string label, string? value = null) : FlowElement
    {
        private const float Height = 32f;
        private const float Indent = 45f;
        private const float LineEnd = 440f;

        internal override float Measure(float width) => Height;

        internal override void Draw(FlowContext context, float x, float top, float width)
        {
            var canvas = context.Canvas;
            var baseline = top - Height + 8f;
            canvas.SaveState();
            canvas.SetFillRgb(PdfStyle.Black.R, PdfStyle.Black.G, PdfStyle.Black.B);
            canvas.ShowTextAligned(StandardFont.HelveticaBold, 12f, label, x + Indent, baseline, TextHorizontalAlignment.Left);
            var lineStart = x + Indent + StandardFont.HelveticaBold.GetWidth(label, 12f) + 8f;
            if (!string.IsNullOrWhiteSpace(value))
            {
                canvas.ShowTextAligned(StandardFont.Helvetica, 11f, value!, lineStart + 4f, baseline, TextHorizontalAlignment.Left);
            }

            canvas.SetStrokeRgb(PdfStyle.Black.R, PdfStyle.Black.G, PdfStyle.Black.B);
            canvas.SetLineWidth(1.2f);
            canvas.MoveTo(lineStart, baseline - 3.5f);
            canvas.LineTo(x + Math.Min(LineEnd, width), baseline - 3.5f);
            canvas.Stroke();
            canvas.RestoreState();
        }
    }
}
