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
}
