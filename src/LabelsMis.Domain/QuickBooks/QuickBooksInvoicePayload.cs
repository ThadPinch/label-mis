using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>An address as the invoice preview holds it.</summary>
public sealed record QuickBooksAddressSource(
    string? Name, string? Line1, string? Line2, string? City, string? State, string? Zip, string? Country);

/// <summary>One invoice line as the preview has it. <paramref name="Quantity"/> is the quantity the
/// QuickBooks line shows (the ordered quantity); <paramref name="Amount"/> is what LabelShop bills for
/// the line, which is what QuickBooks gets whatever the quantity and rate say.</summary>
public sealed record QuickBooksLineSource(
    int LineNumber,
    string Item,
    string Class,
    bool ItemOverridden,
    bool ClassOverridden,
    string Description,
    decimal Quantity,
    decimal Rate,
    decimal Amount);

/// <summary>Everything the payload is built from: the invoice preview plus the facts the preview
/// doesn't carry (the customer's LabelShop terms, the per-invoice send options).</summary>
public sealed record QuickBooksInvoiceSource(
    Guid InvoiceId,
    string DocNumber,
    DateOnly TxnDate,
    DateOnly DueDate,
    PaymentTerms Terms,
    Guid CustomerId,
    string CustomerName,
    string? QuickBooksCustomerId,
    string? PoNumber,
    string? BillingEmail,
    QuickBooksAddressSource? BillTo,
    QuickBooksAddressSource? ShipTo,
    IReadOnlyList<QuickBooksLineSource> Lines,
    string ShippingItem,
    string ShippingClass,
    decimal ShippingAmount,
    bool HoldSend = false,
    string? SendToOverride = null);

/// <summary>Something that stops the invoice going to QuickBooks, in shop words, with where to fix it.</summary>
public sealed record QuickBooksPayloadProblem(string Code, string Message, string FixUrl);

/// <summary>
/// The invoice as QuickBooks' Invoice create body. <see cref="Json"/> is canonical (fixed property
/// order, trailing zeros dropped), so the same invoice always gives the same text, the same
/// <see cref="Hash"/> and the same request id.
/// </summary>
public sealed class QuickBooksInvoicePayload
{
    /// <summary>Intuit's limit on <c>requestid</c> for anything but a batch.</summary>
    public const int MaxRequestIdLength = 50;

    private readonly JsonObject _body;

    internal QuickBooksInvoicePayload(JsonObject body, decimal total, string billEmail, bool emailNow, string? sendTo)
    {
        _body = body;
        Json = body.ToJsonString();
        Hash = Sha256Hex(Json);
        Total = total;
        BillEmail = billEmail;
        EmailNow = emailNow;
        SendTo = sendTo;
    }

    /// <summary>The create body.</summary>
    public string Json { get; }

    /// <summary>SHA-256 of <see cref="Json"/>, hex: kept on the invoice as what was last sent.</summary>
    public string Hash { get; }

    /// <summary>Lines plus shipping, before QuickBooks adds any tax.</summary>
    public decimal Total { get; }

    public string BillEmail { get; }

    /// <summary>Ask QuickBooks to email it as soon as it is created (send setting "send now", not held).</summary>
    public bool EmailNow { get; }

    /// <summary>The one-off address to email it to, when the invoice has one.</summary>
    public string? SendTo { get; }

    /// <summary>The sparse update body for the QuickBooks invoice <paramref name="id"/> at version
    /// <paramref name="syncToken"/>. Lines are replaced; the email status is left as QuickBooks has it,
    /// so an update never puts an invoice back in QuickBooks' send list.</summary>
    public string UpdateJson(string id, string syncToken)
    {
        var update = new JsonObject
        {
            ["Id"] = id,
            ["SyncToken"] = syncToken,
            ["sparse"] = true
        };
        foreach (var (key, value) in _body)
        {
            if (key != "EmailStatus")
            {
                update[key] = value?.DeepClone();
            }
        }

        return update.ToJsonString();
    }

    /// <summary>The void body (<c>operation=void</c>).</summary>
    public static string VoidJson(string id, string syncToken) =>
        new JsonObject { ["Id"] = id, ["SyncToken"] = syncToken }.ToJsonString();

