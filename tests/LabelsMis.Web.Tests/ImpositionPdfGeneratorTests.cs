using FluentAssertions;
using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Cos;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Web.Pdf;

namespace LabelsMis.Web.Tests;

/// <summary>
/// Steps a synthetic label artwork onto a frame and checks the geometry that matters on press:
/// frame size = web × repeat, one shared form XObject for every copy, and the source's trim/bleed
/// and rotation handled. Set PDF_SMOKE_OUT to write the frames out for a visual check.
/// </summary>
public class ImpositionPdfGeneratorTests
{
    private static readonly DateTime Now = new(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc);
    private static readonly ImpositionSlug Slug = new("JOB-2026-00042", "Widget Co · 4 x 3 rectangle", Now);

    private static ImpositionTemplate Template(int across = 3, int around = 12, LabelOrientation orientation = LabelOrientation.AsEntered) =>
        ImpositionTemplate.Create(
            labelAcrossIn: 4m, labelAroundIn: 3m, cornerRadiusIn: 0.125m,
            gutterAcrossIn: 0.125m, gutterAroundIn: 0.125m, bleedIn: 0.0625m,
            labelsAcross: across, labelsAround: around, orientation: orientation,
            webWidthIn: 13m, crossWebOffsetIn: 0m,
            eyeMarks: ImpositionMarkSide.Left, includeDieLines: true, includeSlug: true);

    [Fact]
    public void Template_geometry_follows_the_grid()
    {
        var t = Template();

        t.BlockWidthIn.Should().Be(3 * 4m + 2 * 0.125m);
        t.RepeatLengthIn.Should().Be(12 * 3.125m);
        t.LabelsPerFrame.Should().Be(36);
        t.LeftMarginIn.Should().Be((13m - 12.25m) / 2m);
        t.OverflowsWeb.Should().BeFalse();
        Template(across: 4).OverflowsWeb.Should().BeTrue("4 × 4\" plus gutters is wider than 13\"");
    }

    [Fact]
    public void Rotated_template_swaps_the_placed_dimensions()
    {
        var t = Template(across: 4, around: 9, orientation: LabelOrientation.Rotated);

        t.PlacedAcrossIn.Should().Be(3m);
        t.PlacedAroundIn.Should().Be(4m);
        t.BlockWidthIn.Should().Be(4 * 3m + 3 * 0.125m);
        t.RepeatLengthIn.Should().Be(9 * 4.125m);
    }

