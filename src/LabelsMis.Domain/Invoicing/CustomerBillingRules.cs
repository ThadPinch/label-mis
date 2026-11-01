namespace LabelsMis.Domain.Invoicing;

/// <summary>A customer contact as far as QuickBooks billing cares.</summary>
public sealed record BillingContactFacts(string? Role, string? Email, bool IsPrimary);

/// <summary>
/// Customer-level reasons an invoice can't go to QuickBooks yet (E7), and who it is billed to.
/// Contact roles are free text, so "billing" is matched on words: billing / bill / invoice(s) /
/// accounts payable / A/P / AP / accounting.
/// </summary>
public static class CustomerBillingRules
{
    public const string NewClientFormBlocker = "Waiting on new-client form (tick Tax setup complete on the customer)";
    public const string NoBillingContactBlocker = "No billing contact with email (add a contact with role Billing on the customer)";

    private static readonly string[] BillingWords = ["billing", "bill", "bills", "invoice", "invoices", "invoicing", "accounting", "ap", "a/p"];

    public static bool IsBillingRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var text = role.Trim().ToLowerInvariant();
        if (text.Contains("accounts payable"))
        {
            return true;
        }

        var words = text.Split([' ', ',', ';', '-', '(', ')', '&', '.'], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(w => BillingWords.Contains(w));
    }

    /// <summary>The email QuickBooks should send the invoice to: a billing contact's, primary first.</summary>
    public static string? BillingEmail(IEnumerable<BillingContactFacts> contacts) =>
        contacts
            .Where(c => IsBillingRole(c.Role) && !string.IsNullOrWhiteSpace(c.Email))
            .OrderByDescending(c => c.IsPrimary)
            .Select(c => c.Email!.Trim())
            .FirstOrDefault();

    public static IReadOnlyList<string> Blockers(bool taxSetupComplete, IEnumerable<BillingContactFacts> contacts)
    {
        var blockers = new List<string>();
        if (!taxSetupComplete)
        {
            blockers.Add(NewClientFormBlocker);
        }

        if (BillingEmail(contacts) is null)
        {
            blockers.Add(NoBillingContactBlocker);
        }

        return blockers;
    }
}
