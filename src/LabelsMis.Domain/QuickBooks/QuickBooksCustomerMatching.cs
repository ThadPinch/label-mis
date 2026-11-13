using System.Globalization;
using System.Text;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>Customer names compared the way people read them: case, spaces, punctuation and
/// accents don't count, and "&amp;" is "and". "Smith &amp; Sons, Inc." and "SMITH AND SONS INC"
/// are the same name.</summary>
public static class QuickBooksCustomerNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch == '&')
            {
                builder.Append("and");
            }
            else if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    public static bool Same(string? a, string? b)
    {
        var left = Normalize(a);
        return left.Length > 0 && left == Normalize(b);
    }
}

/// <summary>A LabelShop customer with no QuickBooks link yet.</summary>
public sealed record UnlinkedCustomer(Guid Id, string Name);

public enum QuickBooksMatchKind
{
    /// <summary>No QuickBooks customer has the name.</summary>
    None,

    /// <summary>Exactly one QuickBooks customer has the name and no other unlinked LabelShop
    /// customer claims it: safe to link in one click.</summary>
    Exact,

    /// <summary>The name fits more than one QuickBooks customer, or one QuickBooks customer fits
    /// more than one LabelShop customer. A person picks.</summary>
    Ambiguous
}

/// <summary>Which QuickBooks name the LabelShop name matched.</summary>
public enum QuickBooksMatchField
{
    DisplayName,
    CompanyName
}

/// <summary>What the Match customers page proposes for one LabelShop customer.
/// <see cref="Proposed"/> is set only for an <see cref="QuickBooksMatchKind.Exact"/> match.</summary>
public sealed record QuickBooksCustomerMatch(
    Guid CustomerId,
    string Name,
    QuickBooksMatchKind Kind,
    QuickBooksCustomerSummary? Proposed,
    IReadOnlyList<QuickBooksCustomerSummary> Candidates,
    QuickBooksMatchField? MatchedOn);

/// <summary>Proposes QuickBooks customers for unlinked LabelShop customers by normalised name
/// (<see cref="QuickBooksCustomerNames"/>). The display name is tried first, then the company name
/// of a top-level customer. Inactive QuickBooks customers and ones already linked to a LabelShop
/// customer are never proposed.</summary>
public static class QuickBooksCustomerMatcher
{
    public static IReadOnlyList<QuickBooksCustomerMatch> Match(
        IEnumerable<UnlinkedCustomer> unlinked,
        IEnumerable<QuickBooksCustomerSummary> quickBooks,
        IEnumerable<string> linkedQuickBooksIds)
    {
        var linked = new HashSet<string>(linkedQuickBooksIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.Ordinal);
        var available = quickBooks
            .Where(q => q.Active && !linked.Contains(q.Id))
            .DistinctBy(q => q.Id)
            .ToList();

        var byDisplayName = available
            .Select(q => (Key: QuickBooksCustomerNames.Normalize(q.DisplayName), Customer: q))
            .Where(x => x.Key.Length > 0)
            .ToLookup(x => x.Key, x => x.Customer);
        var byCompanyName = available
            .Where(q => !q.IsSubCustomer)
            .Select(q => (Key: QuickBooksCustomerNames.Normalize(q.CompanyName), Customer: q))
            .Where(x => x.Key.Length > 0)
            .ToLookup(x => x.Key, x => x.Customer);

        var found = unlinked.Select(c => Find(c, byDisplayName, byCompanyName)).ToList();

        // A QuickBooks customer that is the only candidate for two LabelShop customers is proposed
        // to neither: linking both in one click would merge them in QuickBooks.
        var claims = found
            .Where(f => f.Candidates.Count == 1)
            .GroupBy(f => f.Candidates[0].Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return found
            .Select(f =>
            {
                var kind = f.Candidates.Count switch
                {
                    0 => QuickBooksMatchKind.None,
                    1 when claims[f.Candidates[0].Id] == 1 => QuickBooksMatchKind.Exact,
                    _ => QuickBooksMatchKind.Ambiguous
                };
                return new QuickBooksCustomerMatch(
                    f.Customer.Id,
                    f.Customer.Name,
                    kind,
                    kind == QuickBooksMatchKind.Exact ? f.Candidates[0] : null,
                    f.Candidates,
                    f.Field);
            })
            .ToList();
    }

    private static (UnlinkedCustomer Customer, IReadOnlyList<QuickBooksCustomerSummary> Candidates, QuickBooksMatchField? Field) Find(
        UnlinkedCustomer customer,
        ILookup<string, QuickBooksCustomerSummary> byDisplayName,
        ILookup<string, QuickBooksCustomerSummary> byCompanyName)
    {
        var key = QuickBooksCustomerNames.Normalize(customer.Name);
        if (key.Length == 0)
        {
            return (customer, [], null);
        }

        var onDisplay = byDisplayName[key].OrderBy(q => q.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        if (onDisplay.Count > 0)
        {
            return (customer, onDisplay, QuickBooksMatchField.DisplayName);
        }

        var onCompany = byCompanyName[key].OrderBy(q => q.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        return onCompany.Count > 0
            ? (customer, onCompany, QuickBooksMatchField.CompanyName)
            : (customer, [], null);
    }
}
