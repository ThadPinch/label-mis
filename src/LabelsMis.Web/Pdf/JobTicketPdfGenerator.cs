using FrontEndSuite.PdfPlatform.Canvas;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Fonts;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;
using LabelsMis.Web.Services.Jobs;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Pdf;

public class JobOptions
{
    public const string SectionName = "Jobs";
    public string PdfStoragePath { get; set; } = "./data/pdfs/jobs";
    public string ShopName { get; set; } = "Labels MIS Print Shop";
}

public class JobTicketPdfGenerator(IOptions<JobOptions> options)
{
    private const float Margin = 28f;
    private const float BoxWidth = 200f;
    private const float BoxPadding = 8f;
    private const float QrSize = 48f;
    private const float BoxHeight = QrSize + BoxPadding * 2f; // QR code sets the box height
    private const float BannerHeight = 33.6f; // 6 pad + 7pt label line + 11pt value line + 6 pad
    private const float LeftBlockHeight = 15f * 1.2f + 11f * 1.2f;
    private const float HeaderHeight = BoxHeight + 8f + BannerHeight;

    public async Task<byte[]> GenerateAsync(JobTicketDetail detail, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PdfStoragePath);

        return await Task.Run(() => Render(detail, settings.ShopName), cancellationToken);
    }

    private static byte[] Render(JobTicketDetail detail, string shopName)
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(
            pdf, PdfRect.Letter,
            Margin + HeaderHeight + 10f,
            Margin,
            Margin + 14f,
            Margin);

        ComposeContent(document, detail);
        StampChrome(pdf, detail, shopName);
        return pdf.Save();
    }

    private static void ComposeContent(FlowDocument document, JobTicketDetail detail)
    {
        // Order + product, side by side.
        var pair = new FlowTable(new[] { 1f, 0.04f, 1f }) { MarginBottom = 10f };

        var orderRows = new List<(string, string)>
        {
            ("Customer", detail.CustomerName),
            ("Sales order", detail.OrderNumber),
            ("Customer PO", detail.CustomerPoNumber ?? "—"),
            ("Qty ordered", detail.Job.QuantityOrdered.ToString("N0")),
            ("Qty planned", $"{detail.Job.QuantityPlanned:N0}{OverrunSuffix(detail)}"),
            ("Scheduled", ScheduleText(detail))
        };

        var productRows = new List<(string, string)>
        {
            ("SKU", detail.ProductSku),
            ("Description", detail.ProductDescription),
            ("Label size", $"{detail.LabelAcrossIn:0.####}\" × {detail.LabelAroundIn:0.####}\"")
        };
        if (detail.Job.Spec is { } productSpec)
        {
            productRows.Add(("Corner radius", $"{productSpec.CornerRadiusIn:0.####}\""));
            productRows.Add(("Gutters", $"{productSpec.GutterAcrossIn:0.####}\" across × {productSpec.GutterAroundIn:0.####}\" around"));
            productRows.Add(("Bleed", $"{productSpec.BleedIn:0.####}\""));
        }

        productRows.Add(("Die", detail.DieDescription ?? "—"));

        pair.AddCell(new FlowCell { NoBorder = true, Padding = 0f }.Add(InfoSection("ORDER", orderRows)));
        pair.AddCell(new FlowCell { NoBorder = true, Padding = 0f });
        pair.AddCell(new FlowCell { NoBorder = true, Padding = 0f }.Add(InfoSection("PRODUCT", productRows)));
        document.Add(pair);

        // Materials & ink.
        var materialRows = new List<(string, string)>
        {
            ("Substrate", detail.SubstrateDescription),
            ("Ink set", detail.InkSet.ToString())
        };
        if (!string.IsNullOrWhiteSpace(detail.SpecialInksSummary))
        {
            materialRows.Add(("Special inks", detail.SpecialInksSummary!));
        }

        if (detail.Job.Spec is { } wasteSpec)
        {
            materialRows.Add(("Setup waste", $"{wasteSpec.SetupWasteImpressions:0} impressions"));
            materialRows.Add(("Running waste", $"{wasteSpec.RunningWastePct * 100:0.#}%"));
        }

        if (!string.IsNullOrWhiteSpace(detail.Job.Spec?.ArtworkFilePath))
        {
            materialRows.Add(("Artwork", "On file — see job in system"));
        }

        document.Add(WithMarginBottom(InfoSection("MATERIALS & INK", materialRows, labelWeight: 0.6f)));

        // Packaging, when the product has a roll spec.
        if (detail.RollSpec is { } roll)
        {
            var packagingRows = new List<(string, string)>
            {
                ("Labels per roll", roll.LabelsPerRoll.ToString("N0")),
                ("Core / max OD", $"{roll.CoreSizeIn:0.###}\" core · {roll.MaxOdIn:0.###}\" max OD"),
                ("Unwind", $"#{roll.UnwindPosition}"),
                ("Rolls per case", roll.RollsPerCase.ToString("N0"))
            };
            if (!string.IsNullOrWhiteSpace(roll.CaseLabelFormat))
            {
                packagingRows.Add(("Case label", roll.CaseLabelFormat!));
            }

            document.Add(WithMarginBottom(InfoSection("PACKAGING", packagingRows, labelWeight: 0.6f)));
        }

        // Route with sign-off columns for the floor.
        document.Add(WithMarginBottom(SectionShell("ROUTE", BuildRouteBody(detail))));

        // Notes, always shown so the floor has a place to write.
        var notesBody = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = 6f, PaddingBottom = 56f };
        notesBody.Add(Paragraph(detail.Job.Notes ?? ""));
        document.Add(SectionShell("NOTES", notesBody));
    }

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
            Padding = 3.5f
        }.Add(Paragraph(text, StandardFont.HelveticaBold, 8f));
    }

    private static FlowCell RouteCell(string text)
    {
        // Generous bottom padding leaves the floor room to write in the sign-off columns.
        return new FlowCell
        {
            Border = (PdfStyle.GreyLighten1, 0.5f),
            Padding = 4f,
            PaddingBottom = 14f
        }.Add(Paragraph(text));
    }

    private static FlowTable InfoSection(string title, IReadOnlyList<(string Label, string Value)> rows, float labelWeight = 1.1f)
    {
        var rowsTable = new FlowTable(new[] { labelWeight, 2f });
        foreach (var (label, value) in rows)
        {
            rowsTable.AddCell(new FlowCell { NoBorder = true, Padding = 1.5f }
                .Add(Paragraph(label, size: 8.5f, color: PdfStyle.GreyDarken2)));
            rowsTable.AddCell(new FlowCell { NoBorder = true, Padding = 1.5f }
                .Add(Paragraph(value, StandardFont.HelveticaBold)));
        }

        var body = new FlowCell { Border = (PdfStyle.GreyDarken1, 1f), Padding = 5f };
        body.Add(rowsTable);
        return SectionShell(title, body);
    }

    private static FlowTable SectionShell(string title, FlowCell body)
    {
        var table = new FlowTable(new[] { 1f });
        table.AddCell(new FlowCell
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

    private static string OverrunSuffix(JobTicketDetail detail)
    {
        if (detail.Job.QuantityOrdered <= 0 || detail.Job.QuantityPlanned <= detail.Job.QuantityOrdered)
        {
            return string.Empty;
        }

        var overrunPct = (detail.Job.QuantityPlanned - detail.Job.QuantityOrdered) * 100m / detail.Job.QuantityOrdered;
        return $"  (+{overrunPct:0.#}% overrun)";
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

    private static void StampChrome(PdfDocument pdf, JobTicketDetail detail, string shopName)
    {
        var printedAt = $"Printed {DateTime.Now:yyyy-MM-dd HH:mm}";
        var qr = QrCode.Encode(detail.Job.JobNumber, QrErrorCorrection.M);
        var qrForm = qr.CreateFormXObject(pdf);
        var qrScale = QrSize / qr.SizeWithQuietZone;

        var total = pdf.PageCount;
        for (var i = 0; i < total; i++)
        {
            var page = pdf.Pages[i];
            var canvas = PdfStyle.OverlayCanvas(pdf, page);
            DrawHeader(canvas, page.Size, detail, shopName, qrForm, qrScale);
            DrawFooter(canvas, page.Size, printedAt, i + 1, total);
        }
    }

    private static void DrawHeader(PdfCanvas canvas, PdfRect pageSize, JobTicketDetail detail, string shopName, PdfFormXObject qrForm, float qrScale)
    {
        var left = Margin;
        var right = pageSize.Width - Margin;
        var top = pageSize.Height - Margin;

        var yLeft = top;
        yLeft = PdfStyle.WriteLine(canvas, shopName, StandardFont.HelveticaBold, 15f, PdfStyle.Black, left, yLeft, TextHorizontalAlignment.Left);
        PdfStyle.WriteLine(canvas, "PRODUCTION JOB TICKET", StandardFont.HelveticaBold, 11f, PdfStyle.GreyDarken2, left, yLeft, TextHorizontalAlignment.Left);

        // Job number box: the floor scans/reads this first. QR code on the right encodes the
        // job number; the human-readable number sits beside it.
        var boxX = right - BoxWidth;
        canvas.SaveState();
        canvas.SetStrokeRgb(0, 0, 0);
        canvas.SetLineWidth(2f);
        canvas.Rectangle(boxX, top - BoxHeight, BoxWidth, BoxHeight);
        canvas.Stroke();
        canvas.RestoreState();

        canvas.AddFormXObject(qrForm, qrScale, 0, 0, qrScale, right - BoxPadding - QrSize, top - BoxPadding - QrSize);

        var textCenter = boxX + (BoxWidth - QrSize - BoxPadding) / 2f;
        var textBlockHeight = 20f * 1.2f + 7f * 1.2f;
        var yBox = top - BoxPadding - (BoxHeight - BoxPadding * 2f - textBlockHeight) / 2f;
        yBox = PdfStyle.WriteLine(canvas, detail.Job.JobNumber, StandardFont.CourierBold, 20f, PdfStyle.Black, textCenter, yBox, TextHorizontalAlignment.Center);
        PdfStyle.WriteLine(canvas, "Scan job number", StandardFont.Helvetica, 7f, PdfStyle.GreyDarken2, textCenter, yBox, TextHorizontalAlignment.Center);

        // Banner strip: the four things the floor checks first.
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

        var cells = new (string Label, string Value)[]
        {
            ("DUE DATE", detail.Job.DueDate?.ToString("MMM d, yyyy") ?? "—"),
            ("SHIP DATE", detail.RequestedShipDate?.ToString("MMM d, yyyy") ?? "—"),
            ("QTY TO PRODUCE", detail.Job.QuantityPlanned.ToString("N0")),
            ("PRIORITY", detail.Job.Priority.ToString()),
            ("STATUS", detail.Job.Status.ToString())
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
