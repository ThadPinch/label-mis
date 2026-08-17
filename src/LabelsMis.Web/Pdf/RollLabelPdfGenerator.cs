using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using LabelsMis.Web.Services.Settings;

namespace LabelsMis.Web.Pdf;

/// <summary>Everything printed on a roll's inventory label.</summary>
public record RollLabelModel(
    string Barcode,
    string StockCode,
    string StockDescription,
    string StockType,
    string? FaceMaterial,
    string? Adhesive,
    string? Liner,
    decimal CaliperMil,
    string SupplierName,
    string? SupplierPartNumber,
    string LotNumber,
    string? PoNumber,
    decimal WidthIn,
    decimal OriginalLengthLf,
    decimal RemainingLengthLf,
    DateTime ReceivedAt,
    string? Location,
    string Status);

/// <summary>
/// Renders the 4" × 6" roll label that goes on a roll's wrap or core: a large Code 128 of the
/// roll barcode, the stock it is, and the lot / size / where-it-came-from facts an operator
/// needs when pulling material — everything the /rolls/{id} page knows, on one tag.
/// </summary>
public class RollLabelPdfGenerator(GeneralSettingsService generalSettings)
{
    /// <summary>4" × 6" portrait, the common thermal-label size (also prints centered on Letter).</summary>
    public static readonly PdfRect LabelSize = new(0, 0, 4f * 72f, 6f * 72f);

    private const float Margin = 16f;
    private const float BarcodeHeight = 76f;
    private const float RuleGap = 8f;

    public async Task<byte[]> GenerateBytesAsync(RollLabelModel model, CancellationToken cancellationToken = default)
    {
        var branding = await generalSettings.GetAsync(cancellationToken);
        var companyName = !string.IsNullOrWhiteSpace(branding?.CompanyName) ? branding!.CompanyName : "Labels MIS Print Shop";
        return await Task.Run(() => Render(model, companyName), cancellationToken);
    }

