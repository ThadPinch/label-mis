using LabelsMis.Domain.Common;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.Entities;

/// <summary>Setup → QuickBooks mapping (single row). Absent until first saved; the defaults in
/// <see cref="QuickBooksMapping.Default"/> apply meanwhile.</summary>
public class QuickBooksSettings : EntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private QuickBooksSettings()
    {
    }

    public string SalesItem { get; private set; } = string.Empty;
    public string ShippingItem { get; private set; } = string.Empty;
    public string DesignItem { get; private set; } = string.Empty;
    public string RushItem { get; private set; } = string.Empty;
    public string DieCreationItem { get; private set; } = string.Empty;
    public string RollLabelClass { get; private set; } = string.Empty;
    public string ShrinkSleevesClass { get; private set; } = string.Empty;
    public string BuyoutClass { get; private set; } = string.Empty;
    public string DesignKeywords { get; private set; } = string.Empty;
    public string RushKeywords { get; private set; } = string.Empty;
    public string DieCreationKeywords { get; private set; } = string.Empty;
    public string OutsourcedSubstrateCode { get; private set; } = string.Empty;

    public static QuickBooksSettings CreateDefault(Guid createdById, DateTime createdAt)
    {
        var settings = new QuickBooksSettings();
        settings.Apply(QuickBooksMapping.Default);
        settings.SetCreated(SingletonId, createdById, createdAt);
        return settings;
    }

    public QuickBooksMapping ToMapping() => new(
        SalesItem,
        ShippingItem,
        DesignItem,
        RushItem,
        DieCreationItem,
        RollLabelClass,
        ShrinkSleevesClass,
        BuyoutClass,
        DesignKeywords,
        RushKeywords,
        DieCreationKeywords,
        OutsourcedSubstrateCode);

    /// <summary>Saves the mapping. Item and class names are required (QuickBooks needs one on
    /// every line); keyword lists may be empty (nothing then matches that charge type).</summary>
    public void Update(QuickBooksMapping mapping, Guid modifiedById, DateTime modifiedAt)
    {
        foreach (var (value, label) in new[]
                 {
                     (mapping.SalesItem, "Sales item"),
                     (mapping.ShippingItem, "Shipping item"),
                     (mapping.DesignItem, "Design item"),
                     (mapping.RushItem, "Rush item"),
                     (mapping.DieCreationItem, "Die creation item"),
                     (mapping.RollLabelClass, "Roll label class"),
                     (mapping.ShrinkSleevesClass, "Shrink sleeves class"),
                     (mapping.BuyoutClass, "Buyout class")
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{label} is required.");
            }
        }

        Apply(mapping);
        SetModified(modifiedById, modifiedAt);
    }

    private void Apply(QuickBooksMapping mapping)
    {
        SalesItem = mapping.SalesItem.Trim();
        ShippingItem = mapping.ShippingItem.Trim();
        DesignItem = mapping.DesignItem.Trim();
        RushItem = mapping.RushItem.Trim();
        DieCreationItem = mapping.DieCreationItem.Trim();
        RollLabelClass = mapping.RollLabelClass.Trim();
        ShrinkSleevesClass = mapping.ShrinkSleevesClass.Trim();
        BuyoutClass = mapping.BuyoutClass.Trim();
        DesignKeywords = string.Join(", ", QuickBooksMappingRules.Keywords(mapping.DesignKeywords));
        RushKeywords = string.Join(", ", QuickBooksMappingRules.Keywords(mapping.RushKeywords));
        DieCreationKeywords = string.Join(", ", QuickBooksMappingRules.Keywords(mapping.DieCreationKeywords));
        OutsourcedSubstrateCode = (mapping.OutsourcedSubstrateCode ?? string.Empty).Trim();
    }
}
