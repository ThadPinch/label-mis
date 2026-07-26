using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Cos;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;

namespace LabelsMis.Web.Pdf;

/// <summary>
/// A Code 128 barcode as a flow element, right-aligned in its box. Possible because the vendored
/// PdfPlatform sources compile into this assembly, so FlowElement's internal contract is visible.
/// </summary>
internal sealed class FlowBarcode(string content, float height, float maxWidth = 130f) : FlowElement
{
    internal override float Measure(float width) => height;

    internal override void Draw(FlowContext context, float x, float top, float width)
    {
        var barcode = Barcode1D.Encode(content, BarcodeSymbology.Code128);
        var form = barcode.CreateFormXObject(context.Document, heightModules: height);
        var drawWidth = Math.Min(maxWidth, width);
        var scaleX = drawWidth / barcode.WidthWithQuietZones;
        context.Canvas.AddFormXObject(form, scaleX, 0, 0, 1f, x + width - drawWidth, top - height);
    }
}

/// <summary>Shared palette and low-level drawing helpers for the PdfPlatform-based generators.</summary>
internal static class PdfStyle
{
    public static readonly (float R, float G, float B) Accent = Rgb(0x00, 0x5d, 0x66);
    public static readonly (float R, float G, float B) AccentSoft = Rgb(0xe6, 0xef, 0xf0);
    public static readonly (float R, float G, float B) White = (1f, 1f, 1f);
    public static readonly (float R, float G, float B) Black = (0f, 0f, 0f);
    public static readonly (float R, float G, float B) GreyLighten4 = Rgb(0xf5, 0xf5, 0xf5);
    public static readonly (float R, float G, float B) GreyLighten3 = Rgb(0xee, 0xee, 0xee);
    public static readonly (float R, float G, float B) GreyLighten2 = Rgb(0xe0, 0xe0, 0xe0);
    public static readonly (float R, float G, float B) GreyLighten1 = Rgb(0xbd, 0xbd, 0xbd);
    public static readonly (float R, float G, float B) GreyMedium = Rgb(0x9e, 0x9e, 0x9e);
    public static readonly (float R, float G, float B) GreyDarken1 = Rgb(0x75, 0x75, 0x75);
    public static readonly (float R, float G, float B) GreyDarken2 = Rgb(0x61, 0x61, 0x61);
    public static readonly (float R, float G, float B) GreyDarken3 = Rgb(0x42, 0x42, 0x42);

    private static (float R, float G, float B) Rgb(byte r, byte g, byte b) => (r / 255f, g / 255f, b / 255f);

    /// <summary>Opens an overlay canvas appended after the page's existing content.</summary>
    public static PdfCanvas OverlayCanvas(PdfDocument pdf, PdfPage page)
    {
        var resources = page.Resources;
        if (resources is null)
        {
            resources = new CosDictionary();
            page.Dictionary.Put(CosNames.Resources, resources);
        }

        return new PdfCanvas(page.AddContentStreamAfter(), resources, pdf);
    }

    /// <summary>
    /// Draws one line anchored at <paramref name="top"/> (top of the line box) and returns the
    /// top of the next line using a 1.2 leading, matching the stacked single-line text blocks
    /// the generators use for headers and footers.
    /// </summary>
    public static float WriteLine(
        PdfCanvas canvas,
        string text,
        PdfFont font,
        float size,
        (float R, float G, float B) color,
        float x,
        float top,
        TextHorizontalAlignment alignment)
    {
        canvas.SaveState();
        canvas.SetFillRgb(color.R, color.G, color.B);
        canvas.ShowTextAligned(font, size, text, x, top - size, alignment);
        canvas.RestoreState();
        return top - size * 1.2f;
    }

