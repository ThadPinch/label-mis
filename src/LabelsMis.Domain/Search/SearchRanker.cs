namespace LabelsMis.Domain.Search;

/// <summary>How one field of a record matched a search term.</summary>
public enum SearchMatch
{
    None = 0,
    Contains = 1,
    WordStart = 2,
    Prefix = 3,
    Exact = 4
}

/// <summary>
/// Scores a search hit so the record someone meant comes first. A record offers its fields in four
/// tiers — its <b>number</b> (JOB-2026-00052, SKU, customer code), its <b>name</b> (customer name,
/// product description), <b>details</b> (customer PO, tracking #, contact email) and <b>related</b>
/// text (the customer on an order) — and each tier is worth more for an exact match than a prefix,
/// a word start or a plain substring. Typing a customer's name therefore lists the customer above
/// that customer's orders, and typing "52" lists JOB-2026-00052 above JOB-2026-00152.
/// </summary>
public static class SearchRanker
{
    /// <summary>A bare number matching a document number's sequence ("52" → "…-00052").</summary>
    public const int NumberSuffixScore = 96;

    /// <summary>Scores at or above this are "the record you typed" — the search bar jumps straight
    /// to a lone hit like this on Enter.</summary>
    public const int ExactScore = 100;

    private static readonly int[] NumberPoints = [0, 60, 70, 85, 100];
    private static readonly int[] NamePoints = [0, 50, 68, 78, 90];
    private static readonly int[] DetailPoints = [0, 40, 50, 58, 72];
    private static readonly int[] RelatedPoints = [0, 25, 34, 38, 46];

    /// <summary>How <paramref name="value"/> matches an upper-cased <paramref name="term"/>.</summary>
    public static SearchMatch Match(string? value, string term)
    {
        if (string.IsNullOrWhiteSpace(value) || term.Length == 0)
        {
            return SearchMatch.None;
        }

        var text = value.Trim().ToUpperInvariant();
        if (text == term)
        {
            return SearchMatch.Exact;
        }

        if (text.StartsWith(term, StringComparison.Ordinal))
        {
            return SearchMatch.Prefix;
        }

        var index = text.IndexOf(term, StringComparison.Ordinal);
        if (index < 0)
        {
            return SearchMatch.None;
        }

        // A later occurrence may start a word even when the first doesn't ("BLUEBERRY BERRY").
        for (; index >= 0; index = text.IndexOf(term, index + 1, StringComparison.Ordinal))
        {
            if (!char.IsLetterOrDigit(text[index - 1]))
            {
                return SearchMatch.WordStart;
            }
        }

        return SearchMatch.Contains;
    }

    /// <summary>
    /// The record's score for the query, 0 when nothing matches. The whole query is scored as one
    /// string, and each term on its own; with several terms the weakest term sets the per-term
    /// score (every term has to pull its weight), and the better of the two wins.
    /// </summary>
    public static int Score(
        SearchQuery query,
        string? number,
        string? name = null,
        IEnumerable<string?>? details = null,
        IEnumerable<string?>? related = null)
    {
        if (query.IsEmpty)
        {
            return 0;
        }

        var detailList = details?.ToList() ?? [];
        var relatedList = related?.ToList() ?? [];

        var whole = ScoreTerm(query.Whole, number, name, detailList, relatedList);
        if (query.NumberSuffix is { } suffix
            && number is not null
            && number.Trim().EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            whole = Math.Max(whole, NumberSuffixScore);
        }

        if (query.Terms.Count == 1)
        {
            return whole;
        }

        var weakest = query.Terms.Min(term => ScoreTerm(term, number, name, detailList, relatedList));
        return Math.Max(whole, weakest);
    }

    private static int ScoreTerm(
        string term,
        string? number,
        string? name,
        IReadOnlyList<string?> details,
        IReadOnlyList<string?> related)
    {
        var best = Math.Max(
            NumberPoints[(int)Match(number, term)],
            NamePoints[(int)Match(name, term)]);

        foreach (var detail in details)
        {
            best = Math.Max(best, DetailPoints[(int)Match(detail, term)]);
        }

        foreach (var text in related)
        {
            best = Math.Max(best, RelatedPoints[(int)Match(text, term)]);
        }

        return best;
    }
}
