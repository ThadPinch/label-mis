using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Pdf;

/// <summary>
/// The "job-first" ticket (v2), printed beside <see cref="JobTicketPdfGenerator"/> (v1) while the
/// floor tries it. Same data as v1; what changes is where it goes:
/// each job is one block kept on one page, with its own notes directly under its run strip, the
/// order's notes stay at the top (capped, the rest continues after the last job), and rows that have
/// nothing to say are left out. Deliberately self-contained so v1 can be deleted in one piece.
/// </summary>
public class JobTicketV2PdfGenerator(IOptions<JobOptions> options, GeneralSettingsService generalSettings)
{
    private const float Margin = 28f;
    private const float LogoHeight = 36f;
    private const float BoxWidth = 200f;
    private const float BoxPadding = 8f;
    private const float NumberLineHeight = 16f * 1.2f;
    private const float BarcodeHeight = 24f;
    private const float BarcodeGap = 2f;
    private const float JobBarcodeHeight = 14f;
    private const float BoxHeight = BoxPadding * 2f + NumberLineHeight + BarcodeGap + BarcodeHeight;
    private const float BannerHeight = 33.6f; // 6 pad + 7pt label line + 11pt value line + 6 pad
    private const float LeftBlockHeight = 15f * 1.2f + 11f * 1.2f;
    private const float HeaderHeight = BoxHeight + 8f + BannerHeight;
    private const float ContentWidth = 612f - Margin * 2f;

    /// <summary>Order-note lines printed at the top before the rest moves behind the last job.</summary>
    public const int OrderNoteLineCap = 10;

    private const float NoteSize = 9.5f;
    private const float OrderNoteSize = 9f;
    private const float SectionPadding = 6f;

    private static readonly (float R, float G, float B) Frame = PdfStyle.Black;

    public async Task<byte[]> GenerateAsync(
        JobTicketDetail detail,
        IReadOnlyList<JobTicketDetail>? orderJobs = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var branding = await generalSettings.GetAsync(cancellationToken);
        var companyName = !string.IsNullOrWhiteSpace(branding?.CompanyName) ? branding!.CompanyName : settings.ShopName;
        var logoBytes = branding is { HasLogo: true } ? branding.LogoBytes : null;

        var jobs = orderJobs is { Count: > 0 } ? orderJobs : [detail];
        return await Task.Run(() => Render(detail, jobs, companyName, logoBytes), cancellationToken);
    }

    private static byte[] Render(JobTicketDetail detail, IReadOnlyList<JobTicketDetail> orderJobs, string shopName, byte[]? logoBytes)
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(
            pdf, PdfRect.Letter,
            Margin + HeaderHeight + 10f,
            Margin,
            Margin + 14f,
            Margin);

