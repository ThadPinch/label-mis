using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
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

/// <summary>The names offered in the preview's per-line item and class dropdowns.</summary>
public record QbLineOptions(IReadOnlyList<string> Items, IReadOnlyList<string> Classes);

/// <summary>Setup → QuickBooks mapping, and the helper that returns (item, class) for any
/// invoice line (<see cref="QuickBooksMappingRules"/>).</summary>
public class QuickBooksMappingService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    /// <summary>The saved mapping, or the defaults until Setup → QuickBooks is first saved.</summary>
    public async Task<QuickBooksMapping> GetMappingAsync(CancellationToken cancellationToken = default) =>
        (await db.QuickBooksSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToMapping()
        ?? QuickBooksMapping.Default;

    /// <summary>Saves the mapping. A name that matches a cached QuickBooks entry apart from case
    /// or parents is saved as QuickBooks spells it; a name QuickBooks doesn't have is saved as
    /// typed (the page flags it).</summary>
    public async Task UpdateAsync(QuickBooksMapping mapping, CancellationToken cancellationToken = default)
    {
        var lookup = await LoadLookupAsync(cancellationToken);
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.Update(QuickBooksMappingSlots.Normalise(mapping, lookup), UserId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The terms map, PO field and send setting, or the defaults until first saved.</summary>
    public async Task<QuickBooksPushSettings> GetPushSettingsAsync(CancellationToken cancellationToken = default) =>
        (await db.QuickBooksSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToPushSettings()
        ?? QuickBooksPushSettings.Default;

    public async Task UpdatePushAsync(QuickBooksPushSettings push, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.UpdatePush(push, UserId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Saves the terms map, the send setting and the invoice number format; the PO field
    /// stays as it is.</summary>
    public async Task SaveSendingAsync(
        IReadOnlyDictionary<PaymentTerms, string> terms,
        QuickBooksSendMode sendMode,
        QuickBooksInvoiceNumberFormat numberFormat,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.UpdatePush(
            settings.ToPushSettings() with { Terms = terms, SendMode = sendMode, NumberFormat = numberFormat }, UserId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Saves the PO field only (from "Find the PO field"); the terms map and the send
    /// setting stay as they are. A blank id clears it.</summary>
    public async Task SetPoFieldAsync(string? fieldId, string? fieldName, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.UpdatePush(settings.ToPushSettings() with { PoFieldId = fieldId, PoFieldName = fieldName }, UserId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The preview's item and class choices: the cached QuickBooks lists once they have
    /// been read, else the names the mapping holds.</summary>
    public async Task<QbLineOptions> GetLineOptionsAsync(QuickBooksMapping mapping, CancellationToken cancellationToken = default)
    {
        var lookup = await LoadLookupAsync(cancellationToken);
        return new QbLineOptions(
            lookup.HasAny(QuickBooksCatalogKind.Item) ? lookup.Names(QuickBooksCatalogKind.Item) : mapping.Items,
            lookup.HasAny(QuickBooksCatalogKind.Class) ? lookup.Names(QuickBooksCatalogKind.Class) : mapping.Classes);
    }

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");

    private async Task<QuickBooksSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.QuickBooksSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = QuickBooksSettings.CreateDefault(UserId, DateTime.UtcNow);
            db.QuickBooksSettings.Add(settings);
        }

        return settings;
    }

    /// <summary>The cached lists (same reading as <see cref="QuickBooksCatalogService.GetLookupAsync"/>,
    /// which needs the QuickBooks connection services this one doesn't).</summary>
    private async Task<QuickBooksCatalogLookup> LoadLookupAsync(CancellationToken cancellationToken) =>
        new(await db.QuickBooksCatalogEntries.AsNoTracking()
            .Where(e => e.Kind == QuickBooksCatalogKind.Item || e.Kind == QuickBooksCatalogKind.Class)
            .Select(e => new QuickBooksListEntry(e.Kind, e.QuickBooksId, e.Name, e.FullName, e.Taxable))
            .ToListAsync(cancellationToken));

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
                    .Select(l => new { l.Id, l.LineNumber, l.SalesOrderLineId, l.Description, l.QbItemOverride, l.QbClassOverride })
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
                ? QuickBooksMappingRules.MapProductLine(product, mapping, l.QbItemOverride, l.QbClassOverride)
                : QuickBooksMappingRules.MapChargeLine(l.Description, jobClass, mapping, l.QbItemOverride, l.QbClassOverride);
            return new InvoiceLineQbMapping(l.Id, l.LineNumber, product is not null, mapped);
        }).ToList();

        return new InvoiceQbMapping(lines, QuickBooksMappingRules.MapShippingLine(jobClass, mapping), jobClass, mapping);
    }
}
