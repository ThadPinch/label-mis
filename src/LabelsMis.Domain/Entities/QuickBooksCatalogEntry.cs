using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

/// <summary>One product/service, class or payment term as the connected QuickBooks company has it.
/// A cache: it is replaced from QuickBooks by "Refresh the lists", fills the mapping dropdowns and
/// turns a mapped name into the id an invoice needs. It holds names and ids, nothing else.</summary>
public class QuickBooksCatalogEntry : EntityBase
{
    public const int MaxIdLength = 50;
    public const int MaxNameLength = 300;
    public const int MaxFullNameLength = 1000;

    private QuickBooksCatalogEntry()
    {
    }

    public QuickBooksCatalogKind Kind { get; private set; }
    public string QuickBooksId { get; private set; } = string.Empty;

    /// <summary>The entry's own name ("Pump").</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The name with its parents ("Design:Fountains:Pump"); the same as
    /// <see cref="Name"/> for a top-level entry. This is what the mapping stores and shows.</summary>
    public string FullName { get; private set; } = string.Empty;

    /// <summary>An item's own "taxable" setting; null for classes and terms.</summary>
    public bool? Taxable { get; private set; }
    public DateTime RefreshedAt { get; private set; }

    public static QuickBooksCatalogEntry Create(
        Guid id,
        QuickBooksCatalogKind kind,
        string quickBooksId,
        string name,
        string? fullName,
        bool? taxable,
        Guid createdById,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(quickBooksId))
        {
            throw new ArgumentException("A QuickBooks list entry needs its id.", nameof(quickBooksId));
        }

        var entry = new QuickBooksCatalogEntry { Kind = kind, QuickBooksId = Clip(quickBooksId.Trim(), MaxIdLength) };
        entry.Apply(name, fullName, taxable, now);
        entry.SetCreated(id, createdById, now);
        return entry;
    }

    /// <summary>Takes QuickBooks' current values for an entry already cached.</summary>
    public void Refresh(string name, string? fullName, bool? taxable, Guid modifiedById, DateTime now)
    {
        Apply(name, fullName, taxable, now);
        SetModified(modifiedById, now);
    }

    private void Apply(string name, string? fullName, bool? taxable, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A QuickBooks list entry needs its name.", nameof(name));
        }

        Name = Clip(name.Trim(), MaxNameLength);
        FullName = Clip(string.IsNullOrWhiteSpace(fullName) ? Name : fullName.Trim(), MaxFullNameLength);
        Taxable = Kind == QuickBooksCatalogKind.Item ? taxable : null;
        RefreshedAt = now;
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;
}
