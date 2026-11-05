namespace LabelsMis.Domain.Invoicing;

/// <summary>A customer contact as far as QuickBooks billing cares.</summary>
public sealed record BillingContactFacts(bool IsBillingContact, string? Email, bool IsPrimary);

/// <summary>Where the billing email came from: a contact ticked Billing, or the primary contact
/// standing in because no billing contact has an email.</summary>
public enum BillingEmailSource
{
    BillingContact,
    PrimaryContact
}

public sealed record BillingEmailChoice(string Email, BillingEmailSource Source)
{
    public bool IsPrimaryFallback => Source == BillingEmailSource.PrimaryContact;
}

/// <summary>
/// Customer-level reasons an invoice can't go to QuickBooks yet (E7), and who it is billed to: the
/// contact ticked Billing (primary first) with an email, else the primary contact's email. Contact
/// roles are free-text labels and play no part.
/// </summary>
public static class CustomerBillingRules
{
    public const string NewClientFormBlocker = "Waiting on new-client form (tick Tax setup complete on the customer)";
    public const string NoBillingContactBlocker = "No billing email (tick Billing on a customer contact, or give the primary contact an email)";

    /// <summary>The email QuickBooks should send the invoice to, and whether it is the primary
    /// contact standing in for a billing contact. Null when neither has an email.</summary>
    public static BillingEmailChoice? ChooseBillingEmail(IEnumerable<BillingContactFacts> contacts)
    {
        var withEmail = contacts.Where(c => !string.IsNullOrWhiteSpace(c.Email)).ToList();
        var billing = withEmail
            .Where(c => c.IsBillingContact)
            .OrderByDescending(c => c.IsPrimary)
            .FirstOrDefault();
        if (billing is not null)
        {
            return new BillingEmailChoice(billing.Email!.Trim(), BillingEmailSource.BillingContact);
        }

        var primary = withEmail.FirstOrDefault(c => c.IsPrimary);
        return primary is null ? null : new BillingEmailChoice(primary.Email!.Trim(), BillingEmailSource.PrimaryContact);
    }

    public static string? BillingEmail(IEnumerable<BillingContactFacts> contacts) => ChooseBillingEmail(contacts)?.Email;

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
