using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Carriers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

/// <summary>Setup → Shipping carrier: the Worldwide Express API keys the shop's UPS labels are
/// bought with, who the carrier asks for at the shop, the defaults Record shipment starts from,
/// and a log of every call made to the carrier. Admin only: the keys spend the shop's money.</summary>
[Authorize(Roles = AppRoles.Admin)]
public class CarrierModel(
    CarrierSettingsService settings,
    CarrierConnectionTest connectionTest,
    CarrierLogger log,
    ClientClock clock) : PageModel
{
    private const string StatusKey = "CarrierStatus";
    private const string ErrorKey = "CarrierError";
    public const int BlankPresetRows = 3;
    public const int RecentLogRows = 50;
    public const int AllLogRows = 500;

    public CarrierSettingsView Carrier { get; private set; } =
        CarrierSettingsView.Empty(null, new CarrierParty(null, null, null, null, null, null, null, null));

    public IReadOnlyList<CarrierLogRow> Log { get; private set; } = [];

    /// <summary>Show the whole log (the newest 500) instead of the newest 50.</summary>
    [BindProperty(SupportsGet = true, Name = "log")] public string? LogRange { get; set; }

    public bool ShowAllLog => string.Equals(LogRange, "all", StringComparison.OrdinalIgnoreCase);

    /// <summary>The saved boxes, then a few empty rows to add more.</summary>
    public IReadOnlyList<BoxPresetInput> PresetRows =>
        Carrier.BoxPresets
            .Select(p => new BoxPresetInput { Name = p.Name, LengthIn = p.LengthIn, WidthIn = p.WidthIn, HeightIn = p.HeightIn })
            .Concat(Enumerable.Range(0, BlankPresetRows).Select(_ => new BoxPresetInput()))
            .ToList();

    /// <summary>What still stops a label from being bought, in the words Record shipment would use.</summary>
    public IReadOnlyList<string> ShipFromProblems { get; private set; } = [];

    public string Local(DateTime? utc, string format = "MMM d, yyyy h:mm tt") =>
        utc is { } value ? clock.ToLocal(value).ToString(format) : string.Empty;

    public string LocalWithSeconds(DateTime utc) => clock.ToLocal(utc).ToString("MMM d, yyyy h:mm:ss tt");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Carrier = await settings.GetViewAsync(cancellationToken);
        Log = await log.GetRecentAsync(ShowAllLog ? AllLogRows : RecentLogRows, null, cancellationToken);

        // The same check a label goes through, on the shop's end only.
        var probe = new CarrierRateRequest(
            Carrier.ShipFrom, Carrier.ShipFrom, false, clock.Today, [new CarrierBox(1m, 0m, 0m, 0m, 0m)], null, null);
        ShipFromProblems = CarrierRequestRules.LabelProblems(probe)
            .Where(p => p.StartsWith("Ship-from", StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }

    public async Task<IActionResult> OnPostSaveKeysAsync(
        [FromForm(Name = "Keys.Environment")] CarrierEnvironment environment,
        [FromForm(Name = "Keys.ClientId")] string? clientId,
        [FromForm(Name = "Keys.ClientSecret")] string? clientSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            await settings.SaveCredentialsAsync(environment, clientId ?? string.Empty, clientSecret, cancellationToken);
            TempData[StatusKey] = environment == CarrierEnvironment.Production
                ? "Carrier keys saved for production. Labels bought from now on are real and are billed. Test the connection."
                : "Carrier keys saved for staging. Labels bought with them are samples. Test the connection.";
        }
        catch (Exception ex) when (ex is ArgumentException or SecretProtectionException)
        {
            TempData[ErrorKey] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveKeysAsync(CancellationToken cancellationToken)
    {
        await settings.RemoveCredentialsAsync(cancellationToken);
        TempData[StatusKey] = "Carrier keys removed. Shipments are recorded by hand until new keys are saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        var result = await connectionTest.RunAsync(cancellationToken);
        TempData[result.Succeeded ? StatusKey : ErrorKey] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveShipFromAsync(
        [FromForm(Name = "ShipFrom.Contact")] string? contact,
        [FromForm(Name = "ShipFrom.Phone")] string? phone,
        CancellationToken cancellationToken)
    {
        try
        {
            await settings.SaveShipFromAsync(contact, phone, cancellationToken);
            TempData[StatusKey] = "Ship-from contact saved.";
        }
        catch (ArgumentException ex)
        {
            TempData[ErrorKey] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveDefaultsAsync(
        [FromForm(Name = "Defaults.ServiceCode")] string? serviceCode,
        [FromForm(Name = "Defaults.TrackingEnabled")] bool trackingEnabled,
        [FromForm(Name = "Boxes")] List<BoxPresetInput>? boxes,
        CancellationToken cancellationToken)
    {
        try
        {
            var presets = (boxes ?? [])
                .Select(b => new CarrierBoxPreset(b.Name ?? string.Empty, b.LengthIn ?? 0m, b.WidthIn ?? 0m, b.HeightIn ?? 0m))
                .ToList();
            await settings.SaveDefaultsAsync(serviceCode ?? string.Empty, trackingEnabled, presets, cancellationToken);
            TempData[StatusKey] = "Defaults saved.";
        }
        catch (ArgumentException ex)
        {
            TempData[ErrorKey] = ex.Message;
        }

        return RedirectToPage();
    }

    public sealed class BoxPresetInput
    {
        public string? Name { get; set; }
        public decimal? LengthIn { get; set; }
        public decimal? WidthIn { get; set; }
        public decimal? HeightIn { get; set; }
    }
}
