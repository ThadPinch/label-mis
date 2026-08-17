using Microsoft.AspNetCore.Mvc.Rendering;

namespace LabelsMis.Web.Pages.Shared;

/// <summary>
/// Model for <c>Shared/_OutsourceFields</c>: the "outsource this item" toggle plus vendor, quote #,
/// vendor cost, expected-in date and private notes, bound under <see cref="Prefix"/> (e.g.
/// <c>Input.Lines[2]</c>). Estimate lines quote cost per quantity break instead, so they pass
/// <see cref="ShowCost"/> = false and carry the per-quantity pricing in <see cref="PricingJson"/>.
/// </summary>
public record OutsourceFieldsModel(
    string Prefix,
    IEnumerable<SelectListItem> Vendors)
{
    public bool IsOutsourced { get; init; }
    public Guid? VendorId { get; init; }
    public string? QuoteNumber { get; init; }
    public decimal? VendorCost { get; init; }
    public DateOnly? ExpectedIn { get; init; }
    public string? PrivateNotes { get; init; }
    public bool ShowCost { get; init; } = true;
    public string? PricingJson { get; init; }
    public bool ReadOnly { get; init; }
    /// <summary>The switch itself can no longer be flipped (a job is routed / the vendor is involved),
    /// but the vendor details stay editable. The current value is posted as a hidden field.</summary>
    public bool ToggleLocked { get; init; }
    /// <summary>Short tracking status shown next to the switch on existing orders (e.g. "At vendor").</summary>
    public string? StatusLabel { get; init; }
    public string ToggleLabel { get; init; } = "Outsource this item";
    public string? Hint { get; init; }
    /// <summary>Extra CSS class on the vendor-cost input so page scripts can find it (e.g. for a margin readout).</summary>
    public string CostInputClass { get; init; } = "";
    /// <summary>When set, a read-only margin readout is rendered with this CSS class for the page script to fill.</summary>
    public string? MarginReadoutClass { get; init; }
}
