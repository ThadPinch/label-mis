using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
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

    /// <summary>LabelShop term → QuickBooks term name, as JSON (<c>{"Net30":"Net 30"}</c>).</summary>
    public string? TermsMapJson { get; private set; }

    /// <summary>The QuickBooks custom field that carries the customer's PO number, and its label.</summary>
    public string? PoCustomFieldId { get; private set; }
    public string? PoCustomFieldName { get; private set; }
    public QuickBooksSendMode SendMode { get; private set; }

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

    public QuickBooksPushSettings ToPushSettings() =>
        new(QuickBooksPushSettings.ParseTerms(TermsMapJson), PoCustomFieldId, PoCustomFieldName, SendMode);

    /// <summary>Saves the terms map, the PO field and the send setting.</summary>
    public void UpdatePush(QuickBooksPushSettings push, Guid modifiedById, DateTime modifiedAt)
    {
        var fieldId = string.IsNullOrWhiteSpace(push.PoFieldId) ? null : push.PoFieldId.Trim();
        if (fieldId is { Length: > 50 })
        {
            throw new ArgumentException("The PO field id is at most 50 characters.");
        }

        if (!Enum.IsDefined(push.SendMode))
        {
            throw new ArgumentException("Choose how invoices are sent.");
        }

        var json = QuickBooksPushSettings.SerializeTerms(push.Terms);
        if (json.Length > 2000)
        {
            throw new ArgumentException("The payment terms map is too long.");
        }

        TermsMapJson = json;
        PoCustomFieldId = fieldId;
        var fieldName = fieldId is null || string.IsNullOrWhiteSpace(push.PoFieldName) ? null : push.PoFieldName.Trim();
        PoCustomFieldName = fieldName is { Length: > 100 } ? fieldName[..100] : fieldName;
        SendMode = push.SendMode;
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
