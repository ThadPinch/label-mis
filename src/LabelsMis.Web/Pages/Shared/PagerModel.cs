namespace LabelsMis.Web.Pages.Shared;

public record PagerModel(
    int Page,
    int TotalPages,
    int TotalCount,
    string? Search = null,
    string? Sort = null,
    bool IncludeInactive = false,
    string? StockType = null)
{
    /// <summary>Query-string key carrying the page number. Override when a page hosts more than one
    /// paginated list so they don't share (and clobber) a single page parameter.</summary>
    public string PageParam { get; init; } = "pageNumber";

    /// <summary>Extra query values to carry on every pager link (e.g. the active tab, or filters not
    /// covered by the fields above). Null/blank values are omitted.</summary>
    public IReadOnlyDictionary<string, string?>? Extra { get; init; }
}
