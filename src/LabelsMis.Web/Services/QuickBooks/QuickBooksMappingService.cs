using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>One invoice line's QuickBooks item and class.</summary>
public record InvoiceLineQbMapping(Guid InvoiceLineId, int LineNumber, bool IsProductLine, QbLineMapping Mapping);

/// <summary>Every line of an invoice mapped to QuickBooks, plus the shipping line.</summary>
public record InvoiceQbMapping(
    IReadOnlyList<InvoiceLineQbMapping> Lines,
    QbLineMapping Shipping,
    string JobClass,
    QuickBooksMapping Mapping);

/// <summary>Setup → QuickBooks mapping, and the helper that returns (item, class) for any
/// invoice line (<see cref="QuickBooksMappingRules"/>).</summary>
public class QuickBooksMappingService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    /// <summary>The saved mapping, or the defaults until Setup → QuickBooks is first saved.</summary>
    public async Task<QuickBooksMapping> GetMappingAsync(CancellationToken cancellationToken = default) =>
        (await db.QuickBooksSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToMapping()
        ?? QuickBooksMapping.Default;

    public async Task UpdateAsync(QuickBooksMapping mapping, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var now = DateTime.UtcNow;
        var settings = await db.QuickBooksSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = QuickBooksSettings.CreateDefault(userId, now);
            db.QuickBooksSettings.Add(settings);
        }

        settings.Update(mapping, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>(item, class) for every line of the invoice and its shipping line; null when the
    /// invoice doesn't exist.</summary>
    public async Task<InvoiceQbMapping?> MapInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new
            {
                i.SalesOrderId,
                Lines = i.Lines
                    .OrderBy(l => l.LineNumber)
                    .Select(l => new { l.Id, l.LineNumber, l.SalesOrderLineId, l.Description })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var mapping = await GetMappingAsync(cancellationToken);
        var orderLines = await db.SalesOrderLines.AsNoTracking()
            .Where(l => l.SalesOrderId == invoice.SalesOrderId)
            .OrderBy(l => l.LineNumber)
            .Select(l => new
            {
                l.Id,
                SubstrateId = l.Spec != null ? l.Spec.SubstrateId : l.Product.SubstrateId,
                Outsourced = l.OutsourcedItem != null
            })
            .ToListAsync(cancellationToken);
        var substrateIds = orderLines.Select(l => l.SubstrateId).Distinct().ToList();
        var substrates = await db.Stocks.AsNoTracking()
            .Where(s => substrateIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.Code, s.StockType }, cancellationToken);

        var facts = orderLines.ToDictionary(
            l => l.Id,
            l => new QbProductFacts(
                substrates.GetValueOrDefault(l.SubstrateId)?.Code,
                substrates.GetValueOrDefault(l.SubstrateId)?.StockType,
                l.Outsourced));
        var jobClass = QuickBooksMappingRules.JobClass(orderLines.Select(l => facts[l.Id]), mapping);

        var lines = invoice.Lines.Select(l =>
        {
            var product = l.SalesOrderLineId is Guid lineId ? facts.GetValueOrDefault(lineId) : null;
            var mapped = product is not null
                ? QuickBooksMappingRules.MapProductLine(product, mapping)
                : QuickBooksMappingRules.MapChargeLine(l.Description, jobClass, mapping);
            return new InvoiceLineQbMapping(l.Id, l.LineNumber, product is not null, mapped);
        }).ToList();

        return new InvoiceQbMapping(lines, QuickBooksMappingRules.MapShippingLine(jobClass, mapping), jobClass, mapping);
    }
}