    /// <summary>The <c>requestid</c> for one call: hex SHA-256 of the invoice id, the operation and
    /// the body, cut to Intuit's 50 characters. A repeat of the same call gets the same id (Intuit
    /// answers it with the first result); any change to the body gives a new one.</summary>
    public static string RequestId(Guid invoiceId, string operation, string? body) =>
        Sha256Hex($"{invoiceId:N}\n{operation}\n{body}")[..MaxRequestIdLength];

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>The payload, or why there can't be one.</summary>
public sealed record QuickBooksPayloadResult(QuickBooksInvoicePayload? Payload, IReadOnlyList<QuickBooksPayloadProblem> Problems)
{
    public bool Ok => Payload is not null;
}

/// <summary>
/// Builds the QuickBooks Invoice body from the invoice preview (Q14). Rules, from the sandbox checks
/// in <c>docs/quickbooks-phase1.md</c>:
/// <list type="bullet">
/// <item>DocNumber = the LabelShop number; TxnDate and DueDate always; terms only when the terms map
/// names a term QuickBooks has.</item>
/// <item>Every line's Amount is what LabelShop bills. Quantity and rate go with it only when
/// quantity × rate, rounded half up to the cent, equals that amount; otherwise quantity and amount
/// without a rate (QuickBooks refuses a mismatch with 6070 and never recalculates).</item>
/// <item>Shipping is its own line with the mapped Shipping item and the job's class. Tax is never
/// sent: each line says TAX or NON from its QuickBooks item, and QuickBooks works the tax out.</item>
/// <item>BillEmail always (the invoice's one-off address wins); EmailStatus NeedToSend for "send
/// later", NotSet otherwise and whenever the invoice is held.</item>
/// <item>The PO number as a custom field when the PO field's id is known.</item>
/// </list>
/// </summary>
public static class QuickBooksInvoicePayloadBuilder
{
    /// <summary>QuickBooks' longest DocNumber.</summary>
    public const int MaxDocNumberLength = 21;

    public static QuickBooksPayloadResult Build(
        QuickBooksInvoiceSource source,
        QuickBooksCatalogLookup catalog,
        QuickBooksPushSettings settings)
    {
        var problems = new List<QuickBooksPayloadProblem>();
        var settingsUrl = QuickBooksFixLinks.Settings;
        var customerUrl = QuickBooksFixLinks.Customer(source.CustomerId);
        var previewUrl = QuickBooksFixLinks.InvoicePreview(source.InvoiceId);
        var invoiceUrl = QuickBooksFixLinks.Invoice(source.InvoiceId);

        var docNumber = source.DocNumber.Trim();
        if (docNumber.Length > MaxDocNumberLength)
        {
            problems.Add(new(QuickBooksErrorMessages.Number,
                $"Invoice number {docNumber} is longer than the {MaxDocNumberLength} characters QuickBooks allows.", invoiceUrl));
        }

        var customerId = source.QuickBooksCustomerId?.Trim();
        if (string.IsNullOrEmpty(customerId))
        {
            problems.Add(new(QuickBooksErrorMessages.Customer,
                $"{source.CustomerName} isn't linked to a QuickBooks customer yet. Link it on the customer's page.", customerUrl));
        }

        var billEmail = FirstNonBlank(source.SendToOverride, source.BillingEmail);
        if (billEmail is null)
        {
            problems.Add(new(QuickBooksErrorMessages.Email,
                $"There's no email address for the invoice. Tick Billing on a contact with an email on {source.CustomerName}'s "
                + "page, or give this invoice an \"Email to instead\" address.", customerUrl));
        }

        if (source.Lines.Count == 0 && Cents(source.ShippingAmount) == 0m)
        {
            problems.Add(new(QuickBooksErrorMessages.NoLines, "This invoice has no lines to send.", invoiceUrl));
        }

        var haveItems = catalog.HasAny(QuickBooksCatalogKind.Item);
        var haveClasses = catalog.HasAny(QuickBooksCatalogKind.Class);
        if (!haveItems || !haveClasses)
        {
            problems.Add(new(QuickBooksErrorMessages.Lists,
                "LabelShop hasn't read QuickBooks' products, services and classes yet. Refresh the lists on Setup → QuickBooks.",
                settingsUrl));
        }

        var lines = new JsonArray();
        var total = 0m;
        foreach (var line in source.Lines.OrderBy(l => l.LineNumber))
        {
            var where = $"Line {line.LineNumber}";
            var item = haveItems ? ResolveItem(catalog, line.Item, where, line.ItemOverridden, previewUrl, problems) : null;
            var qbClass = haveClasses ? ResolveClass(catalog, line.Class, where, line.ClassOverridden, previewUrl, problems) : null;
            var amount = Cents(line.Amount);
            total += amount;

            var detail = new JsonObject();
            if (item is not null)
            {
                detail["ItemRef"] = Ref(item.Id);
            }

            if (qbClass is not null)
            {
                detail["ClassRef"] = Ref(qbClass.Id);
            }

            detail["Qty"] = Number(line.Quantity);
            if (line.Quantity != 0m && Cents(line.Quantity * line.Rate) == amount)
            {
                detail["UnitPrice"] = Number(line.Rate);
            }

            detail["TaxCodeRef"] = Ref(item?.Taxable == true ? "TAX" : "NON");
            lines.Add(new JsonObject
            {
                ["DetailType"] = "SalesItemLineDetail",
                ["Amount"] = Number(amount),
                ["Description"] = line.Description,
                ["SalesItemLineDetail"] = detail
            });
        }

        var shipping = Cents(source.ShippingAmount);
        if (shipping != 0m)
        {
            var item = haveItems ? ResolveItem(catalog, source.ShippingItem, "Shipping", false, previewUrl, problems) : null;
            var qbClass = haveClasses ? ResolveClass(catalog, source.ShippingClass, "Shipping", false, previewUrl, problems) : null;
            total += shipping;
            var detail = new JsonObject();
            if (item is not null)
            {
                detail["ItemRef"] = Ref(item.Id);
            }

            if (qbClass is not null)
            {
                detail["ClassRef"] = Ref(qbClass.Id);
            }

            detail["TaxCodeRef"] = Ref(item?.Taxable == true ? "TAX" : "NON");
            lines.Add(new JsonObject
            {
                ["DetailType"] = "SalesItemLineDetail",
                ["Amount"] = Number(shipping),
                ["Description"] = "Shipping",
                ["SalesItemLineDetail"] = detail
            });
        }

        if (problems.Count > 0)
        {
            return new QuickBooksPayloadResult(null, problems);
        }

        var body = new JsonObject
        {
            ["DocNumber"] = docNumber,
            ["TxnDate"] = Date(source.TxnDate),
            ["DueDate"] = Date(source.DueDate),
            ["CustomerRef"] = Ref(customerId!)
        };

        var term = catalog.Find(QuickBooksCatalogKind.Term, settings.TermFor(source.Terms));
        if (term is not null)
        {
            body["SalesTermRef"] = Ref(term.Id);
        }

        if (Address(source.BillTo) is { } billAddr)
        {
            body["BillAddr"] = billAddr;
        }

        if (Address(source.ShipTo) is { } shipAddr)
        {
            body["ShipAddr"] = shipAddr;
        }

        body["BillEmail"] = new JsonObject { ["Address"] = billEmail };
        body["EmailStatus"] = settings.SendMode == QuickBooksSendMode.SendLater && !source.HoldSend ? "NeedToSend" : "NotSet";

        var po = source.PoNumber?.Trim();
        var poField = settings.PoFieldId?.Trim();
        if (!string.IsNullOrEmpty(po) && !string.IsNullOrEmpty(poField))
        {
            body["CustomField"] = new JsonArray(new JsonObject
            {
                ["DefinitionId"] = poField,
                ["Type"] = "StringType",
                ["StringValue"] = po
            });
        }

        body["Line"] = lines;

        var emailNow = settings.SendMode == QuickBooksSendMode.SendNow && !source.HoldSend;
        var sendTo = string.IsNullOrWhiteSpace(source.SendToOverride) ? null : source.SendToOverride.Trim();
        return new QuickBooksPayloadResult(new QuickBooksInvoicePayload(body, total, billEmail!, emailNow, sendTo), []);
    }

