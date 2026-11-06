using System.Linq.Expressions;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;
using LabelsMis.Domain.Search;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Jobs;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Search;

/// <summary>Colour role of a hit's badge; the search bar maps it to a CSS class.</summary>
public enum SearchTone
{
    Neutral,
    Accent,
    Info,
    Success,
    Warning,
    Danger,
    Muted
}

public sealed record SearchHit(
    SearchCategory Category,
    Guid Id,
    string Title,
    string? Subtitle,
    string? Badge,
    SearchTone Tone,
    string Url,
    int Score);

public sealed record SearchGroup(
    SearchCategory Category,
    string Label,
    IReadOnlyList<SearchHit> Hits,
    bool HasMore,
    string? SeeAllUrl);

public sealed record SearchResults(SearchQuery Query, IReadOnlyList<SearchGroup> Groups)
{
    public static SearchResults Empty { get; } = new(SearchQuery.Empty, []);

    public int HitCount => Groups.Sum(g => g.Hits.Count);

    /// <summary>
    /// The one record the query names, if it names exactly one — a full document number
    /// (JOB-2026-00052), or a prefixed number unique in its category ("job 52"). The search bar
    /// opens it on Enter instead of listing results.
    /// </summary>
    public SearchHit? JumpTarget
    {
        get
        {
            var exact = Groups.SelectMany(g => g.Hits).Where(h => h.Score >= SearchRanker.ExactScore).ToList();
            if (exact.Count == 1)
            {
                return exact[0];
            }

            if (exact.Count == 0 && Query.Hint is { } hint)
            {
                var hinted = Groups
                    .Where(g => g.Category == hint)
                    .SelectMany(g => g.Hits)
                    .Where(h => h.Score >= SearchRanker.NumberSuffixScore)
                    .ToList();
                return hinted.Count == 1 ? hinted[0] : null;
            }

            return null;
        }
    }
}

/// <summary>
/// The global search bar: one query across customers, orders, jobs, estimates, invoices, shipments,
/// products, purchase orders, rolls, dies, stocks and suppliers. Each category runs one query
/// (every term must match one of its fields), the hits are scored by <see cref="SearchRanker"/>,
/// and categories come back best match first — the one a document prefix names ("SO 52") leads.
/// The caller passes the categories the user may open; nothing else is queried.
/// </summary>
public class GlobalSearchService(LabelsMisDbContext db, JobFloorService floor, LinkGenerator links)
{
    /// <summary>A label-size query ("2x3") matches dies and products within this many inches.</summary>
    public const decimal SizeToleranceIn = 0.125m;

    public static string CategoryLabel(SearchCategory category) => category switch
    {
        SearchCategory.Customer => "Customers",
        SearchCategory.SalesOrder => "Sales orders",
        SearchCategory.Job => "Jobs",
        SearchCategory.Estimate => "Estimates",
        SearchCategory.Invoice => "Invoices",
        SearchCategory.Shipment => "Shipments",
        SearchCategory.Product => "Products",
        SearchCategory.PurchaseOrder => "Purchase orders",
        SearchCategory.Roll => "Rolls",
        SearchCategory.Die => "Dies",
        SearchCategory.Stock => "Stocks",
        SearchCategory.Supplier => "Suppliers",
        _ => category.ToString()
    };

    /// <summary>Kebab-case key the search bar and the results page use for a category.</summary>
    public static string CategoryKey(SearchCategory category) => category switch
    {
        SearchCategory.SalesOrder => "sales-order",
        SearchCategory.PurchaseOrder => "purchase-order",
        _ => category.ToString().ToLowerInvariant()
    };

    public static SearchCategory? ParseCategoryKey(string? key) =>
        Enum.GetValues<SearchCategory>().Cast<SearchCategory?>()
            .FirstOrDefault(c => string.Equals(CategoryKey(c!.Value), key, StringComparison.OrdinalIgnoreCase));

