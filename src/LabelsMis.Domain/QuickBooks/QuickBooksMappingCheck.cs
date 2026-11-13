using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>How a name LabelShop holds compares with the cached QuickBooks list.</summary>
public enum QuickBooksNameStatus
{
    /// <summary>The list has never been read from QuickBooks, so nothing can be said.</summary>
    NotChecked,

    /// <summary>QuickBooks has exactly this name.</summary>
    Found,

    /// <summary>QuickBooks has it apart from case or its parents ("pump" for "Design:Pump"); the
    /// QuickBooks spelling is what gets saved.</summary>
    Matched,

    /// <summary>QuickBooks has no entry by this name: an invoice that needs it can't be sent.</summary>
    Missing
}

/// <summary>One saved name against the list. <see cref="Selected"/> is what a dropdown should
/// show: QuickBooks' full name when it matched, else the saved name as it is.</summary>
public sealed record QuickBooksNameCheck(string Saved, string Selected, QuickBooksNameStatus Status)
{
    public bool IsMissing => Status == QuickBooksNameStatus.Missing;

    public static QuickBooksNameCheck Check(QuickBooksCatalogLookup lookup, QuickBooksCatalogKind kind, string? saved)
    {
        var name = saved?.Trim() ?? string.Empty;
        if (!lookup.HasAny(kind))
        {
            return new(name, name, QuickBooksNameStatus.NotChecked);
        }

        var entry = lookup.Find(kind, name);
        if (entry is null)
        {
            return new(name, name, QuickBooksNameStatus.Missing);
        }

        return string.Equals(entry.FullName, name, StringComparison.Ordinal)
            ? new(name, name, QuickBooksNameStatus.Found)
            : new(name, entry.FullName, QuickBooksNameStatus.Matched);
    }
}

/// <summary>One of the eight names the mapping holds: five items and three classes.</summary>
public sealed record QuickBooksMappingSlot(string Key, string Label, QuickBooksCatalogKind Kind, string Name);

public static class QuickBooksMappingSlots
{
    /// <summary>The mapping's item and class names, keyed by the <see cref="QuickBooksMapping"/>
    /// property that holds each.</summary>
    public static IReadOnlyList<QuickBooksMappingSlot> All(QuickBooksMapping mapping) =>
    [
        new(nameof(QuickBooksMapping.SalesItem), "Sales item", QuickBooksCatalogKind.Item, mapping.SalesItem),
        new(nameof(QuickBooksMapping.ShippingItem), "Shipping item", QuickBooksCatalogKind.Item, mapping.ShippingItem),
        new(nameof(QuickBooksMapping.DesignItem), "Design item", QuickBooksCatalogKind.Item, mapping.DesignItem),
        new(nameof(QuickBooksMapping.RushItem), "Rush item", QuickBooksCatalogKind.Item, mapping.RushItem),
        new(nameof(QuickBooksMapping.DieCreationItem), "Die creation item", QuickBooksCatalogKind.Item, mapping.DieCreationItem),
        new(nameof(QuickBooksMapping.RollLabelClass), "Roll label class", QuickBooksCatalogKind.Class, mapping.RollLabelClass),
        new(nameof(QuickBooksMapping.ShrinkSleevesClass), "Shrink sleeves class", QuickBooksCatalogKind.Class, mapping.ShrinkSleevesClass),
        new(nameof(QuickBooksMapping.BuyoutClass), "Buyout class", QuickBooksCatalogKind.Class, mapping.BuyoutClass)
    ];

    /// <summary>Each slot checked against the list, keyed like <see cref="All"/>.</summary>
    public static IReadOnlyDictionary<string, QuickBooksNameCheck> Check(QuickBooksMapping mapping, QuickBooksCatalogLookup lookup) =>
        All(mapping).ToDictionary(s => s.Key, s => QuickBooksNameCheck.Check(lookup, s.Kind, s.Name));

    /// <summary>The mapping with every name that matched apart from case or parents replaced by
    /// QuickBooks' full name. Names QuickBooks doesn't have are kept as they are.</summary>
    public static QuickBooksMapping Normalise(QuickBooksMapping mapping, QuickBooksCatalogLookup lookup)
    {
        string Fix(QuickBooksCatalogKind kind, string name) => QuickBooksNameCheck.Check(lookup, kind, name).Selected;

        return mapping with
        {
            SalesItem = Fix(QuickBooksCatalogKind.Item, mapping.SalesItem),
            ShippingItem = Fix(QuickBooksCatalogKind.Item, mapping.ShippingItem),
            DesignItem = Fix(QuickBooksCatalogKind.Item, mapping.DesignItem),
            RushItem = Fix(QuickBooksCatalogKind.Item, mapping.RushItem),
            DieCreationItem = Fix(QuickBooksCatalogKind.Item, mapping.DieCreationItem),
            RollLabelClass = Fix(QuickBooksCatalogKind.Class, mapping.RollLabelClass),
            ShrinkSleevesClass = Fix(QuickBooksCatalogKind.Class, mapping.ShrinkSleevesClass),
            BuyoutClass = Fix(QuickBooksCatalogKind.Class, mapping.BuyoutClass)
        };
    }
}
