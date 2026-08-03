namespace LabelsMis.Web.Services.Models;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public static class QueryExtensions
{
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        string? sort,
        Func<IQueryable<T>, string, IQueryable<T>> sorter)
    {
        return string.IsNullOrWhiteSpace(sort) ? query : sorter(query, sort);
    }

    /// <summary>Splits a sort value into its column key and direction. "customer_desc" → ("customer", true);
    /// null/blank → ("", false), letting callers fall through to their default ordering.</summary>
    public static (string Key, bool Desc) ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return ("", false);
        var value = sort.Trim();
        return value.EndsWith("_desc", StringComparison.OrdinalIgnoreCase)
            ? (value[..^5].ToLowerInvariant(), true)
            : (value.ToLowerInvariant(), false);
    }

    public static IOrderedQueryable<T> OrderByDir<T, TKey>(
        this IQueryable<T> query, bool desc, System.Linq.Expressions.Expression<Func<T, TKey>> keySelector)
        => desc ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);

    public static IOrderedQueryable<T> ThenByDir<T, TKey>(
        this IOrderedQueryable<T> query, bool desc, System.Linq.Expressions.Expression<Func<T, TKey>> keySelector)
        => desc ? query.ThenByDescending(keySelector) : query.ThenBy(keySelector);
}
