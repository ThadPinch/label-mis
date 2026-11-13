using System.Text.RegularExpressions;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>Pairs LabelShop's payment terms with the company's QuickBooks terms by name.</summary>
public static partial class QuickBooksTermsMatcher
{
    /// <summary>The QuickBooks term named like LabelShop's term. Case, spaces and punctuation are
    /// ignored, and a few common spellings count ("Due upon receipt", "Net 30 days", "C.O.D.",
    /// "Prepaid"). Null when no term matches, or when two do.</summary>
    public static string? Match(PaymentTerms terms, IEnumerable<string> quickBooksTerms)
    {
        var candidates = quickBooksTerms
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => (Name: n.Trim(), Key: Key(n)))
            .ToList();
        foreach (var alias in Aliases(terms))
        {
            var hits = candidates.Where(c => c.Key == alias).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (hits.Count == 1)
            {
                return hits[0];
            }

            if (hits.Count > 1)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The map with every LabelShop term that has no QuickBooks term chosen filled in by
    /// <see cref="Match"/>. Chosen terms are kept as they are.</summary>
    public static IReadOnlyDictionary<PaymentTerms, string> FillBlanks(
        IReadOnlyDictionary<PaymentTerms, string> saved, IEnumerable<string> quickBooksTerms)
    {
        var names = quickBooksTerms.ToList();
        var result = new Dictionary<PaymentTerms, string>();
        foreach (var terms in Enum.GetValues<PaymentTerms>())
        {
            var chosen = saved.TryGetValue(terms, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : Match(terms, names);
            if (chosen is not null)
            {
                result[terms] = chosen;
            }
        }

        return result;
    }

    private static IEnumerable<string> Aliases(PaymentTerms terms) => terms switch
    {
        PaymentTerms.Prepay => ["prepay", "prepaid", "prepayment", "paymentinadvance"],
        PaymentTerms.DueOnReceipt => ["dueonreceipt", "dueuponreceipt", "payableonreceipt"],
        PaymentTerms.Cod => ["cod", "cashondelivery"],
        _ => [$"net{(int)terms}", $"net{(int)terms}days"]
    };

    private static string Key(string name) => NonAlphanumeric().Replace(name, string.Empty).ToLowerInvariant();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