        ComposeContent(document, detail, orderJobs);
        StampChrome(pdf, detail, orderJobs, shopName, logoBytes);
        return pdf.Save();
    }

    private static void ComposeContent(FlowDocument document, JobTicketDetail detail, IReadOnlyList<JobTicketDetail> orderJobs)
    {
        var continuation = AddOrderNotes(document, detail);

        // Non-label charges on the order (die creation, design): informational only, no job exists.
        if (detail.OrderCharges is { Count: > 0 } orderCharges)
        {
            var chargesBody = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = SectionPadding };
            chargesBody.Add(Text(string.Join("   ·   ", orderCharges), StandardFont.HelveticaBold, 9f));
            chargesBody.Add(Text(
                "Billed with this order. No production job is scheduled for these items.",
                size: 7.5f, color: PdfStyle.GreyDarken2));
            document.Add(Section("ORDER CHARGES (NOT PRODUCED)", chargesBody));
        }

        var orderDue = OrderDue(orderJobs);
        foreach (var job in orderJobs)
        {
            document.Add(JobBlock(job, orderDue));
        }

        if (continuation is not null)
        {
            var body = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = SectionPadding };
            body.Add(Text(continuation, size: OrderNoteSize));
            document.Add(Section("ORDER NOTES, CONTINUED", body));
        }
    }

    // ---- Order notes ---------------------------------------------------------------------------

    /// <summary>Adds the ORDER NOTES section when there is anything to print. Returns the part of the
    /// order note that did not fit under the cap, to be printed after the last job.</summary>
    private static string? AddOrderNotes(FlowDocument document, JobTicketDetail detail)
    {
        var orderNotes = string.IsNullOrWhiteSpace(detail.OrderNotes) ? null : detail.OrderNotes.Trim();
        var standing = string.IsNullOrWhiteSpace(detail.CustomerNotes) ? null : detail.CustomerNotes.Trim();
        if (orderNotes is null && standing is null)
        {
            return null;
        }

        // Side by side only when both exist and the standing notes stay short in the narrow column.
        float[] split = [1.7f, 1f];
        var narrow = ContentWidth * split[1] / split.Sum() - SectionPadding * 2f;
        var sideBySide = orderNotes is not null && standing is not null
            && PdfCanvasTextExtensions.WrapLines(StandardFont.HelveticaBold, OrderNoteSize, standing, narrow).Count <= 6;

        var orderWidth = (sideBySide ? ContentWidth * split[0] / split.Sum() : ContentWidth) - SectionPadding * 2f;
        string? continuation = null;
        var orderCell = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = SectionPadding };
        if (orderNotes is not null)
        {
            var (shown, rest) = CapLines(orderNotes, StandardFont.Helvetica, OrderNoteSize, orderWidth, OrderNoteLineCap);
            continuation = rest;
            orderCell.Add(Caption("THIS ORDER"));
            orderCell.Add(Text(shown, size: OrderNoteSize));
            if (rest is not null)
            {
                orderCell.Add(Text("More order notes after the last job.", StandardFont.HelveticaBold, 7.5f));
            }
        }

        var standingCell = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = SectionPadding };
        if (standing is not null)
        {
            standingCell.Add(Caption("ALWAYS FOR THIS CUSTOMER"));
            standingCell.Add(Text(standing, StandardFont.HelveticaBold, OrderNoteSize));
        }

        var table = new FlowTable(sideBySide ? split : [1f]) { KeepTogether = true, MarginBottom = 8f };
        table.AddHeaderCell(SectionTitleCell("ORDER NOTES", sideBySide ? 2 : 1));
        if (orderNotes is not null)
        {
            table.AddCell(orderCell);
        }

        if (standing is not null)
        {
            table.AddCell(standingCell);
        }

        document.Add(table);
        return continuation;
    }

    /// <summary>Splits <paramref name="text"/> after <paramref name="cap"/> wrapped lines. The remainder
    /// keeps its own paragraph breaks; the paragraph the cut lands in is re-joined so it re-wraps.</summary>
    public static (string Shown, string? Remainder) CapLines(string text, PdfFont font, float size, float width, int cap)
    {
        var shown = new List<string>();
        var rest = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (rest.Count > 0)
            {
                rest.Add(paragraph);
                continue;
            }

            var lines = PdfCanvasTextExtensions.WrapLines(font, size, paragraph, width);
            var room = cap - shown.Count;
            if (lines.Count <= room)
            {
                shown.AddRange(lines);
                continue;
            }

            shown.AddRange(lines.Take(room));
            rest.Add(string.Join(" ", lines.Skip(room)));
        }

        var remainder = string.Join("\n", rest).Trim();
        return (string.Join("\n", shown).TrimEnd(), remainder.Length == 0 ? null : remainder);
    }

    // ---- Job block -----------------------------------------------------------------------------

    private static FlowTable JobBlock(JobTicketDetail job, DateOnly? orderDue)
    {
        var table = new FlowTable([1f]) { KeepTogether = true, MarginBottom = 10f };

        // Title bar. A header row, so it repeats if a block taller than a page has to split.
        var title = new FlowTable([3.6f, 1f]);
        var titleText = new FlowCell { NoBorder = true, Padding = 0f };
        titleText.Add(Text(job.Job.JobNumber, StandardFont.HelveticaBold, 12f));
        titleText.Add(Text($"{job.ProductDescription}   ·   {job.ProductSku}", size: 8.5f));
        title.AddCell(titleText);
        var barcode = new FlowBarcode(job.Job.JobNumber, JobBarcodeHeight, maxWidth: 110f) { MarginTop = 5f };
        title.AddCell(new FlowCell { NoBorder = true, Padding = 0f }.Add(barcode));
        table.AddHeaderCell(new FlowCell { Background = PdfStyle.GreyLighten2, Border = (Frame, 1f), Padding = 5f }.Add(title));

        table.AddCell(Framed(RunStrip(job, orderDue)));

        var notes = JobTicketNotes.Build(job.LineNotes, job.Job.Notes, job.Job.QuantityOrdered);
        if (!notes.IsEmpty)
        {
            table.AddCell(Framed(RunNotes(notes)));
        }

        if (SpecColumns(job) is { } specs)
        {
            table.AddCell(Framed(specs));
        }

        table.AddCell(Framed(RouteTable(job)));
        table.AddCell(new FlowCell { Border = (Frame, 1f), Padding = 4f, PaddingBottom = 24f }.Add(Caption("FLOOR NOTES")));
        return table;
    }

    private static FlowCell Framed(FlowElement content) =>
        new FlowCell { Border = (Frame, 1f), Padding = 0f }.Add(content);

    /// <summary>The facts needed to start the job, in large type.</summary>
    private static FlowTable RunStrip(JobTicketDetail job, DateOnly? orderDue)
    {
        var cells = new List<(float Weight, string Label, string Value, string? Sub)>
        {
            (1f, "QTY", $"{job.Job.QuantityOrdered:N0}", null)
        };

        if (job.Job.IsOutsourced && job.Outsource is { } outsource)
        {
            cells.Add((2.2f, "OUTSOURCED TO", outsource.VendorName ?? "Vendor not set",
                string.IsNullOrWhiteSpace(outsource.QuoteNumber) ? null : $"Quote {outsource.QuoteNumber}"));
            cells.Add((1.2f, "EXPECTED IN", outsource.ExpectedIn?.ToString("MMM d") ?? "—", null));
            cells.Add((1.6f, "VENDOR STATUS", VendorStatus(outsource), "Inspect · Pack · Ship"));
        }
        else
        {
            var cornerRadius = job.Job.Spec?.CornerRadiusIn ?? 0m;
            cells.Add((1.3f, "LABEL", $"{job.LabelAcrossIn:0.####}\" × {job.LabelAroundIn:0.####}\"",
                cornerRadius > 0m ? $"{cornerRadius:0.####}\" corner radius" : null));

            var (stockCode, stockName) = SplitCode(job.SubstrateDescription);
            cells.Add((2.2f, "STOCK", stockCode, stockName));
            cells.Add((1.1f, "INK", job.InkSet.ToString().Replace('_', ' '), job.SpecialInksSummary));

            if (!string.IsNullOrWhiteSpace(job.Die?.Location))
            {
                cells.Add((1.1f, "DIE LOCATION", job.Die!.Location!, job.DieDescription ?? job.Die.Description));
            }
            else if ((job.DieDescription ?? job.Die?.Description) is { Length: > 0 } dieDescription)
            {
                cells.Add((1.4f, "DIE", dieDescription, null));
            }
        }

        // The banner carries the order's earliest due date; say so on a job that is due later.
        if (job.Job.DueDate is { } due && due != orderDue)
        {
            cells.Add((0.9f, "DUE", due.ToString("MMM d"), null));
        }

        var weights = cells.Select(c => c.Weight).ToArray();
        var strip = new FlowTable(weights);
        for (var i = 0; i < cells.Count; i++)
        {
            var (_, label, value, sub) = cells[i];
            const float padding = 4f;
            var width = ContentWidth * weights[i] / weights.Sum() - padding * 2f;
            var cell = new FlowCell { Border = (PdfStyle.GreyMedium, 0.5f), Padding = padding };
            cell.Add(Caption(label));
            cell.Add(Text(value, StandardFont.HelveticaBold, FitSize(value, StandardFont.HelveticaBold, width, 13f, 11f, 9.5f, 8.5f), leading: 1.15f));
            if (!string.IsNullOrWhiteSpace(sub))
            {
                cell.Add(Text(sub!, size: 7.5f));
            }

            strip.AddCell(cell);
        }

        return strip;
    }

    /// <summary>The job's notes across the full width, right under the run strip.</summary>
    private static FlowTable RunNotes(TicketRunNotes notes)
    {
        var table = new FlowTable([52f, ContentWidth - 52f]);
        table.AddCell(new FlowCell { Background = PdfStyle.Black, NoBorder = true, Padding = 5f }
            .Add(Text("RUN\nNOTES", StandardFont.HelveticaBold, 7.5f, PdfStyle.White)));

        var body = new FlowCell { NoBorder = true, Padding = 6f };
        if (notes.Versions.Count > 0)
        {
            var versions = new FlowTable([3.4f, 1f, 0.8f]);
            versions.AddCell(VersionHeader("VERSION"));
            versions.AddCell(VersionHeader("QTY"));
            versions.AddCell(VersionHeader("DONE"));
            foreach (var version in notes.Versions)
            {
                versions.AddCell(VersionCell(Text(version.Name, size: NoteSize)));
                var quantity = Text($"{version.Quantity:N0}", StandardFont.HelveticaBold, NoteSize);
                quantity.Alignment = TextHorizontalAlignment.Right;
                versions.AddCell(VersionCell(quantity));
                versions.AddCell(VersionCell(Text(" ", size: NoteSize)));
            }

            // Two thirds of the width, so a quantity sits near its name.
            var holder = new FlowTable([2f, 1f]) { MarginBottom = notes.LineText is null && notes.JobText is null ? 0f : 4f };
            holder.AddCell(new FlowCell { NoBorder = true, Padding = 0f }.Add(versions));
            holder.AddCell(new FlowCell { NoBorder = true, Padding = 0f });
            body.Add(holder);
        }

        if (notes.LineText is not null)
        {
            body.Add(Text(notes.LineText, size: NoteSize));
        }

        if (notes.JobText is not null)
        {
            var caption = Caption("JOB NOTE");
            caption.MarginTop = notes.LineText is null ? 0f : 4f;
            body.Add(caption);
            body.Add(Text(notes.JobText, size: NoteSize));
        }

        table.AddCell(body);
        return table;
    }

    private static FlowCell VersionHeader(string text) =>
        new FlowCell { Background = PdfStyle.GreyLighten4, Border = (PdfStyle.GreyMedium, 0.5f), Padding = 2.5f }
            .Add(Text(text, size: 6.5f, color: PdfStyle.GreyDarken2));

    private static FlowCell VersionCell(FlowParagraph content) =>
        new FlowCell { Border = (PdfStyle.GreyMedium, 0.5f), Padding = 3f }.Add(content);

    /// <summary>Specs grouped by station. Empty rows are skipped; an empty column is dropped.</summary>
    private static FlowTable? SpecColumns(JobTicketDetail job)
    {
        var columns = new List<(string Title, List<(string Label, string Value)> Rows)>();
        if (job.Job.IsOutsourced && job.Outsource is not null)
        {
            columns.Add(("AS ORDERED", OrderedRows(job)));
        }
        else
        {
            columns.Add(("PRESS", PressRows(job)));
            columns.Add(("FINISHING & DIE", FinishingRows(job)));
        }

        columns.RemoveAll(c => c.Rows.Count == 0);
        if (columns.Count == 0)
        {
            return null;
        }

        var table = new FlowTable(columns.Select(_ => 1f).ToArray());
        foreach (var (title, rows) in columns)
        {
            var grid = new FlowTable([62f, ContentWidth / columns.Count - 62f - 10f]);
            foreach (var (label, value) in rows)
            {
                grid.AddCell(new FlowCell { NoBorder = true, Padding = 1.2f }.Add(Text(label, size: 7.5f, color: PdfStyle.GreyDarken2)));
                grid.AddCell(new FlowCell { NoBorder = true, Padding = 1.2f }.Add(Text(value, StandardFont.HelveticaBold, 8.5f)));
            }

            var heading = Text(title, StandardFont.HelveticaBold, 7f);
            heading.MarginBottom = 2f;
            table.AddCell(new FlowCell { Border = (PdfStyle.GreyMedium, 0.5f), Padding = 5f }.Add(heading).Add(grid));
        }

        return table;
    }

    private static List<(string, string)> PressRows(JobTicketDetail job)
    {
        var rows = new List<(string, string)>();
        var die = job.Die;

        if (job.Job.Imposition is { } imposition)
        {
            var orientation = imposition.Orientation == LabelOrientation.Rotated ? " · rotated 90°" : "";
            var imposed = job.Job.ImposedAt is { } imposedAt ? $"imposed {imposedAt.ToLocalTime():MMM d HH:mm}" : "not yet run";
            rows.Add(("Web width", $"{imposition.WebWidthIn:0.###}\""));
            rows.Add(("Layout", $"{imposition.LabelsAcross} across × {imposition.LabelsAround} around ({imposition.LabelsPerFrame}/frame){orientation}"));
            rows.Add(("Frame", $"{imposition.RepeatLengthIn:0.####}\" repeat · {imposed}"));
        }
        else if (die is not null)
        {
            rows.Add(("Web width", $"{die.WebWidthIn:0.####}\""));
            rows.Add(("Layout", $"{die.LabelsAcross} across × {die.LabelsAround} around · {die.DieRepeatIn ?? die.RepeatLengthIn:0.####}\" repeat"));
        }

        if (job.Job.Spec is { } spec)
        {
            if (spec.GutterAcrossIn > 0m || spec.GutterAroundIn > 0m)
            {
                rows.Add(("Gutters", $"{spec.GutterAcrossIn:0.####}\" × {spec.GutterAroundIn:0.####}\""));
            }

            if (spec.BleedIn > 0m)
            {
                rows.Add(("Bleed", $"{spec.BleedIn:0.####}\""));
            }

            if (spec.SetupWasteImpressions > 0m || spec.RunningWastePct > 0m)
            {
                rows.Add(("Waste", $"{spec.SetupWasteImpressions:0} impressions + {spec.RunningWastePct * 100:0.#}%"));
            }
        }

        var scheduled = (job.Job.ScheduledForDate?.ToString("MMM d, yyyy"), job.ScheduledPressName) switch
        {
            (null, null) => null,
            (null, var press) => press,
            (var date, null) => date,
            var (date, press) => $"{date} · {press}"
        };
        if (scheduled is not null)
        {
            rows.Add(("Scheduled", scheduled));
        }

        return rows;
    }

    private static List<(string, string)> FinishingRows(JobTicketDetail job)
    {
        var rows = new List<(string, string)>();
        var first = true;
        foreach (var step in job.Route.Where(s => s.Type == JobOperationType.Finishing))
        {
            rows.Add((first ? "Finishing" : "", step.Description));
            first = false;
        }

        if (job.Job.Spec?.Unwind is { } unwind)
        {
            rows.Add(("Unwind", unwind.Label()));
        }

        // Shrink-sleeve jobs: layflat and sleeve size in both units (converters spec in mm).
        if (job.ShrinkLayflatIn is { } shrinkLayflat)
        {
            rows.Add(("Shrink layflat", Inches.FormatWithMm(shrinkLayflat)));
            rows.Add(("Shrink size", Inches.FormatSizeWithMm(job.LabelAcrossIn, job.LabelAroundIn)));
        }

        if (job.Die is { } die)
        {
            var dieText = job.DieDescription ?? die.Description;
            dieText += $" · {die.DieType}";
            if (!string.IsNullOrWhiteSpace(die.Shape))
            {
                dieText += $" · {die.Shape}";
            }

            rows.Add(("Die", dieText));
            if (job.Job.Imposition is not null)
            {
                rows.Add(("Die layout", $"{die.LabelsAcross} across × {die.LabelsAround} around · {die.DieRepeatIn ?? die.RepeatLengthIn:0.####}\" repeat"));
            }

            if (!string.IsNullOrWhiteSpace(die.LinerSpec))
            {
                rows.Add(("Liner spec", die.LinerSpec!));
            }

            if (die.Supplier?.Name is { } supplier)
            {
                rows.Add(("Die supplier", string.IsNullOrWhiteSpace(die.SupplierPartNumber) ? supplier : $"{supplier} · {die.SupplierPartNumber}"));
            }

            if (die.Customer?.Name is { } owner)
            {
                rows.Add(("Die owner", owner));
            }
        }

        return rows;
    }

    /// <summary>Outsourced jobs: the vendor makes it, so the ordered spec is reference only.</summary>
    private static List<(string, string)> OrderedRows(JobTicketDetail job)
    {
        var ink = job.InkSet.ToString();
        if (!string.IsNullOrWhiteSpace(job.SpecialInksSummary))
        {
            ink += $" · {job.SpecialInksSummary}";
        }

        return
        [
            ("Size", $"{job.LabelAcrossIn:0.####}\" × {job.LabelAroundIn:0.####}\""),
            ("Substrate", job.SubstrateDescription),
            ("Ink", ink)
        ];
    }

    private static string VendorStatus(JobOutsourceInfo outsource) =>
        outsource.ReceivedAt is { } received
            ? $"Received {received.ToLocalTime():MMM d}"
            : outsource.QuantityReceived > 0
                ? $"{outsource.QuantityReceived:N0} received"
                : outsource.SentToVendorAt is { } sent ? $"At vendor since {sent:MMM d}" : "Not yet sent";

    private static FlowTable RouteTable(JobTicketDetail job)
    {
        var table = new FlowTable([24f, 272f, 52f, 62f, 52f, 46f, 46f]);
        foreach (var header in new[] { "#", "Operation", "Plan min", "Operator", "Date", "Good", "Waste" })
        {
            table.AddHeaderCell(new FlowCell
            {
                Background = PdfStyle.GreyLighten3,
                Border = (PdfStyle.GreyDarken1, 0.5f),
                Padding = 2.5f
            }.Add(Text(header, StandardFont.HelveticaBold, 7.5f)));
        }

        foreach (var step in job.Route)
        {
            table.AddCell(RouteCell(step.Sequence.ToString()));
            table.AddCell(RouteCell(step.Description));
            table.AddCell(RouteCell(step.PlannedMinutes.ToString("0")));
            table.AddCell(RouteCell(""));
            table.AddCell(RouteCell(""));
            table.AddCell(RouteCell(""));
            table.AddCell(RouteCell(""));
        }

        return table;
    }

    // A little bottom padding leaves the floor room to write in the sign-off columns.
    private static FlowCell RouteCell(string text) =>
        new FlowCell { Border = (PdfStyle.GreyLighten1, 0.5f), Padding = 3f, PaddingBottom = 7f }.Add(Text(text, size: 8.5f));

    // ---- Shared pieces -------------------------------------------------------------------------

    private static DateOnly? OrderDue(IReadOnlyList<JobTicketDetail> orderJobs)
    {
        var dueDates = orderJobs.Where(j => j.Job.DueDate.HasValue).Select(j => j.Job.DueDate!.Value).ToList();
        return dueDates.Count > 0 ? dueDates.Min() : null;
    }

    /// <summary>"CODE — description" as the ticket query builds it; a value without the dash is all code.</summary>
    private static (string Code, string? Name) SplitCode(string text)
    {
        var index = text.IndexOf(" — ", StringComparison.Ordinal);
        return index <= 0 ? (text, null) : (text[..index], text[(index + 3)..]);
    }

    /// <summary>The largest of <paramref name="sizes"/> at which the text stays on one line, else the smallest.</summary>
    private static float FitSize(string text, PdfFont font, float width, params float[] sizes)
    {
        foreach (var size in sizes)
        {
            if (font.GetWidth(text, size) <= width)
            {
                return size;
            }
        }

        return sizes[^1];
    }

    /// <summary>Shortens <paramref name="text"/> with an ellipsis until it fits <paramref name="width"/>.</summary>
    private static string Truncate(string text, PdfFont font, float size, float width)
    {
        if (font.GetWidth(text, size) <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && font.GetWidth(cut + "…", size) > width)
        {
            cut = cut[..^1];
        }

        return cut.TrimEnd() + "…";
    }

    private static FlowTable Section(string title, FlowCell body)
    {
        // The title is a table header row so it repeats when the body breaks across pages.
        var table = new FlowTable([1f]) { KeepTogether = true, MarginBottom = 8f };
        table.AddHeaderCell(SectionTitleCell(title, 1));
        table.AddCell(body);
        return table;
    }

    private static FlowCell SectionTitleCell(string title, int colspan) =>
        new FlowCell(colspan)
        {
            Background = PdfStyle.GreyLighten3,
            Border = (PdfStyle.GreyDarken1, 1f),
            Padding = 4f
        }.Add(Text(title, StandardFont.HelveticaBold, 8f));

    private static FlowParagraph Caption(string text) => Text(text, size: 6.5f, color: PdfStyle.GreyDarken2);

    private static FlowParagraph Text(
        string text,
        PdfFont? font = null,
        float size = 9.5f,
        (float R, float G, float B)? color = null,
        float leading = 1.2f)
    {
        return new FlowParagraph(text)
        {
            DefaultFont = font ?? StandardFont.Helvetica,
            DefaultFontSize = size,
            DefaultColor = color ?? PdfStyle.Black,
            MultipliedLeading = leading
        };
    }

    // ---- Page chrome ---------------------------------------------------------------------------

    private static void StampChrome(PdfDocument pdf, JobTicketDetail detail, IReadOnlyList<JobTicketDetail> orderJobs, string shopName, byte[]? logoBytes)
    {
        var printedAt = $"Printed {DateTime.Now:yyyy-MM-dd HH:mm}   ·   Ticket v2";
        var barcode = Barcode1D.Encode(detail.OrderNumber, BarcodeSymbology.Code128);
        var barcodeForm = barcode.CreateFormXObject(pdf, heightModules: BarcodeHeight);
        var barcodeScaleX = (BoxWidth - BoxPadding * 2f) / barcode.WidthWithQuietZones;
        var logo = PdfStyle.TryCreateImage(pdf, logoBytes);

        var total = pdf.PageCount;
        for (var i = 0; i < total; i++)
        {
            var page = pdf.Pages[i];
            var canvas = PdfStyle.OverlayCanvas(pdf, page);
            DrawHeader(canvas, page.Size, detail, orderJobs, shopName, logo, barcodeForm, barcodeScaleX);
            PdfStyle.WriteLine(canvas, printedAt, StandardFont.Helvetica, 7.5f, PdfStyle.GreyDarken2, Margin, 24f, TextHorizontalAlignment.Left);
            PdfStyle.WriteLine(canvas, $"Page {i + 1} of {total}", StandardFont.Helvetica, 7.5f, PdfStyle.GreyDarken2, page.Size.Width - Margin, 24f, TextHorizontalAlignment.Right);
        }
    }

    private static void DrawHeader(PdfCanvas canvas, PdfRect pageSize, JobTicketDetail detail, IReadOnlyList<JobTicketDetail> orderJobs, string shopName, PdfImageXObject? logo, PdfFormXObject barcodeForm, float barcodeScaleX)
    {
        var left = Margin;
        var right = pageSize.Width - Margin;
        var top = pageSize.Height - Margin;

        var yLeft = top;
        if (logo is not null)
        {
            var logoWidth = Math.Min(200f, logo.Width * LogoHeight / Math.Max(1, logo.Height));
            canvas.AddImageFittedIntoRectangle(logo, new PdfRect(left, yLeft - LogoHeight, logoWidth, LogoHeight));
            yLeft -= LogoHeight + 4f;
        }
        else
        {
            yLeft = PdfStyle.WriteLine(canvas, shopName, StandardFont.HelveticaBold, 15f, PdfStyle.Black, left, yLeft, TextHorizontalAlignment.Left);
        }

        PdfStyle.WriteLine(canvas, "SALES ORDER TICKET", StandardFont.HelveticaBold, 11f, PdfStyle.GreyDarken2, left, yLeft, TextHorizontalAlignment.Left);

        // Sales order box: order number on top, Code 128 of it spanning the box below.
        var boxX = right - BoxWidth;
        canvas.SaveState();
        canvas.SetStrokeRgb(0, 0, 0);
        canvas.SetLineWidth(2f);
        canvas.Rectangle(boxX, top - BoxHeight, BoxWidth, BoxHeight);
        canvas.Stroke();
        canvas.RestoreState();

        var boxCenter = boxX + BoxWidth / 2f;
        PdfStyle.WriteLine(canvas, detail.OrderNumber, StandardFont.CourierBold, 16f, PdfStyle.Black, boxCenter, top - BoxPadding, TextHorizontalAlignment.Center);
        canvas.AddFormXObject(
            barcodeForm, barcodeScaleX, 0, 0, 1f,
            boxX + BoxPadding, top - BoxPadding - NumberLineHeight - BarcodeGap - BarcodeHeight);

        // Banner strip: who it is for and the order-level facts. Values shrink, then shorten, to fit.
        var bannerTop = top - Math.Max(LeftBlockHeight, BoxHeight) - 8f;
        var bannerWidth = right - left;
        canvas.SaveState();
        canvas.SetFillRgb(PdfStyle.GreyLighten4.R, PdfStyle.GreyLighten4.G, PdfStyle.GreyLighten4.B);
        canvas.Rectangle(left, bannerTop - BannerHeight, bannerWidth, BannerHeight);
        canvas.Fill();
        canvas.SetStrokeRgb(PdfStyle.GreyDarken1.R, PdfStyle.GreyDarken1.G, PdfStyle.GreyDarken1.B);
        canvas.SetLineWidth(1f);
        canvas.Rectangle(left, bannerTop - BannerHeight, bannerWidth, BannerHeight);
        canvas.Stroke();
        canvas.RestoreState();

        var cells = new (float Weight, string Label, string Value)[]
        {
            (2.3f, "CUSTOMER", detail.CustomerName),
            (1.5f, "CUSTOMER PO", detail.CustomerPoNumber ?? "—"),
            (0.9f, "DUE", OrderDue(orderJobs)?.ToString("MMM d") ?? "—"),
            (0.9f, "SHIP DATE", detail.RequestedShipDate?.ToString("MMM d") ?? "—"),
            (1.3f, "SHIP VIA", detail.ShippingMethodName ?? "—"),
            (0.6f, "JOBS", orderJobs.Count.ToString()),
            (0.9f, "TOTAL QTY", orderJobs.Sum(j => j.Job.QuantityOrdered).ToString("N0"))
        };

        var totalWeight = cells.Sum(c => c.Weight);
        var x = left;
        foreach (var (weight, label, value) in cells)
        {
            var cellWidth = bannerWidth * weight / totalWeight;
            var room = cellWidth - 8f;
            var size = FitSize(value, StandardFont.HelveticaBold, room, 11f, 10f, 9f, 8f);
            var centerX = x + cellWidth / 2f;
            var yCell = bannerTop - 6f;
            yCell = PdfStyle.WriteLine(canvas, label, StandardFont.Helvetica, 7f, PdfStyle.GreyDarken2, centerX, yCell, TextHorizontalAlignment.Center);
            PdfStyle.WriteLine(canvas, Truncate(value, StandardFont.HelveticaBold, size, room), StandardFont.HelveticaBold, size, PdfStyle.Black, centerX, yCell + (11f - size) / 2f, TextHorizontalAlignment.Center);
            x += cellWidth;
        }
    }
}