    private static byte[] Render(RollLabelModel model, string companyName)
    {
        using var pdf = PdfDocument.Create();
        pdf.SetCreator("Labels MIS");
        var page = pdf.AddPage(LabelSize);
        var canvas = PdfStyle.OverlayCanvas(pdf, page);

        var left = Margin;
        var right = LabelSize.Width - Margin;
        var width = right - left;
        var y = LabelSize.Height - Margin;

        // Header strip: who printed it, what kind of roll it is.
        PdfStyle.WriteLine(canvas, companyName, StandardFont.Helvetica, 8f, PdfStyle.GreyDarken1, left, y, TextHorizontalAlignment.Left);
        y = PdfStyle.WriteLine(canvas, $"{model.StockType.ToUpperInvariant()} ROLL", StandardFont.HelveticaBold, 8f, PdfStyle.GreyDarken1, right, y, TextHorizontalAlignment.Right);
        y -= 6f;

        // Barcode spanning the label, human-readable barcode underneath.
        var barcode = Barcode1D.Encode(model.Barcode, BarcodeSymbology.Code128);
        var form = barcode.CreateFormXObject(pdf, heightModules: BarcodeHeight);
        var scaleX = width / barcode.WidthWithQuietZones;
        canvas.AddFormXObject(form, scaleX, 0, 0, 1f, left, y - BarcodeHeight);
        y -= BarcodeHeight + 4f;
        var barcodeSize = FitFontSize(StandardFont.CourierBold, model.Barcode, 22f, 12f, width);
        y = PdfStyle.WriteLine(canvas, model.Barcode, StandardFont.CourierBold, barcodeSize, PdfStyle.Black, left + width / 2f, y, TextHorizontalAlignment.Center);

        y = Rule(canvas, left, right, y);

        // Stock block: code, description, construction.
        var codeSize = FitFontSize(StandardFont.HelveticaBold, model.StockCode, 20f, 12f, width);
        y = PdfStyle.WriteLine(canvas, model.StockCode, StandardFont.HelveticaBold, codeSize, PdfStyle.Black, left, y, TextHorizontalAlignment.Left);
        y -= 2f;
        y = WriteWrapped(canvas, model.StockDescription, StandardFont.Helvetica, 11f, PdfStyle.GreyDarken3, left, y, width, maxLines: 2);

        var construction = string.Join("  ·  ", new[]
        {
            model.FaceMaterial,
            model.Adhesive,
            model.Liner,
            model.CaliperMil > 0 ? $"{model.CaliperMil:0.##} mil" : null
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (construction.Length > 0)
        {
            y -= 2f;
            y = WriteWrapped(canvas, construction, StandardFont.Helvetica, 8f, PdfStyle.GreyDarken1, left, y, width, maxLines: 2);
        }

        y = Rule(canvas, left, right, y);

        // Size row: the three numbers you read off a roll at a glance.
        var thirds = width / 3f;
        y = DrawFactRow(canvas, left, thirds, y, 16f,
            ("WIDTH", $"{model.WidthIn:0.##}\""),
            ("LENGTH", $"{model.OriginalLengthLf:N0} LF"),
            ("REMAINING", $"{model.RemainingLengthLf:N0} LF"));
        y -= 6f;

        // Provenance grid.
        var halves = width / 2f;
        y = DrawFactRow(canvas, left, halves, y, 11f,
            ("LOT", model.LotNumber),
            ("SUPPLIER", model.SupplierName));
        y = DrawFactRow(canvas, left, halves, y, 11f,
            ("PO", model.PoNumber ?? "—"),
            ("SUPPLIER PART #", model.SupplierPartNumber ?? "—"));
        y = DrawFactRow(canvas, left, halves, y, 11f,
            ("RECEIVED", model.ReceivedAt.ToLocalTime().ToString("MMM d, yyyy")),
            ("STATUS", model.Status));

        // Location gets the widest slot: it's what someone walks the floor with.
        DrawFactRow(canvas, left, width, y, 14f, ("LOCATION", model.Location ?? "—"));

        // Footer.
        PdfStyle.WriteLine(canvas, $"Printed {DateTime.Now:yyyy-MM-dd HH:mm}", StandardFont.Helvetica, 7f, PdfStyle.GreyMedium,
            left, Margin + 7f * 1.2f, TextHorizontalAlignment.Left);
        PdfStyle.WriteLine(canvas, model.Barcode, StandardFont.Helvetica, 7f, PdfStyle.GreyMedium,
            right, Margin + 7f * 1.2f, TextHorizontalAlignment.Right);

        return pdf.Save();
    }

    /// <summary>A row of label-over-value cells, each <paramref name="cellWidth"/> wide; returns the next top.</summary>
    private static float DrawFactRow(
        PdfCanvas canvas,
        float left,
        float cellWidth,
        float top,
        float valueSize,
        params (string Label, string Value)[] cells)
    {
        const float labelSize = 6.5f;
        var bottom = top;
        for (var i = 0; i < cells.Length; i++)
        {
            var x = left + i * cellWidth;
            var innerWidth = cellWidth - 6f;
            var y = PdfStyle.WriteLine(canvas, cells[i].Label, StandardFont.HelveticaBold, labelSize, PdfStyle.GreyDarken1, x, top, TextHorizontalAlignment.Left);
            var value = Truncate(StandardFont.HelveticaBold, valueSize, cells[i].Value, innerWidth);
            y = PdfStyle.WriteLine(canvas, value, StandardFont.HelveticaBold, valueSize, PdfStyle.Black, x, y, TextHorizontalAlignment.Left);
            bottom = Math.Min(bottom, y);
        }

        return bottom - 8f;
    }

    private static float Rule(PdfCanvas canvas, float left, float right, float top)
    {
        var y = top - RuleGap / 2f;
        canvas.SaveState();
        canvas.SetStrokeRgb(PdfStyle.GreyLighten1.R, PdfStyle.GreyLighten1.G, PdfStyle.GreyLighten1.B);
        canvas.SetLineWidth(0.75f);
        canvas.MoveTo(left, y);
        canvas.LineTo(right, y);
        canvas.Stroke();
        canvas.RestoreState();
        return top - RuleGap;
    }

    /// <summary>Writes word-wrapped text capped at <paramref name="maxLines"/> (last line ellipsized); returns the next top.</summary>
    private static float WriteWrapped(
        PdfCanvas canvas,
        string text,
        PdfFont font,
        float size,
        (float R, float G, float B) color,
        float x,
        float top,
        float width,
        int maxLines)
    {
        var lines = PdfCanvasTextExtensions.WrapLines(font, size, text, width);
        if (lines.Count > maxLines)
        {
            var kept = lines.Take(maxLines).ToList();
            kept[^1] = Truncate(font, size, string.Join(" ", lines.Skip(maxLines - 1)), width);
            lines = kept;
        }

        var y = top;
        foreach (var line in lines)
        {
            y = PdfStyle.WriteLine(canvas, line, font, size, color, x, y, TextHorizontalAlignment.Left);
        }

        return y;
    }

    /// <summary>Largest size between <paramref name="min"/> and <paramref name="max"/> at which the text fits the width.</summary>
    private static float FitFontSize(PdfFont font, string text, float max, float min, float width)
    {
        var size = max;
        while (size > min && font.GetWidth(text, size) > width)
        {
            size -= 0.5f;
        }

        return size;
    }

    private static string Truncate(PdfFont font, float size, string text, float width)
    {
        if (font.GetWidth(text, size) <= width)
        {
            return text;
        }

        var trimmed = text;
        while (trimmed.Length > 1 && font.GetWidth(trimmed + "…", size) > width)
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return trimmed + "…";
    }
}
