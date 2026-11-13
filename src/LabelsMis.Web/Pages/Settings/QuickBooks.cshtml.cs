using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.QuickBooks;
using LabelsMis.Web.Services.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

/// <summary>Setup → QuickBooks: the connection to the QuickBooks company (keys, connect, test,
/// disconnect, company check), the company's lists read from QuickBooks, the mapping (QB item per
/// line type, class names and the class rule, the keywords that classify charge lines) and the
/// sending settings (payment terms, the PO field, what happens to the email). Accounting keeps it,
/// so Admin and Accounting; the Intuit app keys are Admin only.</summary>
[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Accounting)]
public class QuickBooksModel(
    QuickBooksMappingService mappingService,
    GeneralSettingsService generalSettings,
    QuickBooksConnectionService connectionService,
    QuickBooksCatalogService catalogService,
    Services.PublicUrlService publicUrl,
    Services.ClientClock clock) : PageModel
{
    private const string ConnectionStatusKey = "QuickBooksConnectionStatus";
    private const string ConnectionErrorKey = "QuickBooksConnectionError";
    private const string ListsStatusKey = "QuickBooksListsStatus";
    private const string ListsErrorKey = "QuickBooksListsError";
    private const string SendingStatusKey = "QuickBooksSendingStatus";
    private const string SendingErrorKey = "QuickBooksSendingError";
    private const string PoSearchKey = "QuickBooksPoSearch";

    [BindProperty] public QuickBooksMappingInput Input { get; set; } = new();

    public QuickBooksConnectionView Connection { get; private set; } = QuickBooksConnectionView.Empty(null);

    /// <summary>The Intuit app keys are the developer's; everyone else on the page only connects.</summary>
    public bool CanEditKeys => User.IsInRole(AppRoles.Admin);

    /// <summary>The address Intuit sends the browser back to, shown so it can be copied into the
    /// Intuit app's redirect URIs. Empty when the app's public address is misconfigured.</summary>
    public string RedirectUri
    {
        get
        {
            try
            {
                return publicUrl.Absolute(QuickBooksConnectionService.CallbackPath);
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Whether that address comes from the <c>App__PublicBaseUrl</c> setting. Shown beside
    /// it, because a misnamed setting is silently ignored and the page's own address is used instead.</summary>
    public bool PublicAddressConfigured => publicUrl.IsConfigured;

    /// <summary>A stored UTC time as the person at the screen reads it.</summary>
    public string Local(DateTime? utc, string format = "MMM d, yyyy h:mm tt") =>
        utc is { } value ? clock.ToLocal(value).ToString(format) : string.Empty;

    /// <summary>The E3 shipping settings, shown read-only with a link (Admin edits them).</summary>
    public decimal LocalDeliveryFee { get; private set; }
    public decimal CarrierCostMultiplier { get; private set; }
    public bool CanEditGeneral => User.IsInRole(AppRoles.Admin);

    /// <summary>The company's products/services, classes and terms as last read from QuickBooks.</summary>
    public QuickBooksCatalogLookup Lists { get; private set; } = QuickBooksCatalogLookup.Empty;
    public QuickBooksCatalogCounts ListCounts { get; private set; } = new(0, 0, 0);
    public DateTime? ListsRefreshedAt { get; private set; }

    /// <summary>Each mapped item and class name against the lists, keyed by the Input property.</summary>
    public IReadOnlyDictionary<string, QuickBooksNameCheck> MappingChecks { get; private set; } =
        new Dictionary<string, QuickBooksNameCheck>();

    public int MissingMappingNames => MappingChecks.Values.Count(c => c.IsMissing);

    /// <summary>The saved terms map, PO field and send setting.</summary>
    public QuickBooksPushSettings Push { get; private set; } = QuickBooksPushSettings.Default;
    public IReadOnlyList<QuickBooksTermRow> TermRows { get; private set; } = [];

    /// <summary>What the last "Find the PO field" found (shown once, after the redirect).</summary>
    public QuickBooksCustomFieldSearch? PoSearch { get; private set; }

    /// <summary>A dropdown of QuickBooks names for one mapping slot.</summary>
    public QuickBooksNamePicker MappingPicker(string key, string label)
    {
        var kind = key.EndsWith("Class", StringComparison.Ordinal) ? QuickBooksCatalogKind.Class : QuickBooksCatalogKind.Item;
        var check = MappingChecks.TryGetValue(key, out var found) ? found : new QuickBooksNameCheck(string.Empty, string.Empty, QuickBooksNameStatus.NotChecked);
        return new QuickBooksNamePicker($"Input.{key}", $"Input_{key}", label, Lists.Names(kind), check, NoneText: null, Note: null);
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input = QuickBooksMappingInput.From(await mappingService.GetMappingAsync(cancellationToken));
        await LoadAsync(cancellationToken);
    }

    /// <summary>Saves the Intuit app keys. Binds its three fields by name and ignores ModelState,
    /// which also holds the mapping form's (unposted) required fields.</summary>
    public async Task<IActionResult> OnPostSaveKeysAsync(
        [FromForm(Name = "Keys.Environment")] QuickBooksEnvironment environment,
        [FromForm(Name = "Keys.ClientId")] string? clientId,
        [FromForm(Name = "Keys.ClientSecret")] string? clientSecret,
        CancellationToken cancellationToken)
    {
        if (!CanEditKeys)
        {
            return Forbid();
        }

        try
        {
            var dropped = await connectionService.SaveCredentialsAsync(
                environment, clientId ?? string.Empty, clientSecret, cancellationToken);
            TempData[ConnectionStatusKey] = dropped
                ? "Intuit app keys saved. The company link was dropped because the app or environment changed; connect again."
                : "Intuit app keys saved.";
        }
        catch (Exception ex) when (ex is ArgumentException or SecretProtectionException)
        {
            TempData[ConnectionErrorKey] = ex.Message;
        }

        return RedirectToPage();
    }

    /// <summary>Starts the QuickBooks sign-in: remembers a one-time state value in a cookie and
    /// sends the browser to Intuit's consent screen. Intuit answers at /quickbooks/callback.</summary>
    public async Task<IActionResult> OnPostConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var redirectUri = publicUrl.Absolute(QuickBooksConnectionService.CallbackPath);
            var state = QuickBooksOAuthState.Issue(HttpContext);
            return Redirect(await connectionService.BeginConnectAsync(redirectUri, state, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            TempData[ConnectionErrorKey] = ex.Message;
            return RedirectToPage();
        }
    }

    /// <summary>Reads the company's name and settings from QuickBooks, which proves the saved
    /// sign-in works and refreshes the company check.</summary>
    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await connectionService.TestAsync(cancellationToken);
            TempData[ConnectionStatusKey] =
                $"QuickBooks answered in {result.DurationMs} ms: connected to {result.CompanyName ?? "the company"}. Company check updated.";
        }
        catch (Exception ex) when (ex is QuickBooksApiException or QuickBooksAuthException
                                       or QuickBooksNotConnectedException or SecretProtectionException)
        {
            TempData[ConnectionErrorKey] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(CancellationToken cancellationToken)
    {
        var result = await connectionService.DisconnectAsync(cancellationToken);
        if (!result.WasConnected)
        {
            TempData[ConnectionErrorKey] = "No QuickBooks company was connected.";
        }
        else if (result.RevokeProblem is null)
        {
            TempData[ConnectionStatusKey] = $"Disconnected from {result.CompanyName ?? "QuickBooks"}.";
        }
        else
        {
            TempData[ConnectionStatusKey] =
                $"Disconnected here, but QuickBooks could not be told ({result.RevokeProblem}). "
                + "To finish, remove LabelShop from the company's connected apps in QuickBooks.";
        }

        return RedirectToPage();
    }

    /// <summary>Reads the products/services, classes and payment terms from QuickBooks so the
    /// mapping and the terms can be picked from them.</summary>
    public async Task<IActionResult> OnPostRefreshListsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await catalogService.RefreshAsync(cancellationToken);
            var changes = result.Added + result.Removed == 0 || result.Added == result.Items + result.Classes + result.Terms
                ? string.Empty
                : $" Since the last read: {result.Added} new, {result.Removed} gone.";
            TempData[ListsStatusKey] =
                $"Read from QuickBooks: {Count(result.Items, "product or service", "products and services")}, "
                + $"{Count(result.Classes, "class", "classes")}, {Count(result.Terms, "payment term", "payment terms")}.{changes}";
        }
        catch (Exception ex) when (ex is QuickBooksApiException or QuickBooksAuthException
                                       or QuickBooksNotConnectedException or SecretProtectionException)
        {
            TempData[ListsErrorKey] = $"The lists could not be read from QuickBooks: {ex.Message}";
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: "qb-lists");
    }

    /// <summary>Looks for the custom field that holds the PO number on the company's recent
    /// invoices; the result is listed under the PO field for a person to pick.</summary>
    public async Task<IActionResult> OnPostFindPoFieldAsync(CancellationToken cancellationToken)
    {
        try
        {
            TempData[PoSearchKey] = JsonSerializer.Serialize(await catalogService.FindCustomFieldsAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is QuickBooksApiException or QuickBooksAuthException
                                       or QuickBooksNotConnectedException or SecretProtectionException)
        {
            TempData[SendingErrorKey] = $"QuickBooks could not be asked: {ex.Message}";
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: "qb-po-field");
    }

    /// <summary>Saves the chosen PO field (a blank id stops sending the PO number).</summary>
    public async Task<IActionResult> OnPostUsePoFieldAsync(string? fieldId, string? fieldName, CancellationToken cancellationToken)
    {
        try
        {
            await mappingService.SetPoFieldAsync(fieldId, fieldName, cancellationToken);
            TempData[SendingStatusKey] = string.IsNullOrWhiteSpace(fieldId)
                ? "The PO number is no longer sent to QuickBooks."
                : $"The customer's PO number goes into QuickBooks' \"{fieldName}\" field.";
        }
        catch (ArgumentException ex)
        {
            TempData[SendingErrorKey] = ex.Message;
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: "qb-po-field");
    }

    /// <summary>Saves the payment terms map and the send setting. Binds its fields by name and
    /// ignores ModelState, which also holds the mapping form's (unposted) required fields.</summary>
    public async Task<IActionResult> OnPostSaveSendingAsync(
        [FromForm(Name = "Sending.Terms")] Dictionary<string, string?>? terms,
        [FromForm(Name = "Sending.SendMode")] QuickBooksSendMode sendMode,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<PaymentTerms, string>();
        foreach (var (key, value) in terms ?? [])
        {
            if (Enum.TryParse<PaymentTerms>(key, out var term) && Enum.IsDefined(term) && !string.IsNullOrWhiteSpace(value))
            {
                map[term] = value.Trim();
            }
        }

        try
        {
            await mappingService.SaveSendingAsync(map, sendMode, cancellationToken);
            TempData[SendingStatusKey] = "Payment terms and sending saved.";
        }
        catch (ArgumentException ex)
        {
            TempData[SendingErrorKey] = ex.Message;
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: "qb-sending");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await mappingService.UpdateAsync(Input.ToMapping(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["QuickBooksStatus"] = "QuickBooks mapping saved.";
        return RedirectToPage();
    }

    private static string Count(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count:N0} {many}";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Connection = await connectionService.GetViewAsync(cancellationToken);
        Lists = await catalogService.GetLookupAsync(cancellationToken);
        ListCounts = await catalogService.GetCountsAsync(cancellationToken);
        ListsRefreshedAt = await catalogService.GetRefreshedAtAsync(cancellationToken);
        MappingChecks = QuickBooksMappingSlots.Check(Input.ToMapping(), Lists);
        Push = await mappingService.GetPushSettingsAsync(cancellationToken);
        TermRows = QuickBooksTermRow.Build(Push, Lists);
        if (TempData[PoSearchKey] is string search)
        {
            try
            {
                PoSearch = JsonSerializer.Deserialize<QuickBooksCustomFieldSearch>(search);
            }
            catch (JsonException)
            {
                PoSearch = null;
            }
        }

        var settings = await generalSettings.GetAsync(cancellationToken);
        LocalDeliveryFee = settings?.LocalDeliveryFee ?? Domain.Shipping.ShippingBillingSettings.Default.LocalDeliveryFee;
        CarrierCostMultiplier = settings?.CarrierCostMultiplier ?? Domain.Shipping.ShippingBillingSettings.Default.CarrierCostMultiplier;
    }
}

/// <summary>One dropdown of QuickBooks names. With no list read yet it is a text box.</summary>
public sealed record QuickBooksNamePicker(
    string Name,
    string Id,
    string Label,
    IReadOnlyList<string> Options,
    QuickBooksNameCheck Check,
    string? NoneText,
    string? Note);

/// <summary>One LabelShop payment term and the QuickBooks term it becomes.</summary>
public sealed record QuickBooksTermRow(PaymentTerms Terms, QuickBooksNamePicker Picker)
{
    public const string NoneText = "No terms: send the due date only";

    /// <summary>A term with nothing chosen gets the QuickBooks term of the same name pre-selected
    /// (saved with the form); a chosen term is checked against the list like the mapping.</summary>
    public static IReadOnlyList<QuickBooksTermRow> Build(QuickBooksPushSettings push, QuickBooksCatalogLookup lists)
    {
        var names = lists.Names(QuickBooksCatalogKind.Term);
        return Enum.GetValues<PaymentTerms>().Select(terms =>
        {
            var saved = push.TermFor(terms);
            var matched = saved is null ? QuickBooksTermsMatcher.Match(terms, names) : null;
            var check = saved is null && matched is null
                ? new QuickBooksNameCheck(string.Empty, string.Empty, QuickBooksNameStatus.NotChecked)
                : QuickBooksNameCheck.Check(lists, QuickBooksCatalogKind.Term, saved ?? matched);
            var note = matched is null ? null : "Matched by name; saved when you save this section.";
            return new QuickBooksTermRow(terms, new QuickBooksNamePicker(
                $"Sending.Terms[{terms}]", $"Sending_Terms_{terms}", terms.Label(), names, check, NoneText, note));
        }).ToList();
    }
}

public class QuickBooksMappingInput
{
    [Required, StringLength(100)] [Display(Name = "Sales (product lines)")] public string SalesItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Shipping")] public string ShippingItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Design")] public string DesignItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Rush")] public string RushItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Die creation")] public string DieCreationItem { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Roll label")] public string RollLabelClass { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Shrink sleeves")] public string ShrinkSleevesClass { get; set; } = string.Empty;
    [Required, StringLength(100)] [Display(Name = "Buyout")] public string BuyoutClass { get; set; } = string.Empty;
    [StringLength(500)] [Display(Name = "Design keywords")] public string? DesignKeywords { get; set; }
    [StringLength(500)] [Display(Name = "Rush keywords")] public string? RushKeywords { get; set; }
    [StringLength(500)] [Display(Name = "Die creation keywords")] public string? DieCreationKeywords { get; set; }
    [StringLength(50)] [Display(Name = "Buyout substrate code")] public string? OutsourcedSubstrateCode { get; set; }

    public static QuickBooksMappingInput From(QuickBooksMapping m) => new()
    {
        SalesItem = m.SalesItem,
        ShippingItem = m.ShippingItem,
        DesignItem = m.DesignItem,
        RushItem = m.RushItem,
        DieCreationItem = m.DieCreationItem,
        RollLabelClass = m.RollLabelClass,
        ShrinkSleevesClass = m.ShrinkSleevesClass,
        BuyoutClass = m.BuyoutClass,
        DesignKeywords = m.DesignKeywords,
        RushKeywords = m.RushKeywords,
        DieCreationKeywords = m.DieCreationKeywords,
        OutsourcedSubstrateCode = m.OutsourcedSubstrateCode
    };

    public QuickBooksMapping ToMapping() => new(
        SalesItem,
        ShippingItem,
        DesignItem,
        RushItem,
        DieCreationItem,
        RollLabelClass,
        ShrinkSleevesClass,
        BuyoutClass,
        DesignKeywords ?? string.Empty,
        RushKeywords ?? string.Empty,
        DieCreationKeywords ?? string.Empty,
        OutsourcedSubstrateCode ?? string.Empty);
}
