using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelsMis.Domain.Search;

/// <summary>The kinds of record the global search bar finds, in their default display order.</summary>
public enum SearchCategory
{
    Customer,
    SalesOrder,
    Job,
    Estimate,
    Invoice,
    Shipment,
    Product,
    PurchaseOrder,
    Roll,
    Die,
    Stock,
    Supplier
}

/// <summary>
/// What someone typed into the search bar, normalised: upper-cased terms that must all match
/// ("acme cherry" finds Acme's cherry label), plus the shapes worth special handling — a document
/// number ("JOB-2026-00052", "job 52", "so#52", "inv37"), a bare number ("52" ranks
/// JOB-2026-00052 above JOB-2026-00152), and a label size ("2x3", "1.5 × 1").
/// </summary>
public sealed partial record SearchQuery
{
    public const int MaxLength = 100;
    public const int MaxTerms = 6;

    /// <summary>Document-number prefixes and the category each one names.</summary>
    public static IReadOnlyDictionary<string, SearchCategory> DocumentPrefixes { get; } =
        new Dictionary<string, SearchCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["JOB"] = SearchCategory.Job,
            ["SO"] = SearchCategory.SalesOrder,
            ["INV"] = SearchCategory.Invoice,
            ["EST"] = SearchCategory.Estimate,
            ["PO"] = SearchCategory.PurchaseOrder,
            ["SHP"] = SearchCategory.Shipment,
            ["R"] = SearchCategory.Roll,
        };

    private SearchQuery(string text, IReadOnlyList<string> terms)
    {
        Text = text;
        Terms = terms;
    }

    /// <summary>The query as typed: trimmed, whitespace collapsed, capped at <see cref="MaxLength"/>.</summary>
    public string Text { get; }

    /// <summary>Upper-cased terms; a record must match every one (in any of its fields).</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>The category a document prefix names ("JOB", "SO", …). It ranks that category
    /// first; it does not hide the others ("PO 4471" may be a customer PO on a sales order).</summary>
    public SearchCategory? Hint { get; private init; }

    /// <summary>The digits of a document number or of an all-digit query ("52", "00052").</summary>
    public string? Digits { get; private init; }

    /// <summary>A label size typed as "2x3" / "1.5 × 1" / "2 by 3", in inches (across, around).</summary>
    public (decimal AcrossIn, decimal AroundIn)? Size { get; private init; }

    public bool IsEmpty => Terms.Count == 0;

    /// <summary>The query as one upper-cased string (for exact / prefix comparisons).</summary>
    public string Whole => Terms.Count == 1 ? Terms[0] : string.Join(' ', Terms);

    /// <summary>
    /// The tail every document number with these digits ends in — document numbers are
    /// <c>PREFIX-YEAR-NNNNN</c> — so "52" becomes "-00052". Null when the query isn't a number
    /// or has more digits than the sequence part holds.
    /// </summary>
    public string? NumberSuffix =>
        Digits is { Length: > 0 and <= 5 } digits ? "-" + digits.PadLeft(5, '0') : null;

    public static SearchQuery Empty { get; } = new(string.Empty, []);

    public static SearchQuery Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        var text = WhitespaceRegex().Replace(raw.Trim(), " ");
        if (text.Length > MaxLength)
        {
            text = text[..MaxLength].TrimEnd();
        }

        var document = DocumentNumberRegex().Match(text);
        if (document.Success && DocumentPrefixes.TryGetValue(document.Groups["prefix"].Value, out var category))
        {
            var prefix = document.Groups["prefix"].Value.ToUpperInvariant();
            var digits = document.Groups["num"].Value;
            var term = document.Groups["year"].Success
                ? $"{prefix}-{document.Groups["year"].Value}-{digits.PadLeft(5, '0')}"
                : digits;
            return new SearchQuery(text, [term]) { Hint = category, Digits = digits };
        }

        var size = SizeRegex().Match(text);
        if (size.Success
            && decimal.TryParse(size.Groups["a"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var across)
            && decimal.TryParse(size.Groups["b"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var around)
            && across > 0 && around > 0)
        {
            return new SearchQuery(text, [text.ToUpperInvariant()]) { Size = (across, around) };
        }

        var terms = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(',', ';').ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(MaxTerms)
            .ToList();

        return new SearchQuery(text, terms)
        {
            Digits = terms.Count == 1 && terms[0].All(char.IsAsciiDigit) ? terms[0] : null
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // "JOB-2026-00052", "job 52", "SO#52", "inv37", "po 4471", "R-2026-00012". The year part needs its
    // own separator so "JOB-2026" reads as number 2026, not as a year with nothing after it.
    [GeneratedRegex(@"^(?<prefix>JOB|SO|INV|EST|PO|SHP|R)[\s#:\-]*(?:(?<year>\d{4})[\s\-]+)?(?<num>\d{1,6})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentNumberRegex();

    // "2x3", "1.5 x 1", "2 × 3", "2 by 3", with an optional inch mark on either number.
    [GeneratedRegex(@"^(?<a>\d+(?:\.\d+)?|\.\d+)\s*""?\s*(?:x|×|by)\s*(?<b>\d+(?:\.\d+)?|\.\d+)\s*""?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();
}
