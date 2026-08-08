using FluentAssertions;
using FrontEndSuite.PdfPlatform.Document;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Settings;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests;

/// <summary>
/// End-to-end smoke tests for the PdfPlatform-based generators: render each document from
/// realistic data, verify the bytes are a loadable PDF with the expected page count, and
/// (when PDF_SMOKE_OUT is set) write the files out for visual inspection.
/// </summary>
public class PdfGeneratorSmokeTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);

    // 1x1 PNG, exercises the ImageSharp logo-decode path.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task EstimatePdf_renders_and_paginates()
    {
        var generator = new EstimatePdfGenerator(
            Options.Create(new EstimateOptions()),
            new StubGeneralSettingsService(BrandedSettings()),
            UnusedPdfStorage());

        var lines = Enumerable.Range(1, 8).Select(i => new EstimatePdfLine(
            i,
            $"4×3 matte BOPP label — design {i}",
            4m,
            3m,
            "White matte BOPP 2.6 mil",
            InkSet.CMYK.ToString(),
            i % 2 == 0 ? "Includes lamination." : null,
            new[]
            {
                new EstimatePdfBreak(1000, 0.1234m, 123.40m),
                new EstimatePdfBreak(2500, 0.0987m, 246.75m),
                new EstimatePdfBreak(5000, 0.0765m, 382.50m)
            })).ToList();

        var model = new EstimatePdfModel(
            "EST-1001", 2, Now, new DateOnly(2026, 8, 24),
            "Acme Foods",
            new EstimatePdfAddress(null, "123 Main St", "Suite 4", "Phoenix", "AZ", "85001"),
            new EstimatePdfAddress("Receiving Dept", "456 Dock Rd", null, "Tempe", "AZ", "85281"),
            lines,
            "UPS Ground", 42.50m, true,
            "Customer requested matte finish on all labels.",
            "Pat Doe");

        var bytes = await generator.GenerateBytesAsync(model);

        AssertLoadablePdf(bytes, minPages: 2, "estimate.pdf");
    }

    [Fact]
    public async Task PurchaseOrderPdf_renders()
    {
        var generator = new PurchaseOrderPdfGenerator(
            Options.Create(new PurchaseOrderOptions()),
            new StubGeneralSettingsService(BrandedSettings()),
            UnusedPdfStorage());

        var supplier = Supplier.Create(Guid.NewGuid(), "Great Rolls Inc", "GRI", "Net 30", 7, "ACCT-42", UserId, Now);
        var po = PurchaseOrder.CreateDraft(
            Guid.NewGuid(), "PO-2026-0042", supplier.Id, Now, new DateOnly(2026, 8, 5),
            "Rush order — call on arrival.", UserId, Now);
        SetNavigation(po, nameof(PurchaseOrder.Supplier), supplier);

        for (var i = 1; i <= 3; i++)
        {
            var stock = Stock.Create(
                Guid.NewGuid(), $"BOPP-{i}", $"White BOPP {i} mil", "BOPP", "Permanent", "40# liner",
                3.2m, 13m, supplier.Id, $"SPN-{i}", 0.045m, 5000m, UserId, Now);
            var line = PurchaseOrderLine.Create(Guid.NewGuid(), po.Id, i, stock.Id, 12000m + i * 500m, 0.0450m, UserId, Now);
            SetNavigation(line, nameof(PurchaseOrderLine.Stock), stock);
            po.AddLine(line);
        }

        var bytes = await generator.GenerateBytesAsync(po);

        AssertLoadablePdf(bytes, minPages: 1, "purchase-order.pdf");
    }

    [Fact]
    public async Task JobTicketPdf_renders()
    {
        var generator = new JobTicketPdfGenerator(
            Options.Create(new JobOptions()),
            new StubGeneralSettingsService(BrandedSettings()));

        var spec = LabelsMis.Domain.ValueObjects.LabelSpec.Create(
            4m, 3m, 0.125m, 0.0625m, 0.0625m, 0.0625m,
            Guid.NewGuid(), Guid.NewGuid(), InkSet.CMYK_PlusSpot,
            0, 1m, "[]", "[]", 250m, 0.04m, null, null, null,
            UnwindDirection.Position3);

        var job = Job.CreatePlanned(
            Guid.NewGuid(), "J-1042", Guid.NewGuid(), Guid.NewGuid(),
            5000, 5250, new DateOnly(2026, 8, 10), 2,
            "Customer wants cores shrink-wrapped.", spec, UserId, Now);
        job.Schedule(new DateOnly(2026, 8, 3), Guid.NewGuid(), UserId, Now);

        var rollSpec = RollSpec.Create(Guid.NewGuid(), Guid.NewGuid(), 250, 3m, 4, 8m, 12, "CASE-{n}", UserId, Now);

        var detail = new JobTicketDetail(
            job,
            "Acme Foods",
            "4×3 matte BOPP label",
            "SKU-4X3-MATTE",
            "SO-2026-0117",
            "PO-88231",
            new DateOnly(2026, 8, 7),
            4m, 3m,
            "White matte BOPP 2.6 mil",
            InkSet.CMYK_PlusSpot,
            "PMS 485, PMS 2685",
            "Die D-204 (4.125 × 3.125, 0.125 CR)",
            "Mark Andy P5",
            rollSpec,
            new[]
            {
                new JobTicketRouteStep(1, JobOperationType.Press, "Prepress / plate mount", 45m),
                new JobTicketRouteStep(2, JobOperationType.Press, "Print — Mark Andy P5", 120m),
                new JobTicketRouteStep(3, JobOperationType.Finishing, "Gloss lamination — material: LAM-G — 1.2 mil gloss", 40m),
                new JobTicketRouteStep(4, JobOperationType.Finishing, "Die cut & rewind", 60m),
                new JobTicketRouteStep(5, JobOperationType.Pack, "Pack & label cases", 30m)
            },
            OrderNotes: "Rush order — customer will pick up partials as they finish.",
            LineNotes: "Matte finish only — no gloss substitutes.");

        // A sibling line on the same order: the ticket shows a compact block for every job.
        var siblingJob = Job.CreatePlanned(
            Guid.NewGuid(), "J-1043", Guid.NewGuid(), Guid.NewGuid(),
            2000, 2100, new DateOnly(2026, 8, 10), 2,
            null, null, UserId, Now);
        var sibling = detail with
        {
            Job = siblingJob,
            ProductDescription = "4×3 gloss BOPP label",
            ProductSku = "SKU-4X3-GLOSS",
            InkSet = InkSet.CMYK,
            SpecialInksSummary = null,
            ScheduledPressName = null
        };

        var bytes = await generator.GenerateAsync(detail, new[] { detail, sibling });

        AssertLoadablePdf(bytes, minPages: 1, "job-ticket.pdf");
    }

    [Fact]
    public async Task InvoicePdf_renders()
    {
        var generator = new InvoicePdfGenerator(
            new StubGeneralSettingsService(BrandedSettings()),
            UnusedPdfStorage());

        var customer = Customer.Create(
            Guid.NewGuid(), "Acme Foods", "ACME", PaymentTerms.Prepay, false, 0.4m,
            CustomerStatus.Active, null, UserId, Now);
        customer.AddAddress(Address.Create(
            Guid.NewGuid(), customer.Id, AddressType.Billing, "123 Main St", "Suite 4",
            "Phoenix", "AZ", "85001", "US", true, UserId, Now));

        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(), "SO-2026-0117", customer.Id, null, null, "PO-88231",
            Now, new DateOnly(2026, 8, 7), null, null, null, 42.50m,
            LabelsMis.Domain.ValueObjects.ShippingAddress.Empty, UserId, Now);

        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(), "INV-2026-0042", customer.Id, order.Id, null,
            new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 25),
            617.00m, 50.90m, 42.50m, "Thanks for your business!", UserId, Now);
        SetNavigation(invoice, nameof(Invoice.Customer), customer);
        SetNavigation(invoice, nameof(Invoice.SalesOrder), order);

        for (var i = 1; i <= 2; i++)
        {
            invoice.AddLine(InvoiceLine.Create(
                Guid.NewGuid(), invoice.Id, i, null, null,
                $"4×3 matte BOPP label — design {i}", 2500 * i, 0.1234m, "TAX", UserId, Now));
        }

        var payment = Payment.Create(
            Guid.NewGuid(), invoice.Id, new DateOnly(2026, 7, 25), 200m,
            PaymentMethod.Check, "CHK 1042", null, UserId, Now);
        invoice.RecordPayment(payment);

        var detail = new LabelsMis.Web.Services.Invoices.InvoiceDetail(
            invoice, customer.Name, order.Id, order.OrderNumber, [payment]);

        var bytes = await generator.GenerateBytesAsync(detail);

        AssertLoadablePdf(bytes, minPages: 1, "invoice.pdf");
    }

    private static GeneralSettings BrandedSettings()
    {
        var settings = GeneralSettings.CreateDefault(UserId, Now);
        settings.Update(
            "Printing Solutions AZ", "2120 W Broadway Rd", null, "Mesa", "AZ", "85202",
            "(480) 555-0142", "orders@printingsolutionsaz.com", "printingsolutionsaz.com",
            "Prices valid for 30 days. 10% over/under constitutes a complete order.",
            0.0825m, UserId, Now);
        settings.SetLogo(TinyPng, "image/png", UserId, Now);
        return settings;
    }

    private static void AssertLoadablePdf(byte[] bytes, int minPages, string fileName)
    {
        bytes.Should().NotBeEmpty();
        bytes.Take(5).Should().Equal("%PDF-"u8.ToArray());

        using var pdf = PdfDocument.Load(bytes);
        pdf.PageCount.Should().BeGreaterThanOrEqualTo(minPages);
        pdf.UsedRecoveryScan.Should().BeFalse("the serializer should emit a well-formed xref");

        var outDir = Environment.GetEnvironmentVariable("PDF_SMOKE_OUT");
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, fileName), bytes);
        }
    }

    private static void SetNavigation(object entity, string propertyName, object value) =>
        entity.GetType().GetProperty(propertyName)!.SetValue(entity, value);

    /// <summary>These tests render bytes only and never persist, so the storage is never touched.</summary>
    private static LabelsMis.Web.Services.Pdfs.TempPdfStorage UnusedPdfStorage() => new(null!);

    /// <summary>Bypasses the DbContext so generators can run against fixed branding.</summary>
    private sealed class StubGeneralSettingsService(GeneralSettings? settings) : GeneralSettingsService(null!, null!)
    {
        public override Task<GeneralSettings?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }
}
