using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>The cached copy of the connected company's products/services, classes and payment
/// terms (<see cref="Domain.Entities.QuickBooksCatalogEntry"/>).</summary>
public class QuickBooksCatalogService(LabelsMisDbContext db, QuickBooksGateway gateway, ICurrentUserService currentUser)
{
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
}