    /// <summary>Rounded half up to the cent, the way QuickBooks compares amounts.</summary>
    public static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static QuickBooksListEntry? ResolveItem(
        QuickBooksCatalogLookup catalog, string name, string where, bool overridden, string previewUrl, List<QuickBooksPayloadProblem> problems)
    {
        var entry = catalog.Find(QuickBooksCatalogKind.Item, name);
        if (entry is null)
        {
            problems.Add(overridden
                ? new(QuickBooksErrorMessages.LineItem,
                    $"{where}: QuickBooks has no product or service called \"{name}\". Pick another one on the invoice's QuickBooks preview.",
                    previewUrl)
                : new(QuickBooksErrorMessages.Item,
                    $"{where}: QuickBooks has no product or service called \"{name}\". Pick the right one on Setup → QuickBooks "
                    + "(or refresh the lists there).",
                    QuickBooksFixLinks.Settings));
        }

        return entry;
    }

    private static QuickBooksListEntry? ResolveClass(
        QuickBooksCatalogLookup catalog, string name, string where, bool overridden, string previewUrl, List<QuickBooksPayloadProblem> problems)
    {
        var entry = catalog.Find(QuickBooksCatalogKind.Class, name);
        if (entry is null)
        {
            var what = string.IsNullOrWhiteSpace(name) ? "has no class" : $"names the class \"{name}\", which QuickBooks doesn't have";
            problems.Add(overridden
                ? new(QuickBooksErrorMessages.LineClass,
                    $"{where} {what}. Pick another class on the invoice's QuickBooks preview.", previewUrl)
                : new(QuickBooksErrorMessages.Class,
                    $"{where} {what}. Pick the right class on Setup → QuickBooks (or refresh the lists there).",
                    QuickBooksFixLinks.Settings));
        }

        return entry;
    }

    private static JsonObject? Address(QuickBooksAddressSource? address)
    {
        if (address is null)
        {
            return null;
        }

        var result = new JsonObject();
        var lineNumber = 1;
        foreach (var text in new[] { address.Name, address.Line1, address.Line2 })
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                result[$"Line{lineNumber++}"] = text.Trim();
            }
        }

        Add(result, "City", address.City);
        Add(result, "CountrySubDivisionCode", address.State);
        Add(result, "PostalCode", address.Zip);
        Add(result, "Country", address.Country);
        return result.Count == 0 ? null : result;
    }

    private static void Add(JsonObject target, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[name] = value.Trim();
        }
    }

    private static JsonObject Ref(string id) => new() { ["value"] = id };

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The value without trailing zeros (500.00 → 500, 0.1230 → 0.123), so the JSON, and
    /// with it the hash and request id, don't depend on the scale the database handed back.</summary>
    private static JsonNode Number(decimal value) => JsonValue.Create(value / 1.0000000000000000000000000000m);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
