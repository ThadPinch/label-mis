using LabelsMis.Domain.Search;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Search;

/// <summary>
/// The global search bar's two endpoints: <c>?handler=Suggest</c> answers the dropdown as JSON
/// (a handful of hits per category), and the page itself lists every category's hits for
/// "See all results" / Enter. A query that names exactly one record (a full document number,
/// or "job 52" when only one job ends in 00052) opens that record instead.
/// Each category only appears for users whose role can open its pages.
/// </summary>
[Authorize]
public class IndexModel(GlobalSearchService searchService, IAuthorizationService authorization) : PageModel
{
    /// <summary>The page policy guarding each category's records — the same policies the
    /// record pages carry, so search never lists something its user can't open.</summary>
    private static readonly IReadOnlyDictionary<SearchCategory, string> CategoryPolicies =
        new Dictionary<SearchCategory, string>
        {
            [SearchCategory.Customer] = MasterDataPolicies.Read,
            [SearchCategory.SalesOrder] = TransactionPolicies.SalesOrdersRead,
            [SearchCategory.Job] = TransactionPolicies.JobsRead,
            [SearchCategory.Estimate] = TransactionPolicies.EstimatesRead,
            [SearchCategory.Invoice] = TransactionPolicies.InvoicesRead,
            [SearchCategory.Shipment] = TransactionPolicies.ShippingRead,
            [SearchCategory.Product] = TransactionPolicies.ProductsRead,
            [SearchCategory.PurchaseOrder] = TransactionPolicies.InventoryRead,
            [SearchCategory.Roll] = TransactionPolicies.InventoryRead,
            [SearchCategory.Die] = MasterDataPolicies.Read,
            [SearchCategory.Stock] = MasterDataPolicies.Read,
            [SearchCategory.Supplier] = MasterDataPolicies.Read,
        };

    public const int SuggestPerCategory = 5;
    public const int PagePerCategory = 25;
    public const int SingleCategoryLimit = 100;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }

    /// <summary>Category key ("job", "sales-order", …) to list on its own, with more rows.</summary>
    [BindProperty(SupportsGet = true, Name = "type")] public string? Type { get; set; }

    /// <summary>Set by the results page's own links so a lone exact hit doesn't bounce away.</summary>
    [BindProperty(SupportsGet = true, Name = "all")] public bool ShowAll { get; set; }

    public SearchResults Results { get; private set; } = SearchResults.Empty;

    public SearchCategory? OnlyCategory { get; private set; }

    /// <summary>Hit counts per category for the tabs (from the all-categories search).</summary>
    public IReadOnlyList<SearchGroup> AllGroups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var allowed = await AllowedCategoriesAsync();
        OnlyCategory = GlobalSearchService.ParseCategoryKey(Type) is { } only && allowed.Contains(only) ? only : null;

        var all = await searchService.SearchAsync(Query, allowed, PagePerCategory, cancellationToken);
        AllGroups = all.Groups;

        if (OnlyCategory is null && !ShowAll && all.JumpTarget is { } target)
        {
            return Redirect(target.Url);
        }

        Results = OnlyCategory is { } category
            ? await searchService.SearchAsync(Query, new HashSet<SearchCategory> { category }, SingleCategoryLimit, cancellationToken)
            : all;
        return Page();
    }

    /// <summary>The dropdown's data: up to five hits per category, best category first.</summary>
    public async Task<IActionResult> OnGetSuggestAsync(CancellationToken cancellationToken)
    {
        var allowed = await AllowedCategoriesAsync();
        var results = await searchService.SearchAsync(Query, allowed, SuggestPerCategory, cancellationToken);
        var text = results.Query.Text;

        return new JsonResult(new
        {
            q = text,
            searchUrl = Url.Page("/Search/Index", new { q = text, all = true }),
            jumpUrl = results.JumpTarget?.Url,
            groups = results.Groups.Select(g => new
            {
                key = GlobalSearchService.CategoryKey(g.Category),
                label = g.Label,
                hasMore = g.HasMore,
                seeAllUrl = g.HasMore
                    ? Url.Page("/Search/Index", new { q = text, type = GlobalSearchService.CategoryKey(g.Category), all = true })
                    : null,
                items = g.Hits.Select(h => new
                {
                    title = h.Title,
                    subtitle = h.Subtitle,
                    badge = h.Badge,
                    tone = h.Tone.ToString().ToLowerInvariant(),
                    url = h.Url,
                    score = h.Score
                })
            })
        });
    }

    private async Task<HashSet<SearchCategory>> AllowedCategoriesAsync()
    {
        var allowed = new HashSet<SearchCategory>();
        var decided = new Dictionary<string, bool>();
        foreach (var (category, policy) in CategoryPolicies)
        {
            if (!decided.TryGetValue(policy, out var ok))
            {
                ok = (await authorization.AuthorizeAsync(User, policy)).Succeeded;
                decided[policy] = ok;
            }

            if (ok)
            {
                allowed.Add(category);
            }
        }

        return allowed;
    }
}
