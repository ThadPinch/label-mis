using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Pdf;

public class JobOptions
{
    public const string SectionName = "Jobs";
    public string PdfStoragePath { get; set; } = "./data/pdfs/jobs";
    public string ShopName { get; set; } = "Labels MIS Print Shop";
}

public class JobTicketPdfGenerator(IOptions<JobOptions> options, GeneralSettingsService generalSettings)
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

    /// <summary>
    /// Renders the ticket for <paramref name="detail"/>. When <paramref name="orderJobs"/> is
    /// given (all jobs on the same sales order, in line order), every job gets a spec block with
    /// its own route underneath, so the ticket prints identically from any job on the order.
    /// </summary>
    public async Task<byte[]> GenerateAsync(
        JobTicketDetail detail,
        IReadOnlyList<JobTicketDetail>? orderJobs = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        // Brand the ticket from system settings; fall back to the configured shop name when
        // the company profile hasn't been filled in.
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
        document.Add(WithMarginBottom(OrderSection(detail)));

        // Notes up front, always shown so the floor sees them before the job specs
        // and has a place to write. Order notes first, then every job's own notes so
        // the ticket prints identically no matter which job it was printed from.
        var notesParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(detail.OrderNotes))
        {
            notesParts.Add(detail.OrderNotes!.Trim());
        }

        foreach (var job in orderJobs)
        {
            if (!string.IsNullOrWhiteSpace(job.Job.Notes))
            {
                notesParts.Add($"{job.Job.JobNumber}: {job.Job.Notes!.Trim()}");
            }
        }

        var notesBody = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = 6f, PaddingBottom = 56f };
        notesBody.Add(Paragraph(string.Join("\n", notesParts)));
        document.Add(WithMarginBottom(SectionShell("NOTES", notesBody)));

        // Non-label charges on the order (die creation, design) — informational only, no job exists.
        if (detail.OrderCharges is { Count: > 0 } orderCharges)
        {
            var chargesBody = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = 6f };
            foreach (var charge in orderCharges)
            {
                chargesBody.Add(Paragraph($"• {charge}", StandardFont.HelveticaBold, 9f));
            }
            chargesBody.Add(Paragraph(
                "Billed with this order — no production job is scheduled for these items.",
                size: 7.5f, color: PdfStyle.GreyDarken2));
            document.Add(WithMarginBottom(SectionShell("ORDER CHARGES (NOT PRODUCED)", chargesBody)));
        }

        // Each job's spec block with its own route (sign-off columns) directly underneath.
        foreach (var job in orderJobs)
        {
            document.Add(WithMarginBottom(JobBlock(job)));
        }
    }

    private static FlowTable OrderSection(JobTicketDetail detail)
    {
        var rows = new List<(string, string, string, string)>
        {
            ("Customer", detail.CustomerName, "Sales order", detail.OrderNumber),
            ("Customer PO", detail.CustomerPoNumber ?? "—", "Ship date", detail.RequestedShipDate?.ToString("MMM d, yyyy") ?? "—"),
            ("Ship via", detail.ShippingMethodName ?? "—", "", "")
        };

        return SectionShell("ORDER", PairGridBody(rows));
    }

    private static FlowTable JobBlock(JobTicketDetail job)
    {
        var qty = $"{job.Job.QuantityOrdered:N0}";
        var status = $"{job.Job.Status} · {ScheduleText(job)}";
        var ink = job.InkSet.ToString();
        if (!string.IsNullOrWhiteSpace(job.SpecialInksSummary))
        {
            ink += $" · {job.SpecialInksSummary}";
        }

        var rows = new List<(string, string, string, string)>
        {
            ("Description", job.ProductDescription, "Status", status),
            ("Label size", $"{job.LabelAcrossIn:0.####}\" × {job.LabelAroundIn:0.####}\"", "Qty", qty),
            ("Substrate", job.SubstrateDescription, "Due", job.Job.DueDate?.ToString("MMM d, yyyy") ?? "—"),
            ("Ink", ink, "Spec", SpecSummary(job) ?? "—")
        };

        // Finishing tasks from the job's route, with any assigned materials in the descriptions.
        var finishing = job.Route
            .Where(s => s.Type == JobOperationType.Finishing)
            .Select(s => s.Description)
            .ToList();
        rows.Add(("Finishing", finishing.Count > 0 ? string.Join(" · ", finishing) : "None", "", ""));

        rows.Add(("Unwind", job.Job.Spec?.Unwind?.Label() ?? "—",
            "Shrink layflat", job.Job.Spec?.ShrinkLayflatIn is { } shrinkLayflat ? $"{shrinkLayflat:0.0000}\"" : "—"));

        // Die details — the same facts the job page's die popover shows.
        if (job.Die is { } die)
        {
            var dieType = die.DieType.ToString();
            if (!string.IsNullOrWhiteSpace(die.Shape))
            {
                dieType += $" · {die.Shape}";
            }

            var repeat = die.DieRepeatIn ?? die.RepeatLengthIn;
            var supplier = die.Supplier?.Name;
            if (supplier is not null && !string.IsNullOrWhiteSpace(die.SupplierPartNumber))
            {
                supplier += $" · {die.SupplierPartNumber}";
            }

            rows.Add(("Die", job.DieDescription ?? die.Description, "Die type", dieType));
            rows.Add(("Die layout", $"{die.LabelsAcross} across × {die.LabelsAround} around · {repeat:0.0000}\" repeat",
                "Web width", $"{die.WebWidthIn:0.####}\""));
            rows.Add(("Die location", die.Location ?? "—", "Liner spec", die.LinerSpec ?? "—"));
            if (supplier is not null || die.Customer is not null)
            {
                rows.Add(("Die supplier", supplier ?? "—", "Die owner", die.Customer?.Name ?? "—"));
            }
        }
        else
        {
            rows.Add(("Die", job.DieDescription ?? "—", "", ""));
        }

        if (!string.IsNullOrWhiteSpace(job.LineNotes))
        {
            rows.Add(("Line notes", job.LineNotes!, "", ""));
        }

        var title = $"JOB {job.Job.JobNumber} — {job.ProductSku}";
        return JobSectionShell(title, job.Job.JobNumber, PairGridBody(rows), BuildRouteBody(job));
    }

    /// <summary>Section shell whose header carries a small scannable barcode of the job number.
    /// The job's route (with sign-off columns) renders directly beneath the spec body.</summary>
    private static FlowTable JobSectionShell(string title, string jobNumber, FlowCell body, FlowCell routeBody)
    {
        var header = new FlowTable(new[] { 3.2f, 1f });
        var titleParagraph = Paragraph(title, StandardFont.HelveticaBold, 8f);
        titleParagraph.MarginTop = (JobBarcodeHeight - 8f * 1.2f) / 2f;
        header.AddCell(new FlowCell { NoBorder = true, Padding = 0f }.Add(titleParagraph));
        header.AddCell(new FlowCell { NoBorder = true, Padding = 0f }
            .Add(new FlowBarcode(jobNumber, JobBarcodeHeight, maxWidth: 110f)));

        // Header row so the job title repeats when the block breaks across pages.
        var table = new FlowTable(new[] { 1f });
        table.AddHeaderCell(new FlowCell
        {
            Background = PdfStyle.GreyLighten3,
            Border = (PdfStyle.GreyDarken1, 1f),
            Padding = 4f
        }.Add(header));
        table.AddCell(body);
        table.AddCell(routeBody);
        return table;
    }

    private static string? SpecSummary(JobTicketDetail job)
    {
        if (job.Job.Spec is not { } spec)
        {
            return null;
        }

        return $"{spec.CornerRadiusIn:0.####}\" CR · {spec.GutterAcrossIn:0.####}\" × {spec.GutterAroundIn:0.####}\" gutters · " +
               $"{spec.BleedIn:0.####}\" bleed · waste {spec.SetupWasteImpressions:0} imp + {spec.RunningWastePct * 100:0.#}%";
    }

    /// <summary>Two label/value pairs per row — the compact grid used by the order and job blocks.</summary>
    private static FlowCell PairGridBody(IReadOnlyList<(string Label1, string Value1, string Label2, string Value2)> rows)
    {
        var table = new FlowTable(new[] { 0.9f, 2.3f, 0.7f, 2.3f });
        foreach (var (label1, value1, label2, value2) in rows)
        {
            table.AddCell(PairLabelCell(label1));
            table.AddCell(PairValueCell(value1));
            table.AddCell(PairLabelCell(label2));
            table.AddCell(PairValueCell(value2));
        }

        var body = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = 4f };
        body.Add(table);
        return body;
    }

    private static FlowCell PairLabelCell(string text) =>
        new FlowCell { NoBorder = true, Padding = 1.5f }
            .Add(Paragraph(text, size: 7.5f, color: PdfStyle.GreyDarken2));

    private static FlowCell PairValueCell(string text) =>
        new FlowCell { NoBorder = true, Padding = 1.5f }
            .Add(Paragraph(text, StandardFont.HelveticaBold, 8.5f));

    private static FlowCell BuildRouteBody(JobTicketDetail detail)
    {
        // Column weights mirror the old fixed widths: 24, ~272 (flex), 52, 62, 52, 46, 46 points.
        var table = new FlowTable(new[] { 24f, 272f, 52f, 62f, 52f, 46f, 46f });
        table.AddHeaderCell(RouteHeaderCell("#"));
        table.AddHeaderCell(RouteHeaderCell("Operation"));
        table.AddHeaderCell(RouteHeaderCell("Plan min"));
        table.AddHeaderCell(RouteHeaderCell("Operator"));
        table.AddHeaderCell(RouteHeaderCell("Date"));
        table.AddHeaderCell(RouteHeaderCell("Good"));
        table.AddHeaderCell(RouteHeaderCell("Waste"));

        foreach (var step in detail.Route)
        {
            table.AddCell(RouteCell(step.Sequence.ToString()));
            table.AddCell(RouteCell(step.Description));
            table.AddCell(RouteCell(step.PlannedMinutes.ToString("0")));
            table.AddCell(RouteCell(""));
            table.AddCell(RouteCell(""));
            table.AddCell(RouteCell(""));
            table.AddCell(RouteCell(""));
        }

        var body = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = 0f };
        body.Add(table);
        return body;
    }

    private static FlowCell RouteHeaderCell(string text)
    {
        return new FlowCell
        {
            Background = PdfStyle.GreyLighten3,
            Border = (PdfStyle.GreyDarken1, 0.5f),
            Padding = 2.5f
        }.Add(Paragraph(text, StandardFont.HelveticaBold, 7.5f));
    }

    private static FlowCell RouteCell(string text)
    {
        // A little bottom padding leaves the floor room to write in the sign-off columns.
        return new FlowCell
        {
            Border = (PdfStyle.GreyLighten1, 0.5f),
            Padding = 3f,
            PaddingBottom = 7f
        }.Add(Paragraph(text, size: 8.5f));
    }

    private static FlowTable SectionShell(string title, FlowCell body)
    {
        // The title is a table header row so it repeats when the body breaks across pages.
        var table = new FlowTable(new[] { 1f });
        table.AddHeaderCell(new FlowCell
        {
            Background = PdfStyle.GreyLighten3,
            Border = (PdfStyle.GreyDarken1, 1f),
            Padding = 4f
        }.Add(Paragraph(title, StandardFont.HelveticaBold, 8f)));
        table.AddCell(body);
        return table;
    }

    private static FlowTable WithMarginBottom(FlowTable table)
    {
        table.MarginBottom = 10f;
        return table;
    }

    private static FlowParagraph Paragraph(
        string text,
        PdfFont? font = null,
        float size = 9.5f,
        (float R, float G, float B)? color = null)
    {
        return new FlowParagraph(text)
        {
            DefaultFont = font ?? StandardFont.Helvetica,
            DefaultFontSize = size,
            DefaultColor = color ?? PdfStyle.Black,
            MultipliedLeading = 1.2f
        };
    }

    private static string ScheduleText(JobTicketDetail detail)
    {
        var date = detail.Job.ScheduledForDate?.ToString("MMM d, yyyy");
        return (date, detail.ScheduledPressName) switch
        {
            (null, null) => "Not scheduled",
            (null, var press) => press!,
            (var d, null) => d!,
            var (d, press) => $"{d} — {press}"
        };
    }

    private static void StampChrome(PdfDocument pdf, JobTicketDetail detail, IReadOnlyList<JobTicketDetail> orderJobs, string shopName, byte[]? logoBytes)
    {
        var printedAt = $"Printed {DateTime.Now:yyyy-MM-dd HH:mm}";
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
            DrawFooter(canvas, page.Size, printedAt, i + 1, total);
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

        // Sales order box: the ticket represents the whole order. Order number on top,
        // Code 128 of the order number spanning the box below it.
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

        // Banner strip: order-level facts; per-job status lives in the job blocks.
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

        var dueDates = orderJobs.Where(j => j.Job.DueDate.HasValue).Select(j => j.Job.DueDate!.Value).ToList();
        var cells = new (string Label, string Value)[]
        {
            ("DUE DATE", dueDates.Count > 0 ? dueDates.Min().ToString("MMM d, yyyy") : "—"),
            ("SHIP DATE", detail.RequestedShipDate?.ToString("MMM d, yyyy") ?? "—"),
            ("SHIP VIA", detail.ShippingMethodName ?? "—"),
            ("TOTAL QTY", orderJobs.Sum(j => j.Job.QuantityOrdered).ToString("N0")),
            ("JOBS", orderJobs.Count.ToString()),
            ("CUSTOMER PO", detail.CustomerPoNumber ?? "—")
        };

        var cellWidth = bannerWidth / cells.Length;
        for (var i = 0; i < cells.Length; i++)
        {
            var centerX = left + cellWidth * (i + 0.5f);
            var yCell = bannerTop - 6f;
            yCell = PdfStyle.WriteLine(canvas, cells[i].Label, StandardFont.Helvetica, 7f, PdfStyle.GreyDarken2, centerX, yCell, TextHorizontalAlignment.Center);
            PdfStyle.WriteLine(canvas, cells[i].Value, StandardFont.HelveticaBold, 11f, PdfStyle.Black, centerX, yCell, TextHorizontalAlignment.Center);
        }
    }

    private static void DrawFooter(PdfCanvas canvas, PdfRect pageSize, string printedAt, int pageNumber, int totalPages)
    {
        PdfStyle.WriteLine(canvas, printedAt, StandardFont.Helvetica, 7.5f, PdfStyle.GreyDarken2, Margin, 24f, TextHorizontalAlignment.Left);
        PdfStyle.WriteLine(canvas, $"Page {pageNumber} of {totalPages}", StandardFont.Helvetica, 7.5f, PdfStyle.GreyDarken2, pageSize.Width - Margin, 24f, TextHorizontalAlignment.Right);
    }
}
