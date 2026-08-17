using System.Globalization;
using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Cos;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Web.Pdf;

/// <summary>Facts about the job stamped into the frame's slug line.</summary>
public sealed record ImpositionSlug(string JobNumber, string ProductDescription, DateTime GeneratedAt);

/// <summary>What the imposer found in the source artwork — surfaced on the job page after a run.</summary>
public sealed record ImpositionSourceInfo(
    string Kind,
    decimal TrimWidthIn,
    decimal TrimHeightIn,
    bool HasTrimBox,
    int Rotation,
    bool RotatedToFit,
    int PageCount);

public sealed record ImpositionRenderResult(
    byte[] PdfBytes,
    IReadOnlyList<string> Warnings,
    ImpositionSourceInfo Source);

/// <summary>
/// Steps a product's artwork onto one press frame per the job's <see cref="ImpositionTemplate"/>:
/// the artwork's page (or image) is imported once as a form XObject and placed labels-across ×
/// labels-around, each copy clipped to its trim plus bleed (never past a neighbour's trim), rows
/// starting half a gutter in so frames tile continuously. Optional finishing eye marks, a
/// die-line layer, and a slug line ride in the web margins.
/// </summary>
public class ImpositionPdfGenerator
{
    private const float Pt = 72f;

    /// <summary>How far the artwork's trim may differ from the template's label size before we warn.</summary>
    private const decimal FitToleranceIn = 0.03m;

    /// <summary>Clearance between the label block and an eye mark or the slug.</summary>
    private const float MarginGapPt = 0.0625f * Pt;
    private const float SlugFontSize = 6f;
    private const float DieLineWidthPt = 0.5f;

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".ai" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    public static bool CanImpose(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        return PdfExtensions.Contains(extension) || ImageExtensions.Contains(extension);
    }

    public ImpositionRenderResult Render(byte[] artworkBytes, string artworkFileName, ImpositionTemplate template, ImpositionSlug slug)
    {
        if (artworkBytes.Length == 0)
        {
            throw new InvalidOperationException("The artwork file is empty.");
        }

        if (template.OverflowsWeb)
        {
            throw new InvalidOperationException(
                $"The label block ({template.BlockWidthIn:0.###}\" wide) is wider than the {template.WebWidthIn:0.###}\" web — reduce labels across or the offset.");
        }

        var extension = Path.GetExtension(artworkFileName);
        if (ImageExtensions.Contains(extension))
        {
            return RenderImage(artworkBytes, template, slug);
        }

        if (!PdfExtensions.Contains(extension) && !LooksLikePdf(artworkBytes))
        {
            throw new InvalidOperationException(
                $"'{extension}' artwork can't be imposed — upload the label as a PDF (or PDF-compatible .ai) or a PNG/JPEG.");
        }

        return RenderPdf(artworkBytes, template, slug);
    }

    private static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F';

    // ---- PDF artwork -----------------------------------------------------------------------

