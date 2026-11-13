using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services;

/// <summary>
/// Hands out document numbers (<c>INV-2026-00549</c>). The year in the middle is the year the
/// document is made; the number at the end keeps counting from year to year and never starts
/// again, so it identifies the document by itself (QuickBooks can be sent it without the rest).
/// </summary>
public class DocumentNumberService(LabelsMisDbContext db, TimeProvider? time = null)
{
    public const string EstimateType = "EST";
    public const string SalesOrderType = "SO";
    public const string JobType = "JOB";
    public const string PurchaseOrderType = "PO";
    public const string RollType = "ROLL";
    public const string ShipmentType = "SHP";
    public const string InvoiceType = "INV";

    public async Task<string> NextEstimateNumberAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(EstimateType, "EST", cancellationToken);

    public async Task<string> NextSalesOrderNumberAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(SalesOrderType, "SO", cancellationToken);

    public async Task<string> NextJobNumberAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(JobType, "JOB", cancellationToken);

    public async Task<string> NextPurchaseOrderNumberAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(PurchaseOrderType, "PO", cancellationToken);

    public async Task<string> NextRollBarcodeAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(RollType, "R", cancellationToken);

    public async Task<string> NextShipmentNumberAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(ShipmentType, "SHP", cancellationToken);

    public async Task<string> NextInvoiceNumberAsync(CancellationToken cancellationToken = default) =>
        await NextNumberAsync(InvoiceType, "INV", cancellationToken);

    private async Task<string> NextNumberAsync(
        string documentType,
        string prefix,
        CancellationToken cancellationToken)
    {
        var year = (time ?? TimeProvider.System).GetUtcNow().Year;
        var sequence = await db.DocumentSequences
            .FirstOrDefaultAsync(s => s.DocumentType == documentType && s.Year == year, cancellationToken);

        if (sequence is null)
        {
            // The first document of a year: carry on from the highest number this type has reached.
            var reached = await db.DocumentSequences
                .Where(s => s.DocumentType == documentType)
                .MaxAsync(s => (int?)s.LastNumber, cancellationToken) ?? 0;
            sequence = DocumentSequence.Create(Guid.NewGuid(), documentType, year, reached);
            db.DocumentSequences.Add(sequence);
        }

        var number = sequence.NextFormattedNumber(prefix);
        await db.SaveChangesAsync(cancellationToken);
        return number;
    }
}