    public async Task<SearchResults> SearchAsync(
        string? text,
        IReadOnlySet<SearchCategory> categories,
        int perCategory,
        CancellationToken cancellationToken = default)
    {
        var query = SearchQuery.Parse(text);
        if (query.IsEmpty || categories.Count == 0)
        {
            return new SearchResults(query, []);
        }

        perCategory = Math.Clamp(perCategory, 1, 100);
        var groups = new List<SearchGroup>();
        foreach (var category in Enum.GetValues<SearchCategory>().Where(categories.Contains))
        {
            // One DbContext, so the categories run one after another (each is a single query).
            var group = category switch
            {
                SearchCategory.Customer => await CustomersAsync(query, perCategory, cancellationToken),
                SearchCategory.SalesOrder => await SalesOrdersAsync(query, perCategory, cancellationToken),
                SearchCategory.Job => await JobsAsync(query, perCategory, cancellationToken),
                SearchCategory.Estimate => await EstimatesAsync(query, perCategory, cancellationToken),
                SearchCategory.Invoice => await InvoicesAsync(query, perCategory, cancellationToken),
                SearchCategory.Shipment => await ShipmentsAsync(query, perCategory, cancellationToken),
                SearchCategory.Product => await ProductsAsync(query, perCategory, cancellationToken),
                SearchCategory.PurchaseOrder => await PurchaseOrdersAsync(query, perCategory, cancellationToken),
                SearchCategory.Roll => await RollsAsync(query, perCategory, cancellationToken),
                SearchCategory.Die => await DiesAsync(query, perCategory, cancellationToken),
                SearchCategory.Stock => await StocksAsync(query, perCategory, cancellationToken),
                SearchCategory.Supplier => await SuppliersAsync(query, perCategory, cancellationToken),
                _ => null
            };

            if (group is { Hits.Count: > 0 })
            {
                groups.Add(group);
            }
        }

        var ordered = groups
            .OrderByDescending(g => g.Category == query.Hint)
            .ThenByDescending(g => g.Hits.Max(h => h.Score))
            .ThenBy(g => g.Category)
            .ToList();
        return new SearchResults(query, ordered);
    }

    // ---- Categories -------------------------------------------------------------------------