    [Fact]
    public void Imposes_pdf_artwork_onto_one_frame_sharing_a_single_form()
    {
        var artwork = SyntheticLabel(trimWidthIn: 4f, trimHeightIn: 3f, bleedIn: 0.0625f, rotate: 0);
        var template = Template();

        var result = new ImpositionPdfGenerator().Render(artwork, "label.pdf", template, Slug);

        AssertFrame(result.PdfBytes, template, "imposition-3x12.pdf");
        result.Warnings.Should().BeEmpty();
        result.Source.TrimWidthIn.Should().Be(4m);
        result.Source.TrimHeightIn.Should().Be(3m);
        result.Source.HasTrimBox.Should().BeTrue();
        result.Source.RotatedToFit.Should().BeFalse();

        using var pdf = PdfDocument.Load(result.PdfBytes);
        var xobjects = pdf.Pages[0].Resources!.GetAsDictionary(CosNames.XObject)!;
        xobjects.Count.Should().Be(1, "every copy reuses the one imported form");
        var content = System.Text.Encoding.ASCII.GetString(pdf.Pages[0].GetContentBytes());
        CountOccurrences(content, " Do").Should().Be(template.LabelsPerFrame);
        var spots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, colorSpace) in pdf.Pages[0].Resources!.GetAsDictionary(CosNames.ColorSpace)!)
        {
            PdfInspection.CollectSpotColorNames(colorSpace, spots);
        }

        spots.Should().Contain("CutContour", "die lines are stroked in the CutContour separation");
    }

    [Fact]
    public void Rotates_artwork_whose_trim_matches_the_label_sideways()
    {
        // 3 wide × 4 tall artwork for a 4 across × 3 around label: rotated to fit, no warning.
        var artwork = SyntheticLabel(trimWidthIn: 3f, trimHeightIn: 4f, bleedIn: 0.0625f, rotate: 0);

        var result = new ImpositionPdfGenerator().Render(artwork, "label-portrait.pdf", Template(), Slug);

        result.Source.RotatedToFit.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        AssertFrame(result.PdfBytes, Template(), "imposition-rotated-source.pdf");
    }

    [Fact]
    public void Honours_the_source_page_rotate_entry()
    {
        // The page is stored 3 × 4 but displayed with /Rotate 90, i.e. as a 4 × 3 label.
        var artwork = SyntheticLabel(trimWidthIn: 3f, trimHeightIn: 4f, bleedIn: 0.0625f, rotate: 90);

        var result = new ImpositionPdfGenerator().Render(artwork, "label-rotate90.pdf", Template(), Slug);

        result.Source.Rotation.Should().Be(90);
        result.Source.TrimWidthIn.Should().Be(4m);
        result.Source.TrimHeightIn.Should().Be(3m);
        result.Source.RotatedToFit.Should().BeFalse();
        result.Warnings.Should().BeEmpty();
        AssertFrame(result.PdfBytes, Template(), "imposition-rotate90-source.pdf");
    }

    [Fact]
    public void Warns_when_the_artwork_trim_does_not_match_the_label()
    {
        var artwork = SyntheticLabel(trimWidthIn: 3.5f, trimHeightIn: 2.5f, bleedIn: 0f, rotate: 0);

        var result = new ImpositionPdfGenerator().Render(artwork, "label-small.pdf", Template(), Slug);

        result.Warnings.Should().ContainSingle(w => w.Contains("placed unscaled"));
        AssertFrame(result.PdfBytes, Template(), "imposition-mismatch.pdf");
    }

    [Fact]
    public void Imposes_raster_artwork_at_trim_size()
    {
        var template = Template(across: 2, around: 4);

        var result = new ImpositionPdfGenerator().Render(TinyPng, "label.png", template, Slug);

        result.Source.Kind.Should().Be("Image");
        result.Warnings.Should().Contain(w => w.Contains("no bleed"));
        AssertFrame(result.PdfBytes, template, "imposition-image.pdf");
    }

    [Fact]
    public void Rejects_a_block_wider_than_the_web()
    {
        var artwork = SyntheticLabel(4f, 3f, 0.0625f, 0);
        var act = () => new ImpositionPdfGenerator().Render(artwork, "label.pdf", Template(across: 4), Slug);
        act.Should().Throw<InvalidOperationException>().WithMessage("*wider than the 13*");
    }

    [Fact]
    public void Rejects_files_that_are_not_pdf_or_image()
    {
        var act = () => new ImpositionPdfGenerator().Render("%!PS-Adobe-3.0"u8.ToArray(), "label.eps", Template(), Slug);
        act.Should().Throw<InvalidOperationException>().WithMessage("*can't be imposed*");
    }

    private static void AssertFrame(byte[] bytes, ImpositionTemplate template, string fileName)
    {
        bytes.Should().NotBeEmpty();
        bytes.Take(5).Should().Equal("%PDF-"u8.ToArray());
        using var pdf = PdfDocument.Load(bytes);
        pdf.PageCount.Should().Be(1);
        pdf.UsedRecoveryScan.Should().BeFalse();
        var size = pdf.Pages[0].Size;
        size.Width.Should().BeApproximately((float)(template.WebWidthIn * 72m), 0.01f);
        size.Height.Should().BeApproximately((float)(template.RepeatLengthIn * 72m), 0.01f);

        var outDir = Environment.GetEnvironmentVariable("PDF_SMOKE_OUT");
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, fileName), bytes);
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    /// <summary>A one-page label: media box = trim + bleed, a filled bleed area, a distinct trim area,
    /// a corner marker so orientation is visible, and TrimBox/BleedBox set. Optionally stored with /Rotate.</summary>
    private static byte[] SyntheticLabel(float trimWidthIn, float trimHeightIn, float bleedIn, int rotate)
    {
        const float pt = 72f;
        var bleed = bleedIn * pt;
        var trimW = trimWidthIn * pt;
        var trimH = trimHeightIn * pt;
        // Offset media box origin to make sure the imposer re-origins the trim box.
        var origin = new PdfRect(20f, 30f, trimW + 2 * bleed, trimH + 2 * bleed);

        using var pdf = PdfDocument.Create();
        var page = pdf.AddPage(origin);
        var trim = new PdfRect(origin.X + bleed, origin.Y + bleed, trimW, trimH);
        page.SetTrimBox(trim);
        page.SetBleedBox(origin);
        page.SetCropBox(trim); // the common "crop = trim" export that would otherwise clip the bleed
        if (rotate != 0)
        {
            page.Dictionary.Put(CosNames.Rotate, new CosNumber(rotate));
        }

        var resources = new CosDictionary();
        page.Dictionary.Put(CosNames.Resources, resources);
        var canvas = new PdfCanvas(page.AddContentStreamAfter(), resources, pdf);
        // Bleed area: light red; trim: pale yellow; a blue corner block at the trim's lower-left.
        canvas.SetFillRgb(1f, 0.75f, 0.75f).Rectangle(origin).Fill();
        canvas.SetFillRgb(1f, 0.98f, 0.8f).Rectangle(trim).Fill();
        canvas.SetFillRgb(0.2f, 0.4f, 0.9f).Rectangle(trim.X, trim.Y, 0.5f * pt, 0.5f * pt).Fill();
        canvas.SetStrokeGray(0.3).SetLineWidth(0.5).Rectangle(trim).Stroke();
        canvas.SetFillGray(0.1);
        canvas.ShowTextAligned(StandardFont.HelveticaBold, 14f, $"{trimWidthIn:0.##} x {trimHeightIn:0.##}", trim.X + trimW / 2f, trim.Y + trimH / 2f, TextHorizontalAlignment.Center);
        return pdf.Save();
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
