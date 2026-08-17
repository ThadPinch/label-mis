using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Web.Services.Outsourcing;

/// <summary>Vendor cost + our final (total) price for one quantity break of an outsourced estimate line.</summary>
public record OutsourceQuantityPrice(decimal VendorCost, decimal FinalPrice);

/// <summary>Outsourcing entered on an estimate line: the vendor details plus, per quoted quantity,
/// the vendor cost and the final price we quote (the calculator's own price is kept for comparison).</summary>
public record OutsourceLineQuoteInput(
    OutsourceDetails Details,
    IReadOnlyDictionary<int, OutsourceQuantityPrice> Pricing);

/// <summary>Outsourcing entered on a single-price item: an estimate charge, a sales-order line, or a
/// sales-order charge. Cost is the vendor's total for the item; our price stays on the item itself.</summary>
public record OutsourceItemInput(
    OutsourceDetails Details,
    decimal? VendorCost);
