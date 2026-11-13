using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>What one "Refresh the lists" did.</summary>
public sealed record QuickBooksCatalogRefreshResult(int Items, int Classes, int Terms, int Added, int Removed, DateTime RefreshedAt);

/// <summary>How many entries of each list the cache holds.</summary>
public sealed record QuickBooksCatalogCounts(int Items, int Classes, int Terms)
{
    public bool IsEmpty => Items + Classes + Terms == 0;
}

/// <summary>What "Find the PO field" found: the custom fields on the recent invoices, or, when
/// none carried one, the legacy fields switched on in the company's settings.</summary>
public sealed record QuickBooksCustomFieldSearch(
    IReadOnlyList<QuickBooksCustomFieldDefinition> Fields,
    int InvoicesRead,
    bool FromPreferences)
{
    public QuickBooksCustomFieldDefinition? Suggested => QuickBooksCustomFieldParser.Suggest(Fields);
}

/// <summary>The cached copy of the connected company's products/services, classes and payment
/// terms (<see cref="Domain.Entities.QuickBooksCatalogEntry"/>).</summary>
public class QuickBooksCatalogService(LabelsMisDbContext db, QuickBooksGateway gateway, ICurrentUserService currentUser)
{
    /// <summary>Rows per query page; Intuit's maximum.</summary>
    public const int PageSize = 1000;

    /// <summary>How many of the most recently changed invoices "Find the PO field" reads.</summary>
    public const int RecentInvoiceCount = 100;

    private static readonly QuickBooksCatalogKind[] Kinds =
        [QuickBooksCatalogKind.Item, QuickBooksCatalogKind.Class, QuickBooksCatalogKind.Term];

    private static readonly IReadOnlyDictionary<string, string> EnhancedCustomFields =
        new Dictionary<string, string> { ["include"] = "enhancedAllCustomFields" };

    private readonly LabelsMisDbContext _db = db;
    private readonly QuickBooksGateway _gateway = gateway;
    private readonly ICurrentUserService _currentUser = currentUser;

    /// <summary>The cache as a lookup: names to QuickBooks ids. Empty until the lists have been
    /// refreshed once.</summary>
    public async Task<QuickBooksCatalogLookup> GetLookupAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _db.QuickBooksCatalogEntries
            .AsNoTracking()
            .Select(e => new QuickBooksListEntry(e.Kind, e.QuickBooksId, e.Name, e.FullName, e.Taxable))
            .ToListAsync(cancellationToken);
        return new QuickBooksCatalogLookup(entries);
    }

    /// <summary>When the lists were last read from QuickBooks; null when never.</summary>
    public Task<DateTime?> GetRefreshedAtAsync(CancellationToken cancellationToken = default) =>
        _db.QuickBooksCatalogEntries.MaxAsync(e => (DateTime?)e.RefreshedAt, cancellationToken);

    public async Task<QuickBooksCatalogCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _db.QuickBooksCatalogEntries.AsNoTracking()
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Kind, g => g.Count, cancellationToken);
        return new QuickBooksCatalogCounts(
            counts.GetValueOrDefault(QuickBooksCatalogKind.Item),
            counts.GetValueOrDefault(QuickBooksCatalogKind.Class),
            counts.GetValueOrDefault(QuickBooksCatalogKind.Term));
    }

    /// <summary>Reads the company's products/services, classes and payment terms (every page) and
    /// makes the cache match: entries are updated in place by QuickBooks id, new ones added, and
    /// ones QuickBooks no longer returns (deleted or made inactive) removed. All three lists are
    /// read before anything is saved, so a failed read leaves the cache as it was.</summary>
    /// <exception cref="QuickBooksNotConnectedException">No company is connected.</exception>
    /// <exception cref="QuickBooksApiException">QuickBooks refused or failed a read.</exception>
    public async Task<QuickBooksCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
        var read = new Dictionary<QuickBooksCatalogKind, IReadOnlyList<QuickBooksListEntry>>();
        foreach (var kind in Kinds)
        {
            read[kind] = await ReadListAsync(kind, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var cached = await _db.QuickBooksCatalogEntries.ToListAsync(cancellationToken);
        var byKey = cached.ToDictionary(e => (e.Kind, e.QuickBooksId));
        var seen = new HashSet<(QuickBooksCatalogKind, string)>();
        var added = 0;
        foreach (var entry in read.Values.SelectMany(list => list))
        {
            var key = (entry.Kind, entry.Id.Trim());
            if (!seen.Add(key))
            {
                continue;
            }

            if (byKey.TryGetValue(key, out var existing))
            {
                existing.Refresh(entry.Name, entry.FullName, entry.Taxable, userId, now);
            }
            else
            {
                _db.QuickBooksCatalogEntries.Add(QuickBooksCatalogEntry.Create(
                    Guid.NewGuid(), entry.Kind, entry.Id, entry.Name, entry.FullName, entry.Taxable, userId, now));
                added++;
            }
        }

        var gone = cached.Where(e => !seen.Contains((e.Kind, e.QuickBooksId))).ToList();
        _db.QuickBooksCatalogEntries.RemoveRange(gone);
        await _db.SaveChangesAsync(cancellationToken);

        return new QuickBooksCatalogRefreshResult(
            seen.Count(k => k.Item1 == QuickBooksCatalogKind.Item),
            seen.Count(k => k.Item1 == QuickBooksCatalogKind.Class),
            seen.Count(k => k.Item1 == QuickBooksCatalogKind.Term),
            added,
            gone.Count,
            now);
    }

    /// <summary>Reads the most recently changed invoices with their custom fields and lists the
    /// fields found. A field shows only on an invoice that has a value in it, so when none of them
    /// carries one, the legacy fields from the company's settings are listed instead (often none:
    /// then the PO box has to be filled in by hand on one invoice in QuickBooks first).</summary>
    /// <exception cref="QuickBooksNotConnectedException">No company is connected.</exception>
    /// <exception cref="QuickBooksApiException">QuickBooks refused or failed a read.</exception>
    public async Task<QuickBooksCustomFieldSearch> FindCustomFieldsAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await _gateway.QueryAsync(
            $"select * from Invoice orderby MetaData.LastUpdatedTime desc maxresults {RecentInvoiceCount}",
            QuickBooksOperations.PoFieldFind,
            parameters: EnhancedCustomFields,
            cancellationToken: cancellationToken);
        var (fields, invoicesRead) = QuickBooksCustomFieldParser.FromInvoices(invoices.Body);
        if (fields.Count > 0)
        {
            return new QuickBooksCustomFieldSearch(fields, invoicesRead, FromPreferences: false);
        }

        var preferences = await _gateway.ReadAsync(
            "preferences", QuickBooksOperations.PoFieldFind, cancellationToken: cancellationToken);
        return new QuickBooksCustomFieldSearch(
            QuickBooksCustomFieldParser.FromPreferences(preferences.Body), invoicesRead, FromPreferences: true);
    }

    private async Task<IReadOnlyList<QuickBooksListEntry>> ReadListAsync(QuickBooksCatalogKind kind, CancellationToken cancellationToken)
    {
        var entries = new List<QuickBooksListEntry>();
        for (var start = 1; ; start += PageSize)
        {
            var response = await _gateway.QueryAsync(
                $"select * from {kind} STARTPOSITION {start} MAXRESULTS {PageSize}",
                QuickBooksOperations.ListsRefresh,
                cancellationToken: cancellationToken);
            var (page, rowCount) = QuickBooksCatalogParser.ParsePage(kind, response.Body);
            entries.AddRange(page);
            if (rowCount < PageSize)
            {
                return entries;
            }
        }
    }
}
