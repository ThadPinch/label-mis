using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Invoicing;

/// <summary>The kind of QuickBooks line an invoice line becomes (one QB item each).</summary>
public enum QbLineType
{
    Sales = 0,
    Shipping = 1,
    Design = 2,
    Rush = 3,
    DieCreation = 4
}

/// <summary>
/// Setup → QuickBooks mapping: the QB item per line type, the QB class names, the keywords that
/// classify a charge line (charges carry no type, so the description decides — see
/// <see cref="QuickBooksMappingRules.ChargeType"/>), and the substrate code that marks a buyout.
/// Phase 0 stores QB <em>names</em>; Phase 1 adds the ItemRef / ClassRef ids next to them.
/// </summary>
public sealed record QuickBooksMapping(
    string SalesItem,
    string ShippingItem,
    string DesignItem,
    string RushItem,
    string DieCreationItem,
    string RollLabelClass,
    string ShrinkSleevesClass,
    string BuyoutClass,
    string DesignKeywords,
    string RushKeywords,
    string DieCreationKeywords,
    string OutsourcedSubstrateCode)
{
    public static QuickBooksMapping Default { get; } = new(
        "Sales",
        "Shipping",
        "Design",
        "Rush",
        "Die Creation",
        "Roll Label",
        "Shrink Sleeves",
        "Buyout",
        "design, art, artwork, proof",
        "rush, expedite",
        "die, tooling, plate",
        "OUTSOURCED");

    public string ItemFor(QbLineType type) => type switch
    {
        QbLineType.Shipping => ShippingItem,
        QbLineType.Design => DesignItem,
        QbLineType.Rush => RushItem,
        QbLineType.DieCreation => DieCreationItem,
        _ => SalesItem
    };

    /// <summary>Every QB item name the mapping knows, for the per-line override dropdown.</summary>
    public IReadOnlyList<string> Items =>
        new[] { SalesItem, ShippingItem, DesignItem, RushItem, DieCreationItem }.Distinct().ToList();

    /// <summary>Every QB class name the mapping knows, for the per-line override dropdown.</summary>
    public IReadOnlyList<string> Classes =>
        new[] { RollLabelClass, ShrinkSleevesClass, BuyoutClass }.Distinct().ToList();
}

/// <summary>What the mapping needs to know about one product (label) line.</summary>
/// <param name="SubstrateCode">The line's substrate (spec stock, else the product's).</param>
/// <param name="SubstrateType">Its stock type; <see cref="StockType.Shrink"/> means shrink sleeves.</param>
/// <param name="IsOutsourced">The line is bought out (has an outsourced item).</param>
public sealed record QbProductFacts(string? SubstrateCode, StockType? SubstrateType, bool IsOutsourced);

/// <summary>The QB item and class for one line, and why (shown on the preview).</summary>
public sealed record QbLineMapping(
    QbLineType LineType,
    string Item,
    string Class,
    string ItemSource,
    string ClassSource);

/// <summary>
/// Pure rules turning an invoice line into a QuickBooks (item, class):
/// - product lines → the Sales item; class by substrate: the outsourced substrate code (or an
///   outsourced line) → Buyout, shrink stock → Shrink Sleeves, else Roll Label;
/// - charge lines → Design / Rush / Die Creation when a keyword matches the description (first
///   match in that order: Die Creation, Rush, Design), else Sales; class = the job's class;
/// - the shipping line → the Shipping item, the job's class.
/// A per-line override (item or class) always wins.
/// </summary>
public static class QuickBooksMappingRules
{
    public static string ClassFor(QbProductFacts product, QuickBooksMapping mapping)
    {
        if (product.IsOutsourced
            || (!string.IsNullOrWhiteSpace(product.SubstrateCode)
                && string.Equals(product.SubstrateCode.Trim(), mapping.OutsourcedSubstrateCode.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return mapping.BuyoutClass;
        }

        return product.SubstrateType == StockType.Shrink ? mapping.ShrinkSleevesClass : mapping.RollLabelClass;
    }

    /// <summary>The order's (job's) class, inherited by its charge and shipping lines: the first
    /// product line's class; an order with no product lines gets Roll Label.</summary>
    public static string JobClass(IEnumerable<QbProductFacts> productLinesInOrder, QuickBooksMapping mapping) =>
        productLinesInOrder.Select(p => ClassFor(p, mapping)).FirstOrDefault() ?? mapping.RollLabelClass;

    /// <summary>Classifies a charge by its description. A keyword matches the start of any word
    /// ("die" matches "Die creation" and "diecut"; "art" matches "artwork").</summary>
    public static (QbLineType Type, string? Keyword) ChargeType(string? description, QuickBooksMapping mapping)
    {
        var words = Words(description);
        foreach (var (type, keywords) in new[]
                 {
                     (QbLineType.DieCreation, mapping.DieCreationKeywords),
                     (QbLineType.Rush, mapping.RushKeywords),
                     (QbLineType.Design, mapping.DesignKeywords)
                 })
        {
            var keyword = Keywords(keywords).FirstOrDefault(k => words.Any(w => w.StartsWith(k, StringComparison.Ordinal)));
            if (keyword is not null)
            {
                return (type, keyword);
            }
        }

        return (QbLineType.Sales, null);
    }

    public static QbLineMapping MapProductLine(
        QbProductFacts product,
        QuickBooksMapping mapping,
        string? itemOverride = null,
        string? classOverride = null) =>
        Apply(QbLineType.Sales, mapping.SalesItem, "Product line", ClassFor(product, mapping), "Substrate", itemOverride, classOverride);

    public static QbLineMapping MapChargeLine(
        string? description,
        string jobClass,
        QuickBooksMapping mapping,
        string? itemOverride = null,
        string? classOverride = null)
    {
        var (type, keyword) = ChargeType(description, mapping);
        return Apply(
            type,
            mapping.ItemFor(type),
            keyword is null ? "Charge — no keyword matched" : $"Charge — keyword \"{keyword}\"",
            jobClass,
            "Job's class",
            itemOverride,
            classOverride);
    }

    public static QbLineMapping MapShippingLine(string jobClass, QuickBooksMapping mapping) =>
        new(QbLineType.Shipping, mapping.ShippingItem, jobClass, "Shipping", "Job's class");

    /// <summary>Splits a comma-separated keyword setting into lower-case keywords.</summary>
    public static IReadOnlyList<string> Keywords(string? csv) =>
        (csv ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant())
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();

    private static QbLineMapping Apply(
        QbLineType type,
        string item,
        string itemSource,
        string cls,
        string classSource,
        string? itemOverride,
        string? classOverride) =>
        new(
            type,
            string.IsNullOrWhiteSpace(itemOverride) ? item : itemOverride.Trim(),
            string.IsNullOrWhiteSpace(classOverride) ? cls : classOverride.Trim(),
            string.IsNullOrWhiteSpace(itemOverride) ? itemSource : "Override",
            string.IsNullOrWhiteSpace(classOverride) ? classSource : "Override");

    private static string[] Words(string? text) =>
        (text ?? string.Empty).ToLowerInvariant()
            .Split([' ', ',', ';', '-', '/', '(', ')', '&', '.', ':', '+'], StringSplitOptions.RemoveEmptyEntries);
}
