using LabelsMis.Web.Services.Carriers;

namespace LabelsMis.Web.Pages.Shared;

/// <summary>Rows for <c>_CarrierLogTable</c>: the whole log on Setup → Shipping carrier, or one
/// shipment's own calls on its page (<see cref="ShowShipment"/> false).</summary>
public sealed record CarrierLogTableModel(
    IReadOnlyList<CarrierLogRow> Rows,
    Func<DateTime, string> Local,
    bool ShowShipment = true,
    string? TableId = null);
