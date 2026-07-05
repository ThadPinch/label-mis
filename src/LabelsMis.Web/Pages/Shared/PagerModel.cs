namespace LabelsMis.Web.Pages.Shared;

public record PagerModel(
    int Page,
    int TotalPages,
    int TotalCount,
    string? Search = null,
    string? Sort = null,
    bool IncludeInactive = false,
    string? StockType = null);