    private async Task<SearchGroup> CustomersAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var openStatuses = new[] { SalesOrderStatus.Open, SalesOrderStatus.InProduction };
        var rows = await AllTerms(db.Customers.AsNoTracking(), q, term => c =>
                c.Name.ToUpper().Contains(term)
                || c.Code.ToUpper().Contains(term)
                || c.Contacts.Any(k =>
                    (k.FirstName + " " + k.LastName).ToUpper().Contains(term)
                    || (k.Email != null && k.Email.ToUpper().Contains(term))
                    || (k.Phone != null && k.Phone.Contains(term))))
            .OrderByDescending(c => c.Code.ToUpper() == whole || c.Name.ToUpper() == whole)
            .ThenByDescending(c => c.Name.ToUpper().StartsWith(first))
            .ThenByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Take(limit + 1)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Code,
                c.IsActive,
                c.Status,
                Contacts = c.Contacts
                    .OrderByDescending(k => k.IsPrimary)
                    .Select(k => new { Name = k.FirstName + " " + k.LastName, k.Email, k.Phone, k.IsPrimary })
                    .ToList(),
                OpenOrders = db.SalesOrders.Count(o => o.CustomerId == c.Id && openStatuses.Contains(o.Status))
            })
            .ToListAsync(ct);

        var hits = rows.Select(c =>
        {
            var contactTexts = c.Contacts.SelectMany(k => new[] { k.Name, k.Email, k.Phone });
            var matchedContact = c.Contacts.FirstOrDefault(k =>
                q.Terms.Any(t => SearchRanker.Match(k.Name, t) != SearchMatch.None
                    || SearchRanker.Match(k.Email, t) != SearchMatch.None
                    || SearchRanker.Match(k.Phone, t) != SearchMatch.None));
            var shownContact = matchedContact ?? c.Contacts.FirstOrDefault(k => k.IsPrimary);
            var contactText = shownContact is null
                ? null
                : Join(matchedContact is null ? shownContact.Name.Trim() : $"Contact {shownContact.Name.Trim()}", shownContact.Email);

            var (badge, tone) = !c.IsActive
                ? ("Inactive", SearchTone.Muted)
                : c.Status == CustomerStatus.Prospect
                    ? ("Prospect", SearchTone.Info)
                    : c.OpenOrders > 0
                        ? ($"{c.OpenOrders} open order{(c.OpenOrders == 1 ? "" : "s")}", SearchTone.Accent)
                        : ((string?)null, SearchTone.Neutral);

            return new SearchHit(
                SearchCategory.Customer, c.Id, c.Name, Join(c.Code, contactText), badge, tone,
                Page("/Customers/Edit", new { id = c.Id }),
                SearchRanker.Score(q, c.Code, c.Name, contactTexts) - (c.IsActive ? 0 : 5));
        });

        return Group(SearchCategory.Customer, hits, rows.Count > limit, limit, Page("/Customers/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> SalesOrdersAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var rows = await AllTerms(db.SalesOrders.AsNoTracking(), q, term => o =>
                o.OrderNumber.ToUpper().Contains(term)
                || (o.CustomerPoNumber != null && o.CustomerPoNumber.ToUpper().Contains(term))
                || (o.ShipToName != null && o.ShipToName.ToUpper().Contains(term))
                || o.Customer.Name.ToUpper().Contains(term)
                || o.Lines.Any(l => (l.Description ?? l.Product.Description).ToUpper().Contains(term)))
            .OrderByDescending(NumberFirst<Domain.Entities.SalesOrder>(o => o.OrderNumber, whole, suffix))
            .ThenByDescending(o => o.OrderedAt)
            .Take(limit + 1)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.CustomerPoNumber,
                o.ShipToName,
                o.Status,
                o.RequestedShipDate,
                CustomerName = o.Customer.Name,
                LineCount = o.Lines.Count,
                FirstLine = o.Lines.OrderBy(l => l.LineNumber).Select(l => l.Description ?? l.Product.Description).FirstOrDefault(),
                LineTexts = o.Lines.OrderBy(l => l.LineNumber).Select(l => l.Description ?? l.Product.Description).Take(10).ToList()
            })
            .ToListAsync(ct);

        var hits = rows.Select(o => new SearchHit(
            SearchCategory.SalesOrder, o.Id, o.OrderNumber,
            Join(o.CustomerName,
                string.IsNullOrWhiteSpace(o.CustomerPoNumber) ? null : $"PO {o.CustomerPoNumber}",
                LineSummary(o.FirstLine, o.LineCount),
                o.RequestedShipDate is { } ship ? $"ship {ship:MMM d}" : null),
            SalesOrderStatusLabel(o.Status),
            o.Status switch
            {
                SalesOrderStatus.Open => SearchTone.Info,
                SalesOrderStatus.InProduction => SearchTone.Accent,
                SalesOrderStatus.Shipped => SearchTone.Success,
                SalesOrderStatus.Cancelled => SearchTone.Danger,
                _ => SearchTone.Muted
            },
            Page("/SalesOrders/Edit", new { id = o.Id }),
            SearchRanker.Score(q, o.OrderNumber, null, [o.CustomerPoNumber, o.ShipToName], [o.CustomerName, .. o.LineTexts])));

        return Group(SearchCategory.SalesOrder, hits, rows.Count > limit, limit, Page("/SalesOrders/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> JobsAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var live = JobService.LiveStatuses.ToList();
        var candidates = await AllTerms(db.Jobs.AsNoTracking(), q, term => j =>
                j.JobNumber.ToUpper().Contains(term)
                || j.Product.Description.ToUpper().Contains(term)
                || j.Product.InternalSku.ToUpper().Contains(term)
                || (j.Product.CustomerSku != null && j.Product.CustomerSku.ToUpper().Contains(term))
                || (j.SalesOrderLine.Description != null && j.SalesOrderLine.Description.ToUpper().Contains(term))
                || j.SalesOrderLine.SalesOrder.OrderNumber.ToUpper().Contains(term)
                || (j.SalesOrderLine.SalesOrder.CustomerPoNumber != null && j.SalesOrderLine.SalesOrder.CustomerPoNumber.ToUpper().Contains(term))
                || j.SalesOrderLine.SalesOrder.Customer.Name.ToUpper().Contains(term))
            .OrderByDescending(NumberFirst<Domain.Entities.Job>(j => j.JobNumber, whole, suffix))
            .ThenByDescending(j => live.Contains(j.Status))
            .ThenByDescending(j => j.CreatedAt)
            .Take(limit + 1)
            .Select(j => j.Id)
            .ToListAsync(ct);

        var jobs = await floor.GetAsync(candidates, ct);
        var hits = jobs.Select(j => new SearchHit(
                SearchCategory.Job, j.Id, j.JobNumber,
                Join(j.CustomerName, j.ProductDescription, j.OrderNumber,
                    j.IsLive && j.DueDate is { } due ? $"due {due:MMM d}" : null),
                JobBadge(j.Location),
                JobTone(j),
                Page("/Jobs/Detail", new { id = j.Id }),
                SearchRanker.Score(q, j.JobNumber, j.ProductDescription,
                    [j.ProductSku, j.CustomerSku, j.OrderNumber, j.CustomerPoNumber], [j.CustomerName])))
            .ToList();

        // Live jobs ahead of shipped/closed ones at the same score: "where is it" is about the floor.
        var liveIds = jobs.Where(j => j.IsLive).Select(j => j.Id).ToHashSet();
        var created = jobs.ToDictionary(j => j.Id, j => j.CreatedAt);
        var ordered = hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => liveIds.Contains(h.Id))
            .ThenByDescending(h => created[h.Id])
            .Take(limit)
            .ToList();

        var seeAll = jobs.Any(j => !j.IsLive)
            ? Page("/Jobs/Index", new { Search = q.Text, StatusFilter = "all", view = "list" })
            : Page("/Jobs/Index", new { Search = q.Text });
        return new SearchGroup(SearchCategory.Job, CategoryLabel(SearchCategory.Job), ordered, candidates.Count > limit, seeAll);
    }

    private async Task<SearchGroup> EstimatesAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var rows = await AllTerms(db.Estimates.AsNoTracking(), q, term => e =>
                e.EstimateNumber.ToUpper().Contains(term)
                || e.Customer.Name.ToUpper().Contains(term)
                || (e.ContactEmail != null && e.ContactEmail.ToUpper().Contains(term))
                || e.Lines.Any(l => l.ProductDescription.ToUpper().Contains(term)))
            .OrderByDescending(NumberFirst<Domain.Entities.Estimate>(e => e.EstimateNumber, whole, suffix))
            .ThenByDescending(e => e.CreatedAt)
            .Take(limit + 1)
            .Select(e => new
            {
                e.Id,
                e.EstimateNumber,
                e.RevisionNumber,
                e.Status,
                e.ContactEmail,
                e.CreatedAt,
                CustomerName = e.Customer.Name,
                LineCount = e.Lines.Count,
                FirstLine = e.Lines.OrderBy(l => l.LineNumber).Select(l => l.ProductDescription).FirstOrDefault(),
                LineTexts = e.Lines.OrderBy(l => l.LineNumber).Select(l => l.ProductDescription).Take(10).ToList()
            })
            .ToListAsync(ct);

        var hits = rows.Select(e => new SearchHit(
            SearchCategory.Estimate, e.Id,
            e.RevisionNumber > 1 ? $"{e.EstimateNumber} rev {e.RevisionNumber}" : e.EstimateNumber,
            Join(e.CustomerName, LineSummary(e.FirstLine, e.LineCount), e.CreatedAt.ToLocalTime().ToString("MMM d, yyyy")),
            e.Status.ToString(),
            e.Status switch
            {
                EstimateStatus.Sent => SearchTone.Info,
                EstimateStatus.Won => SearchTone.Success,
                EstimateStatus.Lost => SearchTone.Danger,
                EstimateStatus.Draft => SearchTone.Neutral,
                _ => SearchTone.Muted
            },
            Page("/Estimates/Edit", new { id = e.Id }),
            SearchRanker.Score(q, e.EstimateNumber, null, [e.ContactEmail, .. e.LineTexts], [e.CustomerName])));

        return Group(SearchCategory.Estimate, hits, rows.Count > limit, limit, Page("/Estimates/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> InvoicesAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var rows = await AllTerms(db.Invoices.AsNoTracking(), q, term => i =>
                i.InvoiceNumber.ToUpper().Contains(term)
                || i.SalesOrder.OrderNumber.ToUpper().Contains(term)
                || (i.SalesOrder.CustomerPoNumber != null && i.SalesOrder.CustomerPoNumber.ToUpper().Contains(term))
                || (i.QbInvoiceId != null && i.QbInvoiceId.ToUpper().Contains(term))
                || i.Customer.Name.ToUpper().Contains(term))
            .OrderByDescending(NumberFirst<Domain.Entities.Invoice>(i => i.InvoiceNumber, whole, suffix))
            .ThenByDescending(i => i.InvoiceDate)
            .Take(limit + 1)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.Status,
                i.Total,
                i.BalanceDue,
                i.QbExportedAt,
                i.QbInvoiceId,
                CustomerName = i.Customer.Name,
                i.SalesOrder.OrderNumber,
                i.SalesOrder.CustomerPoNumber
            })
            .ToListAsync(ct);

        var hits = rows.Select(i => new SearchHit(
            SearchCategory.Invoice, i.Id, i.InvoiceNumber,
            Join(i.CustomerName, i.OrderNumber, i.Total.ToString("C"),
                i.Status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid && i.BalanceDue > 0 ? $"{i.BalanceDue:C} due" : null,
                i.QbExportedAt is not null ? "in QuickBooks" : null),
            i.Status == InvoiceStatus.PartiallyPaid ? "Partially paid" : i.Status.ToString(),
            i.Status switch
            {
                InvoiceStatus.Sent => SearchTone.Info,
                InvoiceStatus.PartiallyPaid => SearchTone.Warning,
                InvoiceStatus.Paid => SearchTone.Success,
                InvoiceStatus.Void => SearchTone.Danger,
                _ => SearchTone.Neutral
            },
            Page("/Invoices/Detail", new { id = i.Id }),
            SearchRanker.Score(q, i.InvoiceNumber, null, [i.OrderNumber, i.CustomerPoNumber, i.QbInvoiceId], [i.CustomerName])));

        return Group(SearchCategory.Invoice, hits, rows.Count > limit, limit, Page("/Invoices/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> ShipmentsAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var rows = await AllTerms(db.Shipments.AsNoTracking(), q, term => s =>
                s.ShipmentNumber.ToUpper().Contains(term)
                || s.SalesOrder.OrderNumber.ToUpper().Contains(term)
                || s.SalesOrder.Customer.Name.ToUpper().Contains(term)
                || (s.ShipToName != null && s.ShipToName.ToUpper().Contains(term))
                || s.Packages.Any(p => p.TrackingNumber != null && p.TrackingNumber.ToUpper().Contains(term)))
            .OrderByDescending(NumberFirst<Domain.Entities.Shipment>(s => s.ShipmentNumber, whole, suffix))
            .ThenByDescending(s => s.ShipDate)
            .Take(limit + 1)
            .Select(s => new
            {
                s.Id,
                s.ShipmentNumber,
                s.Status,
                s.ShipDate,
                s.Carrier,
                s.ShipToName,
                s.SalesOrder.OrderNumber,
                CustomerName = s.SalesOrder.Customer.Name,
                Tracking = s.Packages.Where(p => p.TrackingNumber != null).OrderBy(p => p.PackageNumber).Select(p => p.TrackingNumber!).Take(5).ToList()
            })
            .ToListAsync(ct);

        var hits = rows.Select(s =>
        {
            var tracking = s.Tracking.FirstOrDefault(t => q.Terms.Any(term => SearchRanker.Match(t, term) != SearchMatch.None))
                ?? s.Tracking.FirstOrDefault();
            return new SearchHit(
                SearchCategory.Shipment, s.Id, s.ShipmentNumber,
                Join(s.CustomerName, s.OrderNumber, s.ShipDate.ToString("MMM d"), CarrierLabel(s.Carrier), tracking),
                s.Status == ShipmentStatus.InTransit ? "In transit" : s.Status.ToString(),
                s.Status switch
                {
                    ShipmentStatus.Pending => SearchTone.Warning,
                    ShipmentStatus.InTransit => SearchTone.Info,
                    ShipmentStatus.Delivered => SearchTone.Success,
                    ShipmentStatus.Exception => SearchTone.Danger,
                    _ => SearchTone.Muted
                },
                Page("/Shipments/Detail", new { id = s.Id }),
                SearchRanker.Score(q, s.ShipmentNumber, null, [s.OrderNumber, s.ShipToName, .. s.Tracking], [s.CustomerName]));
        });

        return Group(SearchCategory.Shipment, hits, rows.Count > limit, limit,
            Page("/Shipments/Index", new { Tab = "history", Search = q.Text }));
    }

    private async Task<SearchGroup> ProductsAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var size = q.Size;
        var (a, b) = size ?? (0m, 0m);
        var tol = SizeToleranceIn;
        var products = db.Products.AsNoTracking();
        products = size is null
            ? AllTerms(products, q, term => p =>
                p.InternalSku.ToUpper().Contains(term)
                || (p.CustomerSku != null && p.CustomerSku.ToUpper().Contains(term))
                || p.Description.ToUpper().Contains(term)
                || (p.PrimaryCustomer != null && p.PrimaryCustomer.Name.ToUpper().Contains(term))
                || p.CustomerAssignments.Any(c => c.Customer.Name.ToUpper().Contains(term)))
            : products.Where(p =>
                (p.LabelAcrossIn >= a - tol && p.LabelAcrossIn <= a + tol && p.LabelAroundIn >= b - tol && p.LabelAroundIn <= b + tol)
                || (p.LabelAcrossIn >= b - tol && p.LabelAcrossIn <= b + tol && p.LabelAroundIn >= a - tol && p.LabelAroundIn <= a + tol)
                || p.Description.ToUpper().Contains(whole));

        var rows = await products
            .OrderByDescending(p => p.InternalSku.ToUpper() == whole || (p.CustomerSku != null && p.CustomerSku.ToUpper() == whole))
            .ThenByDescending(p => p.IsActive)
            .ThenByDescending(p => p.CreatedAt)
            .Take(limit + 1)
            .Select(p => new
            {
                p.Id,
                p.InternalSku,
                p.CustomerSku,
                p.Description,
                p.IsActive,
                p.Status,
                p.LabelAcrossIn,
                p.LabelAroundIn,
                CustomerName = p.PrimaryCustomer != null ? p.PrimaryCustomer.Name : null,
                Assigned = p.CustomerAssignments.OrderBy(c => c.Customer.Name).Select(c => c.Customer.Name).Take(5).ToList()
            })
            .ToListAsync(ct);

        var hits = rows.Select(p =>
        {
            var textScore = SearchRanker.Score(q, p.InternalSku, p.Description, [p.CustomerSku], [p.CustomerName, .. p.Assigned]);
            var score = size is { } wanted ? Math.Max(SizeScore(p.LabelAcrossIn, p.LabelAroundIn, wanted), textScore) : textScore;
            var (badge, tone) = !p.IsActive
                ? ("Inactive", SearchTone.Muted)
                : p.Status == ProductStatus.Discontinued ? ("Discontinued", SearchTone.Muted) : ((string?)null, SearchTone.Neutral);
            return new SearchHit(
                SearchCategory.Product, p.Id, p.Description,
                Join(p.InternalSku,
                    string.IsNullOrWhiteSpace(p.CustomerSku) ? null : $"cust. SKU {p.CustomerSku}",
                    p.CustomerName ?? p.Assigned.FirstOrDefault(),
                    SizeLabel(p.LabelAcrossIn, p.LabelAroundIn)),
                badge, tone,
                Page("/Products/Edit", new { id = p.Id }),
                score - (p.IsActive ? 0 : 5));
        });

        return Group(SearchCategory.Product, hits, rows.Count > limit, limit, Page("/Products/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> PurchaseOrdersAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var rows = await AllTerms(db.PurchaseOrders.AsNoTracking(), q, term => p =>
                p.PoNumber.ToUpper().Contains(term)
                || p.Supplier.Name.ToUpper().Contains(term)
                || (p.Notes != null && p.Notes.ToUpper().Contains(term)))
            .OrderByDescending(NumberFirst<Domain.Entities.PurchaseOrder>(p => p.PoNumber, whole, suffix))
            .ThenByDescending(p => p.OrderedAt)
            .Take(limit + 1)
            .Select(p => new { p.Id, p.PoNumber, p.Status, p.OrderedAt, p.ExpectedAt, SupplierName = p.Supplier.Name })
            .ToListAsync(ct);

        var hits = rows.Select(p => new SearchHit(
            SearchCategory.PurchaseOrder, p.Id, p.PoNumber,
            Join(p.SupplierName, $"ordered {p.OrderedAt.ToLocalTime():MMM d}", p.ExpectedAt is { } e ? $"expected {e:MMM d}" : null),
            p.Status == PurchaseOrderStatus.PartiallyReceived ? "Partially received" : p.Status.ToString(),
            p.Status switch
            {
                PurchaseOrderStatus.Sent => SearchTone.Info,
                PurchaseOrderStatus.PartiallyReceived => SearchTone.Warning,
                PurchaseOrderStatus.Received => SearchTone.Success,
                PurchaseOrderStatus.Cancelled => SearchTone.Danger,
                _ => SearchTone.Neutral
            },
            Page("/PurchaseOrders/Edit", new { id = p.Id }),
            SearchRanker.Score(q, p.PoNumber, null, null, [p.SupplierName])));

        return Group(SearchCategory.PurchaseOrder, hits, rows.Count > limit, limit, Page("/PurchaseOrders/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> RollsAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var suffix = q.NumberSuffix;
        var rows = await AllTerms(db.Rolls.AsNoTracking(), q, term => r =>
                r.RollBarcode.ToUpper().Contains(term)
                || r.SupplierLotNumber.ToUpper().Contains(term)
                || (r.Location != null && r.Location.ToUpper().Contains(term))
                || r.Stock.Code.ToUpper().Contains(term)
                || r.Stock.Description.ToUpper().Contains(term))
            .OrderByDescending(NumberFirst<Domain.Entities.Roll>(r => r.RollBarcode, whole, suffix))
            .ThenBy(r => r.Status)
            .ThenByDescending(r => r.ReceivedAt)
            .Take(limit + 1)
            .Select(r => new
            {
                r.Id,
                r.RollBarcode,
                r.SupplierLotNumber,
                r.Location,
                r.Status,
                r.RemainingLengthLf,
                StockCode = r.Stock.Code,
                StockDescription = r.Stock.Description
            })
            .ToListAsync(ct);

        var hits = rows.Select(r => new SearchHit(
            SearchCategory.Roll, r.Id, r.RollBarcode,
            Join($"{r.StockCode} {r.StockDescription}", $"{r.RemainingLengthLf:N0} LF left",
                string.IsNullOrWhiteSpace(r.Location) ? null : $"at {r.Location}",
                string.IsNullOrWhiteSpace(r.SupplierLotNumber) ? null : $"lot {r.SupplierLotNumber}"),
            r.Status == RollStatus.OnPress ? "On press" : r.Status.ToString(),
            r.Status switch
            {
                RollStatus.Available => SearchTone.Success,
                RollStatus.Staged => SearchTone.Info,
                RollStatus.OnPress => SearchTone.Accent,
                _ => SearchTone.Muted
            },
            Page("/Rolls/Detail", new { id = r.Id }),
            SearchRanker.Score(q, r.RollBarcode, null, [r.SupplierLotNumber, r.Location], [r.StockCode, r.StockDescription])));

        return Group(SearchCategory.Roll, hits, rows.Count > limit, limit, Page("/Rolls/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> DiesAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var size = q.Size;
        var (a, b) = size ?? (0m, 0m);
        var tol = SizeToleranceIn;
        var dies = db.Dies.AsNoTracking();
        dies = size is null
            ? AllTerms(dies, q, term => d =>
                d.Description.ToUpper().Contains(term)
                || (d.Shape != null && d.Shape.ToUpper().Contains(term))
                || (d.SupplierPartNumber != null && d.SupplierPartNumber.ToUpper().Contains(term))
                || (d.Location != null && d.Location.ToUpper().Contains(term))
                || (d.Customer != null && d.Customer.Name.ToUpper().Contains(term)))
            : dies.Where(d =>
                (d.LabelAcrossIn >= a - tol && d.LabelAcrossIn <= a + tol && d.LabelAroundIn >= b - tol && d.LabelAroundIn <= b + tol)
                || (d.LabelAcrossIn >= b - tol && d.LabelAcrossIn <= b + tol && d.LabelAroundIn >= a - tol && d.LabelAroundIn <= a + tol)
                || d.Description.ToUpper().Contains(whole));

        var rows = await dies
            .OrderByDescending(d => d.IsActive && d.RetiredAt == null)
            .ThenByDescending(d => d.Description.ToUpper().StartsWith(first))
            .ThenByDescending(d => d.LastUsedAt)
            .Take(limit + 1)
            .Select(d => new
            {
                d.Id,
                d.Description,
                d.Shape,
                d.DieType,
                d.LabelAcrossIn,
                d.LabelAroundIn,
                d.LabelsAcross,
                d.LabelsAround,
                d.Location,
                d.SupplierPartNumber,
                d.IsActive,
                d.RetiredAt,
                CustomerName = d.Customer != null ? d.Customer.Name : null
            })
            .ToListAsync(ct);

        var hits = rows.Select(d =>
        {
            var usable = d.IsActive && d.RetiredAt is null;
            var textScore = SearchRanker.Score(q, d.SupplierPartNumber, d.Description, [d.Shape, d.Location], [d.CustomerName]);
            var score = size is { } wanted ? Math.Max(SizeScore(d.LabelAcrossIn, d.LabelAroundIn, wanted), textScore) : textScore;
            return new SearchHit(
                SearchCategory.Die, d.Id, d.Description,
                Join(SizeLabel(d.LabelAcrossIn, d.LabelAroundIn), $"{d.LabelsAcross} across × {d.LabelsAround} around",
                    string.IsNullOrWhiteSpace(d.Location) ? null : $"at {d.Location}", d.CustomerName),
                d.RetiredAt is not null ? "Retired" : !d.IsActive ? "Inactive" : d.DieType.ToString(),
                usable ? SearchTone.Neutral : SearchTone.Muted,
                Page("/Dies/Edit", new { id = d.Id }),
                score - (usable ? 0 : 5));
        });

        return Group(SearchCategory.Die, hits, rows.Count > limit, limit, Page("/Dies/Index", new { Search = size is null ? q.Text : null }));
    }

    private async Task<SearchGroup> StocksAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var rows = await AllTerms(db.Stocks.AsNoTracking(), q, term => s =>
                s.Code.ToUpper().Contains(term)
                || s.Description.ToUpper().Contains(term)
                || (s.SupplierPartNumber != null && s.SupplierPartNumber.ToUpper().Contains(term))
                || s.FaceMaterial.ToUpper().Contains(term)
                || s.Supplier.Name.ToUpper().Contains(term))
            .OrderByDescending(s => s.Code.ToUpper() == whole)
            .ThenByDescending(s => s.IsActive)
            .ThenBy(s => s.Code)
            .Take(limit + 1)
            .Select(s => new
            {
                s.Id,
                s.Code,
                s.Description,
                s.StockType,
                s.WidthIn,
                s.SupplierPartNumber,
                s.IsActive,
                SupplierName = s.Supplier.Name
            })
            .ToListAsync(ct);

        var hits = rows.Select(s => new SearchHit(
            SearchCategory.Stock, s.Id, $"{s.Code} — {s.Description}",
            Join($"{s.WidthIn:0.###}\" wide", s.SupplierName, s.SupplierPartNumber),
            !s.IsActive ? "Inactive" : s.StockType.ToString(),
            s.IsActive ? SearchTone.Neutral : SearchTone.Muted,
            Page("/Stocks/Edit", new { id = s.Id }),
            SearchRanker.Score(q, s.Code, s.Description, [s.SupplierPartNumber], [s.SupplierName]) - (s.IsActive ? 0 : 5)));

        return Group(SearchCategory.Stock, hits, rows.Count > limit, limit, Page("/Stocks/Index", new { Search = q.Text }));
    }

    private async Task<SearchGroup> SuppliersAsync(SearchQuery q, int limit, CancellationToken ct)
    {
        var (whole, first) = (q.Whole, q.Terms[0]);
        var rows = await AllTerms(db.Suppliers.AsNoTracking(), q, term => s =>
                s.Name.ToUpper().Contains(term)
                || s.Code.ToUpper().Contains(term)
                || (s.AccountNumber != null && s.AccountNumber.ToUpper().Contains(term)))
            .OrderByDescending(s => s.Code.ToUpper() == whole || s.Name.ToUpper() == whole)
            .ThenByDescending(s => s.IsActive)
            .ThenBy(s => s.Name)
            .Take(limit + 1)
            .Select(s => new { s.Id, s.Name, s.Code, s.Terms, s.IsActive, s.IsOutsourceVendor, s.AccountNumber })
            .ToListAsync(ct);

        var hits = rows.Select(s => new SearchHit(
            SearchCategory.Supplier, s.Id, s.Name,
            Join(s.Code, s.Terms, s.IsOutsourceVendor ? "outsource vendor" : null),
            !s.IsActive ? "Inactive" : null,
            s.IsActive ? SearchTone.Neutral : SearchTone.Muted,
            Page("/Suppliers/Edit", new { id = s.Id }),
            SearchRanker.Score(q, s.Code, s.Name, [s.AccountNumber]) - (s.IsActive ? 0 : 5)));

        return Group(SearchCategory.Supplier, hits, rows.Count > limit, limit, Page("/Suppliers/Index", new { Search = q.Text }));
    }

    // ---- Helpers ----------------------------------------------------------------------------

    /// <summary>Requires every term to match (each term is ORed across the category's fields).</summary>
    private static IQueryable<T> AllTerms<T>(
        IQueryable<T> query,
        SearchQuery q,
        Func<string, Expression<Func<T, bool>>> matches)
    {
        foreach (var term in q.Terms)
        {
            query = query.Where(matches(term));
        }

        return query;
    }

    /// <summary>Sort key that floats the typed document number (or its sequence, for a bare
    /// number) to the top before the row limit applies.</summary>
    private static Expression<Func<T, bool>> NumberFirst<T>(
        Expression<Func<T, string>> number,
        string whole,
        string? suffix)
    {
        var parameter = number.Parameters[0];
        var upper = Expression.Call(number.Body, typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!);
        Expression test = Expression.Equal(upper, Expression.Constant(whole));
        if (suffix is not null)
        {
            var endsWith = Expression.Call(number.Body, typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!,
                Expression.Constant(suffix));
            test = Expression.OrElse(test, endsWith);
        }

        return Expression.Lambda<Func<T, bool>>(test, parameter);
    }

    private SearchGroup Group(SearchCategory category, IEnumerable<SearchHit> hits, bool hasMore, int limit, string? seeAllUrl) =>
        new(category,
            CategoryLabel(category),
            hits.OrderByDescending(h => h.Score).Take(limit).ToList(),
            hasMore,
            seeAllUrl);

    private string Page(string page, object values) => links.GetPathByPage(page, values: values) ?? "/";

    /// <summary>How well a label size matches a "2x3" query: exact beats rotated beats near; 0 when
    /// it is outside <see cref="SizeToleranceIn"/> both ways (the hit then matched on its text).</summary>
    private static int SizeScore(decimal acrossIn, decimal aroundIn, (decimal AcrossIn, decimal AroundIn) wanted)
    {
        var match = Domain.Dies.DieSizeMatcher.Match(acrossIn, aroundIn, wanted.AcrossIn, wanted.AroundIn, SizeToleranceIn);
        return match switch
        {
            null => 0,
            { IsExact: true, Rotated: false } => 92,
            { IsExact: true } => 88,
            _ => (int)Math.Round(80 - match.DistanceIn * 40)
        };
    }

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : text;
    }

    private static string? LineSummary(string? firstLine, int lineCount) =>
        firstLine is null ? null : lineCount > 1 ? $"{firstLine} +{lineCount - 1} more" : firstLine;

    private static string SizeLabel(decimal acrossIn, decimal aroundIn) => $"{acrossIn:0.###}\" × {aroundIn:0.###}\"";

    private static string CarrierLabel(Carrier carrier) => carrier switch
    {
        Carrier.Fedex => "FedEx",
        Carrier.Ups => "UPS",
        Carrier.Usps => "USPS",
        Carrier.Dhl => "DHL",
        _ => "Other carrier"
    };

    private static string SalesOrderStatusLabel(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.InProduction => "In production",
        _ => status.ToString()
    };

    /// <summary>"Finishing · Laminate", "Shipping · Ready to ship", "At Acme Flexo".</summary>
    public static string JobBadge(JobFloorLocation location) => location.Stage switch
    {
        JobStatus.Shipped or JobStatus.Closed => location.StageLabel,
        JobStatus.Outsourced => location.Where,
        _ => $"{location.StageLabel} · {location.Where}"
    };

    private static SearchTone JobTone(FloorJob job) => job.Status switch
    {
        _ when job.Location.NeedsAttention => SearchTone.Warning,
        JobStatus.Rewound => SearchTone.Success,
        JobStatus.Shipped or JobStatus.Closed => SearchTone.Muted,
        JobStatus.Outsourced => SearchTone.Info,
        _ => SearchTone.Accent
    };
}
