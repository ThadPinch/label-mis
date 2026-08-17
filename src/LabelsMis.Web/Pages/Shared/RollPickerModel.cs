using LabelsMis.Web.Services.Rolls;

namespace LabelsMis.Web.Pages.Shared;

/// <summary>
/// The consume-material roll picker: a fuzzy search box that whittles a dropdown of every
/// consumable roll in inventory. The selected roll's barcode posts under <see cref="InputName"/>,
/// so the same handlers that took a scanned barcode keep working. Rolls of
/// <see cref="PreferredStockId"/> (the material the job calls for) are listed first.
/// </summary>
public record RollPickerModel(
    IReadOnlyList<RollPickerOption> Rolls,
    string InputName,
    Guid? PreferredStockId,
    string PreferredGroupLabel = "Material for this job",
    bool Small = true);