    /// <summary>Decodes uploaded logo bytes into an image XObject; null when the bytes can't be decoded.</summary>
    public static PdfImageXObject? TryCreateImage(PdfDocument pdf, byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return PdfImageXObject.Create(pdf, bytes);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// The branded page chrome shared by the estimate and purchase-order PDFs: logo/company block
/// with contact lines on the left, document title block on the right, an accent rule under both,
/// and a centered "company · Page x of y" footer. Stamped onto every page after flow layout,
/// with the flow document's top margin enlarged by <see cref="HeaderHeight"/> to reserve the space.
/// </summary>
internal sealed class BrandedPageChrome(
    string companyName,
    byte[]? logoBytes,
    IReadOnlyList<string> contactLines,
    string title,
    float titleSize,
    string subtitle,
    IReadOnlyList<string> metaLines)
{
    public const float PageMargin = 40f;
    private const float LogoHeight = 56f;
    private const float RuleGap = 10f;
    private const float RuleWidth = 2f;

    private float LeftHeight =>
        (logoBytes is { Length: > 0 }
            ? LogoHeight + (string.IsNullOrWhiteSpace(companyName) ? 0f : 6f + 12f * 1.2f)
            : 20f * 1.2f)
        + contactLines.Count * 9f * 1.2f;

    private float RightHeight => titleSize * 1.2f + 11f * 1.2f + metaLines.Count * 9f * 1.2f;

    /// <summary>Header block height from the top page margin down to and including the accent rule.</summary>
    public float HeaderHeight => Math.Max(LeftHeight, RightHeight) + RuleGap + RuleWidth;

    public void Stamp(PdfDocument pdf)
    {
        var logo = PdfStyle.TryCreateImage(pdf, logoBytes);
        var total = pdf.PageCount;
        for (var i = 0; i < total; i++)
        {
            var page = pdf.Pages[i];
            var canvas = PdfStyle.OverlayCanvas(pdf, page);
            DrawHeader(canvas, page.Size, logo);
            DrawFooter(canvas, page.Size, i + 1, total);
        }
    }

    private void DrawHeader(PdfCanvas canvas, PdfRect pageSize, PdfImageXObject? logo)
    {
        var left = PageMargin;
        var right = pageSize.Width - PageMargin;
        var top = pageSize.Height - PageMargin;

        var yLeft = top;
        if (logo is not null)
        {
            var width = Math.Min(220f, logo.Width * LogoHeight / Math.Max(1, logo.Height));
            canvas.AddImageFittedIntoRectangle(logo, new PdfRect(left, yLeft - LogoHeight, width, LogoHeight));
            yLeft -= LogoHeight;
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                yLeft -= 6f;
                yLeft = PdfStyle.WriteLine(canvas, companyName, StandardFont.HelveticaBold, 12f, PdfStyle.GreyDarken3, left, yLeft, TextHorizontalAlignment.Left);
            }
        }
        else
        {
            yLeft = PdfStyle.WriteLine(canvas, companyName, StandardFont.HelveticaBold, 20f, PdfStyle.Accent, left, yLeft, TextHorizontalAlignment.Left);
        }

        foreach (var line in contactLines)
        {
            yLeft = PdfStyle.WriteLine(canvas, line, StandardFont.Helvetica, 9f, PdfStyle.GreyDarken1, left, yLeft, TextHorizontalAlignment.Left);
        }

        var yRight = top;
        yRight = PdfStyle.WriteLine(canvas, title, StandardFont.HelveticaBold, titleSize, PdfStyle.Accent, right, yRight, TextHorizontalAlignment.Right);
        yRight = PdfStyle.WriteLine(canvas, subtitle, StandardFont.HelveticaBold, 11f, PdfStyle.GreyDarken3, right, yRight, TextHorizontalAlignment.Right);
        foreach (var line in metaLines)
        {
            yRight = PdfStyle.WriteLine(canvas, line, StandardFont.Helvetica, 9f, PdfStyle.GreyDarken3, right, yRight, TextHorizontalAlignment.Right);
        }

        var ruleY = top - Math.Max(LeftHeight, RightHeight) - RuleGap - RuleWidth / 2f;
        canvas.SaveState();
        canvas.SetStrokeRgb(PdfStyle.Accent.R, PdfStyle.Accent.G, PdfStyle.Accent.B);
        canvas.SetLineWidth(RuleWidth);
        canvas.MoveTo(left, ruleY);
        canvas.LineTo(right, ruleY);
        canvas.Stroke();
        canvas.RestoreState();
    }

    private void DrawFooter(PdfCanvas canvas, PdfRect pageSize, int pageNumber, int totalPages)
    {
        PdfStyle.WriteLine(
            canvas,
            $"{companyName}   •   Page {pageNumber} of {totalPages}",
            StandardFont.Helvetica,
            8f,
            PdfStyle.GreyMedium,
            pageSize.Width / 2f,
            34f,
            TextHorizontalAlignment.Center);
    }
}