    private static ImpositionRenderResult RenderPdf(byte[] artworkBytes, ImpositionTemplate template, ImpositionSlug slug)
    {
        var warnings = new List<string>();
        PdfDocument source;
        try
        {
            source = PdfDocument.Load(artworkBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The artwork could not be read as a PDF ({ex.Message}).", ex);
        }

        using (source)
        {
            if (source.IsEncrypted)
            {
                throw new InvalidOperationException("The artwork PDF is encrypted — save an unprotected copy and upload that.");
            }

            if (source.PageCount == 0)
            {
                throw new InvalidOperationException("The artwork PDF has no pages.");
            }

            if (source.UsedRecoveryScan)
            {
                warnings.Add("The artwork PDF needed repair to open — check the imposed output carefully.");
            }

            if (source.PageCount > 1)
            {
                warnings.Add($"The artwork PDF has {source.PageCount} pages — only page 1 was imposed.");
            }

            var page = source.Pages[0];
            var hasTrimBox = page.HasBox(CosNames.TrimBox);
            if (!hasTrimBox)
            {
                warnings.Add(page.HasBox(CosNames.CropBox)
                    ? "The artwork has no trim box — its crop box was used as the label trim."
                    : "The artwork has no trim box — its page size was used as the label trim.");
            }

            var mediaBox = page.MediaBox;
            var trimBox = page.TrimBox;
            var rotation = page.Rotation;

            // Form space is the source page's own user space and ignores /Rotate — normalise it so
            // the trim box lands upright with its lower-left corner at the origin.
            var upright = UprightMatrix(mediaBox, rotation);
            var uprightTrim = TransformRect(trimBox, upright);
            var normalize = upright.Multiply(PdfMatrix.Translate(-uprightTrim.X, -uprightTrim.Y));
            var trimW = uprightTrim.Width;
            var trimH = uprightTrim.Height;

            var cellW = ToPt(template.PlacedAcrossIn);
            var cellH = ToPt(template.PlacedAroundIn);
            var (fit, rotatedToFit) = FitToCell(trimW, trimH, cellW, cellH, template, warnings);

            using var output = PdfDocument.Create();
            output.SetCreator("Labels MIS");
            output.SetProducer("Labels MIS imposition");
            var frame = new PdfRect(0, 0, ToPt(template.WebWidthIn), ToPt(template.RepeatLengthIn));
            var outPage = output.AddPage(frame);
            outPage.SetTrimBox(frame);

            var importer = new PdfImporter(output);
            var form = importer.ImportPageAsForm(page);
            // The importer's BBox is the crop box, which on label artwork is often the trim — that
            // would clip the bleed we're about to place. Let the form show its whole media box; the
            // per-copy clip below is what bounds each label.
            form.Stream.Put(CosNames.BBox, mediaBox.ToCosArray());
            var group = page.Dictionary.GetRaw(new CosName("Group"));
            if (group is not null)
            {
                form.Stream.Put(new CosName("Group"), importer.Copy(group));
            }

            var canvas = PdfStyle.OverlayCanvas(output, outPage);
            var placement = normalize.Multiply(fit);
            PlaceCopies(canvas, template, (cellX, cellY) =>
                canvas.AddFormXObject(form, placement.Multiply(PdfMatrix.Translate(cellX, cellY))));
            DrawMarks(output, canvas, template, slug, warnings);

            var bytes = output.Save();
            var info = new ImpositionSourceInfo(
                "PDF",
                ToIn(trimW),
                ToIn(trimH),
                hasTrimBox,
                rotation,
                rotatedToFit,
                source.PageCount);
            return new ImpositionRenderResult(bytes, warnings, info);
        }
    }

    // ---- Image artwork ---------------------------------------------------------------------

    private static ImpositionRenderResult RenderImage(byte[] imageBytes, ImpositionTemplate template, ImpositionSlug slug)
    {
        var warnings = new List<string> { "Raster artwork has no bleed — each label is placed at trim size only." };

        using var output = PdfDocument.Create();
        output.SetCreator("Labels MIS");
        output.SetProducer("Labels MIS imposition");
        var frame = new PdfRect(0, 0, ToPt(template.WebWidthIn), ToPt(template.RepeatLengthIn));
        var outPage = output.AddPage(frame);
        outPage.SetTrimBox(frame);

        PdfImageXObject image;
        try
        {
            image = PdfImageXObject.Create(output, imageBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The artwork image could not be decoded ({ex.Message}).", ex);
        }

        var cellW = ToPt(template.PlacedAcrossIn);
        var cellH = ToPt(template.PlacedAroundIn);
        var imageAspect = image.Height == 0 ? 1f : (float)image.Width / image.Height;
        var cellAspect = cellH == 0 ? 1f : cellW / cellH;
        var rotatedToFit = false;
        if (Math.Abs(imageAspect - cellAspect) > 0.02f && Math.Abs(1f / imageAspect - cellAspect) <= 0.02f)
        {
            rotatedToFit = true;
        }
        else if (Math.Abs(imageAspect - cellAspect) > 0.02f)
        {
            warnings.Add($"The image's proportions ({image.Width}×{image.Height} px) don't match the {template.PlacedAcrossIn:0.###}\" × {template.PlacedAroundIn:0.###}\" label — it was stretched to the trim.");
        }

        var canvas = PdfStyle.OverlayCanvas(output, outPage);
        PlaceCopies(canvas, template, (cellX, cellY) =>
        {
            if (rotatedToFit)
            {
                // Rotate 90° CCW so the image's long side runs the label's long side.
                canvas.SaveState();
                canvas.ConcatMatrix(new PdfMatrix(0, 1, -1, 0, cellX + cellW, cellY));
                canvas.AddImageFittedIntoRectangle(image, new PdfRect(0, 0, cellH, cellW));
                canvas.RestoreState();
            }
            else
            {
                canvas.AddImageFittedIntoRectangle(image, new PdfRect(cellX, cellY, cellW, cellH));
            }
        });
        DrawMarks(output, canvas, template, slug, warnings);

        var bytes = output.Save();
        var info = new ImpositionSourceInfo(
            "Image",
            ToIn(rotatedToFit ? cellH : cellW),
            ToIn(rotatedToFit ? cellW : cellH),
            false,
            0,
            rotatedToFit,
            1);
        return new ImpositionRenderResult(bytes, warnings, info);
    }

    // ---- Shared layout ---------------------------------------------------------------------

    /// <summary>Walks the grid, clipping each cell to trim + bleed and invoking the draw callback with the cell origin.</summary>
    private static void PlaceCopies(PdfCanvas canvas, ImpositionTemplate template, Action<float, float> drawAt)
    {
        var cellW = ToPt(template.PlacedAcrossIn);
        var cellH = ToPt(template.PlacedAroundIn);
        var pitchX = ToPt(template.PitchAcrossIn);
        var pitchY = ToPt(template.PitchAroundIn);
        var startX = ToPt(template.LeftMarginIn);
        var startY = ToPt(template.GutterAroundIn) / 2f;
        var bleed = ToPt(template.BleedIn);
        // Bleed between neighbours is capped at the gutter so no copy paints over the next label's trim.
        var innerBleedX = Math.Min(bleed, ToPt(template.GutterAcrossIn));
        var innerBleedY = Math.Min(bleed, ToPt(template.GutterAroundIn));

        for (var row = 0; row < template.LabelsAround; row++)
        {
            var cellY = startY + row * pitchY;
            for (var col = 0; col < template.LabelsAcross; col++)
            {
                var cellX = startX + col * pitchX;
                var left = col == 0 ? bleed : innerBleedX;
                var right = col == template.LabelsAcross - 1 ? bleed : innerBleedX;

                canvas.SaveState();
                canvas.Rectangle(cellX - left, cellY - innerBleedY, cellW + left + right, cellH + 2 * innerBleedY);
                canvas.Clip();
                canvas.EndPath();
                drawAt(cellX, cellY);
                canvas.RestoreState();
            }
        }
    }

    private static void DrawMarks(PdfDocument output, PdfCanvas canvas, ImpositionTemplate template, ImpositionSlug slug, List<string> warnings)
    {
        var frameW = ToPt(template.WebWidthIn);
        var frameH = ToPt(template.RepeatLengthIn);
        var blockLeft = ToPt(template.LeftMarginIn);
        var blockRight = blockLeft + ToPt(template.BlockWidthIn);
        var leftMargin = blockLeft;
        var rightMargin = frameW - blockRight;
        var cellW = ToPt(template.PlacedAcrossIn);
        var cellH = ToPt(template.PlacedAroundIn);
        var pitchY = ToPt(template.PitchAroundIn);
        var startY = ToPt(template.GutterAroundIn) / 2f;

        // Eye marks: one per label row, in the margin, hugging the block.
        var markW = ToPt(template.EyeMarkWidthIn);
        var markH = ToPt(template.EyeMarkHeightIn);
        var wantLeftMark = template.EyeMarks is ImpositionMarkSide.Left or ImpositionMarkSide.Both;
        var wantRightMark = template.EyeMarks is ImpositionMarkSide.Right or ImpositionMarkSide.Both;
        var leftMarkFits = leftMargin >= markW + MarginGapPt;
        var rightMarkFits = rightMargin >= markW + MarginGapPt;
        if (wantLeftMark && !leftMarkFits)
        {
            warnings.Add($"No room for eye marks in the left margin ({ToIn(leftMargin):0.###}\") — they were skipped.");
        }

        if (wantRightMark && !rightMarkFits)
        {
            warnings.Add($"No room for eye marks in the right margin ({ToIn(rightMargin):0.###}\") — they were skipped.");
        }

        var drawLeftMark = wantLeftMark && leftMarkFits;
        var drawRightMark = wantRightMark && rightMarkFits;
        if (drawLeftMark || drawRightMark)
        {
            canvas.SaveState();
            canvas.SetFillCmyk(0, 0, 0, 1);
            for (var row = 0; row < template.LabelsAround; row++)
            {
                var y = startY + row * pitchY;
                if (drawLeftMark)
                {
                    canvas.Rectangle(blockLeft - MarginGapPt - markW, y, markW, markH);
                    canvas.Fill();
                }

                if (drawRightMark)
                {
                    canvas.Rectangle(blockRight + MarginGapPt, y, markW, markH);
                    canvas.Fill();
                }
            }

            canvas.RestoreState();
        }

        // Die lines: the trim outline of every label as a "CutContour" spot colour on its own layer.
        if (template.IncludeDieLines)
        {
            var layer = PdfOptionalContentGroup.Create(output, "Die lines");
            var cutContour = PdfSeparationColor.Create(output, "CutContour", 0f, 1f, 0f, 0f);
            var radius = Math.Min(ToPt(template.CornerRadiusIn), Math.Min(cellW, cellH) / 2f);
            var pitchX = ToPt(template.PitchAcrossIn);
            canvas.SaveState();
            canvas.BeginLayer(layer);
            canvas.SetStrokeSeparation(cutContour);
            canvas.SetLineWidth(DieLineWidthPt);
            for (var row = 0; row < template.LabelsAround; row++)
            {
                for (var col = 0; col < template.LabelsAcross; col++)
                {
                    var x = blockLeft + col * pitchX;
                    var y = startY + row * pitchY;
                    StrokeRoundedRect(canvas, x, y, cellW, cellH, radius);
                }
            }

            canvas.EndLayer();
            canvas.RestoreState();
        }

        // Slug: job facts along the web edge, in whichever margin has room (left first, then right).
        if (template.IncludeSlug)
        {
            var text = BuildSlugText(template, slug);
            var textWidth = StandardFont.Helvetica.GetWidth(text, SlugFontSize);
            var maxLength = frameH - 2 * MarginGapPt;
            if (textWidth > maxLength)
            {
                text = FitSlugText(text, maxLength);
            }

            var needed = SlugFontSize + MarginGapPt + (drawLeftMark ? markW + MarginGapPt : 0f);
            float? x = null;
            if (leftMargin >= needed)
            {
                // Rotated 90° CCW the glyphs' "up" points to −x, so the baseline sits at the outer side.
                x = MarginGapPt + SlugFontSize * 0.2f;
                if (drawLeftMark && blockLeft - MarginGapPt - markW < x + SlugFontSize)
                {
                    x = null;
                }
            }

            var neededRight = SlugFontSize + MarginGapPt + (drawRightMark ? markW + MarginGapPt : 0f);
            if (x is null && rightMargin >= neededRight)
            {
                x = frameW - MarginGapPt - SlugFontSize * 0.8f;
            }

            if (x is { } slugX)
            {
                canvas.SaveState();
                canvas.SetFillCmyk(0, 0, 0, 1);
                canvas.ShowTextAligned(StandardFont.Helvetica, SlugFontSize, text, slugX, MarginGapPt, TextHorizontalAlignment.Left, rotationRadians: MathF.PI / 2f);
                canvas.RestoreState();
            }
            else
            {
                warnings.Add("No room for the slug line in either web margin — it was skipped.");
            }
        }
    }

    private static string BuildSlugText(ImpositionTemplate template, ImpositionSlug slug)
    {
        var description = slug.ProductDescription.Trim();
        return string.Join("  ·  ", new[]
        {
            slug.JobNumber,
            description,
            $"{template.LabelsAcross} across x {template.LabelsAround} around",
            $"{template.PlacedAcrossIn.ToString("0.###", CultureInfo.InvariantCulture)} x {template.PlacedAroundIn.ToString("0.###", CultureInfo.InvariantCulture)} in",
            $"repeat {template.RepeatLengthIn.ToString("0.###", CultureInfo.InvariantCulture)} in",
            $"web {template.WebWidthIn.ToString("0.###", CultureInfo.InvariantCulture)} in",
            slug.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string FitSlugText(string text, float maxWidth)
    {
        var font = StandardFont.Helvetica;
        while (text.Length > 8 && font.GetWidth(text + "…", SlugFontSize) > maxWidth)
        {
            text = text[..^1];
        }

        return text + "…";
    }

    /// <summary>Strokes a rounded rectangle; the canvas has no curve op so the arcs are raw Béziers.</summary>
    private static void StrokeRoundedRect(PdfCanvas canvas, float x, float y, float w, float h, float r)
    {
        if (r <= 0.01f)
        {
            canvas.Rectangle(x, y, w, h);
            canvas.Stroke();
            return;
        }

        const float kappa = 0.5522847f;
        var k = r * kappa;
        string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        var ops =
            $"{F(x + r)} {F(y)} m\n" +
            $"{F(x + w - r)} {F(y)} l\n" +
            $"{F(x + w - r + k)} {F(y)} {F(x + w)} {F(y + r - k)} {F(x + w)} {F(y + r)} c\n" +
            $"{F(x + w)} {F(y + h - r)} l\n" +
            $"{F(x + w)} {F(y + h - r + k)} {F(x + w - r + k)} {F(y + h)} {F(x + w - r)} {F(y + h)} c\n" +
            $"{F(x + r)} {F(y + h)} l\n" +
            $"{F(x + r - k)} {F(y + h)} {F(x)} {F(y + h - r + k)} {F(x)} {F(y + h - r)} c\n" +
            $"{F(x)} {F(y + r)} l\n" +
            $"{F(x)} {F(y + r - k)} {F(x + r - k)} {F(y)} {F(x + r)} {F(y)} c\n" +
            "h S\n";
        canvas.WriteRaw(ops);
    }

    /// <summary>
    /// Decides how the upright artwork trim maps onto the label cell: as-is when it matches, rotated
    /// 90° when the swapped dimensions match, otherwise unscaled and centred (with a warning). Returns
    /// the matrix that takes the upright trim (origin at its lower-left) into cell space.
    /// </summary>
    private static (PdfMatrix Fit, bool Rotated) FitToCell(float trimW, float trimH, float cellW, float cellH, ImpositionTemplate template, List<string> warnings)
    {
        var tolerance = ToPt(FitToleranceIn);
        var matchesAsIs = Math.Abs(trimW - cellW) <= tolerance && Math.Abs(trimH - cellH) <= tolerance;
        var matchesRotated = Math.Abs(trimW - cellH) <= tolerance && Math.Abs(trimH - cellW) <= tolerance;

        if (matchesAsIs)
        {
            return (PdfMatrix.Translate((cellW - trimW) / 2f, (cellH - trimH) / 2f), false);
        }

        if (matchesRotated)
        {
            // 90° CCW: (x, y) → (trimH − y, x), so the rotated trim occupies [0, trimH] × [0, trimW].
            var rotate = new PdfMatrix(0, 1, -1, 0, trimH, 0);
            return (rotate.Multiply(PdfMatrix.Translate((cellW - trimH) / 2f, (cellH - trimW) / 2f)), true);
        }

        warnings.Add(
            $"The artwork trim is {ToIn(trimW):0.###}\" × {ToIn(trimH):0.###}\" but the label is {template.PlacedAcrossIn:0.###}\" × {template.PlacedAroundIn:0.###}\" — placed unscaled and centred in each label; check the template or the artwork.");
        return (PdfMatrix.Translate((cellW - trimW) / 2f, (cellH - trimH) / 2f), false);
    }

    /// <summary>Maps source page space to an upright page (honouring /Rotate) whose media box starts at the origin.</summary>
    private static PdfMatrix UprightMatrix(PdfRect mediaBox, int rotation) => rotation switch
    {
        90 => new PdfMatrix(0, -1, 1, 0, -mediaBox.Y, mediaBox.Right),
        180 => new PdfMatrix(-1, 0, 0, -1, mediaBox.Right, mediaBox.Top),
        270 => new PdfMatrix(0, 1, -1, 0, mediaBox.Top, -mediaBox.X),
        _ => PdfMatrix.Translate(-mediaBox.X, -mediaBox.Y)
    };

    private static PdfRect TransformRect(PdfRect rect, PdfMatrix matrix)
    {
        var (x1, y1) = matrix.Transform(rect.Left, rect.Bottom);
        var (x2, y2) = matrix.Transform(rect.Right, rect.Top);
        var (x3, y3) = matrix.Transform(rect.Left, rect.Top);
        var (x4, y4) = matrix.Transform(rect.Right, rect.Bottom);
        var minX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        var maxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
        var minY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        var maxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
        return new PdfRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static float ToPt(decimal inches) => (float)(inches * (decimal)Pt);
    private static decimal ToIn(float points) => Math.Round((decimal)points / (decimal)Pt, 4);
}
