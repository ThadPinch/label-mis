using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Carriers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Setup;

/// <summary>The in-app manual. The UPS labels section offers two guided walkthroughs
/// (wwwroot/js/walkthroughs.js); which of them this person can start is decided here.</summary>
[Authorize]
public class DocumentationModel(
    CarrierSettingsService carrierSettings,
    CarrierShipmentService carrierShipments,
    IAuthorizationService authorization) : PageModel
{
    /// <summary>Setup → Shipping carrier is an administrators' page.</summary>
    public bool CanSetUpCarrier => User.IsInRole(AppRoles.Admin);

    /// <summary>Record shipment, and with it buying a label.</summary>
    public bool CanShip { get; private set; }

    /// <summary>Keys are saved and readable, so Record shipment offers Buy a UPS label.</summary>
    public bool CarrierConnected { get; private set; }

    public string CarrierStateText { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanShip = (await authorization.AuthorizeAsync(User, TransactionPolicies.ShippingEdit)).Succeeded;
        var carrier = await carrierSettings.GetViewAsync(cancellationToken);
        CarrierConnected = carrier.IsReady;
        CarrierStateText = !carrier.IsReady
            ? "Right now: the carrier is not connected, so shipments are recorded with typed tracking numbers."
            : carrier.Environment == CarrierEnvironment.Production
                ? "Right now: connected with production keys. Labels bought are real and are billed."
                : "Right now: connected with staging keys. Labels bought are samples: they can't ship and nothing is billed.";
    }

    /// <summary>Addresses a walkthrough can't work out from the page it is on. A value is null when
    /// there is nothing to show, and the walkthrough says so instead of opening an empty page.</summary>
    public async Task<IActionResult> OnGetWalkthroughTargetsAsync(CancellationToken cancellationToken)
    {
        string? labelShipment = null;
        if ((await authorization.AuthorizeAsync(User, TransactionPolicies.ShippingRead)).Succeeded
            && await carrierShipments.GetNewestLabelShipmentIdAsync(cancellationToken) is { } shipmentId)
        {
            labelShipment = Url.Page("/Shipments/Detail", new { id = shipmentId });
        }

        return new JsonResult(new { labelShipment });
    }
}
