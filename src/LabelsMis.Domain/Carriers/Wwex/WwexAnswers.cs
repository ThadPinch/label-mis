using System.Globalization;
using System.Text;
using System.Text.Json;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Carriers.Wwex;

/// <summary>A token from Worldwide Express's sign-in, and what it says about the account.</summary>
public sealed record WwexToken(string AccessToken, DateTime ExpiresAtUtc, string? Account, string? Plan);

/// <summary>Reads the answers of Worldwide Express's SpeedShip API. Every shape handled here was
/// seen on staging (docs/carrier-labels.md); the saved answers are the fixtures of the tests.
/// Anything unexpected becomes a <see cref="CarrierException"/>, never a half-read result.</summary>
public static class WwexAnswers
{
    private const string CarrierName = "Worldwide Express";

    /// <summary>The sign-in answer: <c>{"access_token": "…", "expires_in": 86400}</c>.</summary>
    public static WwexToken ParseToken(string json, DateTime nowUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var token = Text(root, "access_token");
            var expiresIn = Number(root, "expires_in");
            if (string.IsNullOrWhiteSpace(token) || expiresIn is not > 0)
            {
                throw new CarrierException(CarrierErrorKind.Unreadable, $"{CarrierName} returned an incomplete sign-in answer.");
            }

            var (account, plan) = ReadClaims(token);
            return new WwexToken(token, nowUtc.AddSeconds((double)expiresIn.Value), account, plan);
        }
        catch (JsonException ex)
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable, $"{CarrierName} returned a sign-in answer that could not be read.", inner: ex);
        }
    }

    /// <summary>The exception for an answer that is not a 2xx. A refusal carries
    /// <c>clientStatus.message</c> ("weight is required; exception: AppException"); the gateway's own
    /// refusals carry a bare <c>message</c>; the sign-in's carry <c>error_description</c>.</summary>
    public static CarrierException Failure(int statusCode, string? body)
    {
        string? message = null;
        string? correlationId = null;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            correlationId = Text(root, "correlationId");
            message = CleanMessage(Text(Child(root, "clientStatus"), "message"))
                ?? CleanMessage(Text(root, "error_description"))
                ?? CleanMessage(Text(root, "message"));
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's HTML error page): the status code is all there is to report.
        }

        return statusCode switch
        {
            401 or 403 => new CarrierException(
                CarrierErrorKind.Unauthorized,
                $"{CarrierName} refused the sign-in. Check the client ID and secret in Setup → Shipping carrier.",
                statusCode,
                correlationId),
            >= 500 => new CarrierException(
                CarrierErrorKind.Server,
                message is null
                    ? $"{CarrierName} had a problem and could not complete the request (answer {statusCode})."
                    : $"{CarrierName} could not complete the request: {message}",
                statusCode,
                correlationId),
            _ => new CarrierException(
                CarrierErrorKind.Refused,
                message is null ? $"{CarrierName} refused the request (answer {statusCode})." : $"{CarrierName}: {message}",
                statusCode,
                correlationId)
        };
    }

    /// <summary>The rate answer. <paramref name="boxes"/> are the boxes that were asked for, in
    /// order; each rate's boxes come back in that order.</summary>
    public static CarrierRateQuote ParseRates(string json, IReadOnlyList<CarrierBox> boxes)
    {
        using var document = Open(json, out var response, out var correlationId);
        var offers = Child(response, "offerList");
        if (offers.ValueKind != JsonValueKind.Array || offers.GetArrayLength() == 0)
        {
            throw new CarrierException(
                CarrierErrorKind.Refused,
                $"{CarrierName} returned no UPS services for this shipment."
                + (CleanMessage(Text(response, "message")) is { } said ? $" It said: {said}" : string.Empty),
                correlationId: correlationId);
        }

        string? quoteId = null;
        CarrierBillTo? billTo = null;
        var rates = new List<CarrierRate>();
        foreach (var offer in offers.EnumerateArray())
        {
            var offerId = Text(offer, "offerId");
            var products = Child(offer, "offeredProductList");
            if (string.IsNullOrWhiteSpace(offerId) || products.ValueKind != JsonValueKind.Array || products.GetArrayLength() == 0)
            {
                continue;
            }

            var firstShipment = Child(products[0], "shopRQShipment");
            var transit = Child(firstShipment, "timeInTransit");
            var serviceCode = Text(transit, "upsServiceCode");
            var total = Money(Child(offer, "totalOfferPrice"));
            if (string.IsNullOrWhiteSpace(serviceCode) || total is null)
            {
                continue;
            }

            quoteId ??= Text(offer, "productTransactionId");
            billTo ??= ReadBillTo(firstShipment);

            var priced = new List<PricedBox>();
            foreach (var product in products.EnumerateArray())
            {
                var unit = First(Child(Child(product, "shopRQShipment"), "handlingUnitList"));
                var key = Text(unit, "handlingUnitId");
                var cost = Money(Child(product, "offerPrice"));
                if (string.IsNullOrWhiteSpace(key) || cost is null)
                {
                    throw new CarrierException(
                        CarrierErrorKind.Unreadable,
                        $"{CarrierName} returned a rate without a price for one of the boxes.",
                        correlationId: correlationId);
                }

                var dimension = Child(unit, "billedDimension");
                priced.Add(new PricedBox(
                    new CarrierBoxRate(key, cost.Value, ReadCharges(product)),
                    Decimal(Child(unit, "weight"), "value"),
                    Decimal(Child(dimension, "length"), "value"),
                    Decimal(Child(dimension, "width"), "value"),
                    Decimal(Child(dimension, "height"), "value")));
            }

            if (priced.Count != boxes.Count)
            {
                throw new CarrierException(
                    CarrierErrorKind.Unreadable,
                    $"{CarrierName} priced {priced.Count} boxes for a shipment of {boxes.Count}.",
                    correlationId: correlationId);
            }

            rates.Add(new CarrierRate(
                offerId,
                serviceCode.Trim().ToUpperInvariant(),
                UpsServices.Name(serviceCode, Text(transit, "serviceDescription")),
                total.Value,
                (int?)Number(transit, "transitDays"),
                Date(Text(transit, "estimatedDeliveryDate")),
                ClockTime(Text(transit, "deliveryBy")),
                Decimal(Child(firstShipment, "totalBilledWeight"), "value"),
                InRequestOrder(priced, boxes)));
        }

        if (rates.Count == 0 || string.IsNullOrWhiteSpace(quoteId))
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable,
                $"{CarrierName} returned services that could not be read.",
                correlationId: correlationId);
        }

        return new CarrierRateQuote(
            quoteId, rates.OrderBy(r => r.TotalCost).ThenBy(r => r.ServiceCode, StringComparer.Ordinal).ToList(), billTo, correlationId);
    }

    /// <summary>The booking answer. An answer without the order is not a booking, even with a 200:
    /// a quote that already has one answers "Shipment already has order placed".</summary>
    public static CarrierBooking ParseBooking(string json)
    {
        using var document = Open(json, out var response, out var correlationId);
        var order = Child(Child(response, "shipmentOrderResponse"), "order");
        if (order.ValueKind != JsonValueKind.Object)
        {
            var said = CleanMessage(Text(response, "message"));
            if (said is not null && said.Contains("already has order", StringComparison.OrdinalIgnoreCase))
            {
                throw new CarrierException(
                    CarrierErrorKind.AlreadyBooked,
                    $"{CarrierName} already has a booking for this shipment.",
                    correlationId: correlationId);
            }

            throw new CarrierException(
                said is null ? CarrierErrorKind.Unreadable : CarrierErrorKind.Refused,
                said is null ? $"{CarrierName} did not confirm the booking." : $"{CarrierName}: {said}",
                correlationId: correlationId);
        }

        var orderNumber = Text(order, "orderId");
        var boxes = new List<CarrierBookedBox>();
        var items = Child(order, "orderedItemList");
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var tracking = TrackingNumber(Child(item, "secondaryTxnIdList"));
                var boxKey = FirstText(Child(item, "handlingUnitIdList"));
                var packageId = Text(item, "orderedItemId");
                if (tracking is null || boxKey is null || string.IsNullOrWhiteSpace(packageId))
                {
                    boxes.Clear();
                    break;
                }

                boxes.Add(new CarrierBookedBox(boxKey, tracking, packageId));
            }
        }

        if (string.IsNullOrWhiteSpace(orderNumber) || boxes.Count == 0)
        {
            // Booked, by the look of it, but without what LabelShop needs to record it. Unreadable
            // keeps the outcome "unknown", so the caller settles it with the carrier.
            throw new CarrierException(
                CarrierErrorKind.Unreadable,
                $"{CarrierName} answered the booking without a tracking number for every box.",
                correlationId: correlationId);
        }

        return new CarrierBooking(orderNumber, boxes, correlationId);
    }

    /// <summary>A label file: base64 under <c>reportList[0].report.documentBinary</c> for one box,
    /// under <c>fileContent</c> for the Letter PDF of every box.</summary>
    public static CarrierLabel ParseLabel(string json, CarrierLabelFormat format)
    {
        using var document = Open(json, out var response, out var correlationId);
        var encoded = format == CarrierLabelFormat.PdfLetter
            ? Text(response, "fileContent")
            : Text(Child(First(Child(response, "reportList")), "report"), "documentBinary");
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable,
                $"{CarrierName} returned no label."
                + (CleanMessage(Text(response, "message")) is { } said ? $" It said: {said}" : string.Empty),
                correlationId: correlationId);
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(encoded);
        }
        catch (FormatException ex)
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable, $"{CarrierName} returned a label that could not be read.", correlationId: correlationId, inner: ex);
        }

        var looksRight = format == CarrierLabelFormat.Zpl
            ? Encoding.ASCII.GetString(content, 0, Math.Min(content.Length, 200)).Contains("^XA", StringComparison.Ordinal)
            : content.Length > 4 && content[0] == (byte)'%' && content[1] == (byte)'P' && content[2] == (byte)'D' && content[3] == (byte)'F';
        if (!looksRight)
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable,
                $"{CarrierName} returned a file that is not a {(format == CarrierLabelFormat.Zpl ? "ZPL" : "PDF")} label.",
                correlationId: correlationId);
        }

        return new CarrierLabel(content, format);
    }

    /// <summary>The void answer, in either of its two shapes: <c>orderCancelStatus: VOIDED</c> the
    /// first time, <c>isPtrAlreadyVoided: true</c> on a repeat.</summary>
    public static CarrierVoidOutcome ParseVoid(string json)
    {
        using var document = Open(json, out var response, out var correlationId);
        var status = Text(Child(response, "shipmentOrderCancelRS"), "orderCancelStatus");
        if (string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            return CarrierVoidOutcome.Voided;
        }

        var alreadyVoided = Child(response, "isPtrAlreadyVoided").ValueKind == JsonValueKind.True
            || string.Equals(Text(response, "shipmentPTRFinalStatus"), "VOID", StringComparison.OrdinalIgnoreCase);
        if (alreadyVoided)
        {
            return CarrierVoidOutcome.AlreadyVoided;
        }

        throw new CarrierException(
            CarrierErrorKind.Unreadable,
            $"{CarrierName} did not confirm the void."
            + (CleanMessage(status ?? Text(response, "message")) is { } said ? $" It said: {said}" : string.Empty),
            correlationId: correlationId);
    }

    /// <summary>The tracking answer: every box of the shipment, each with its events.</summary>
    public static CarrierTracking ParseTracking(string json)
    {
        using var document = Open(json, out var response, out var correlationId);
        var items = Child(response, "orderedItemFulfillmentStatusList");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return CarrierTracking.NotFound(correlationId);
        }

        var boxes = new List<CarrierBoxTracking>();
        foreach (var item in items.EnumerateArray())
        {
            var packageId = Text(item, "orderedItemId");
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            var events = new List<CarrierTrackingEvent>();
            var list = Child(item, "fulfillmentStatusList");
            if (list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    var code = Text(entry, "code");
                    var at = EventTime(Text(entry, "eventDateTime"), Text(entry, "timeZoneOffset"));
                    if (!string.IsNullOrWhiteSpace(code) && at is not null)
                    {
                        events.Add(new CarrierTrackingEvent(code.Trim().ToUpperInvariant(), at.Value));
                    }
                }
            }

            events.Sort((a, b) => a.OccurredAtUtc.CompareTo(b.OccurredAtUtc));
            var status = Text(item, "status") ?? (events.Count > 0 ? events[^1].Code : string.Empty);
            boxes.Add(new CarrierBoxTracking(packageId, status.Trim().ToUpperInvariant(), events));
        }

        return boxes.Count == 0 ? CarrierTracking.NotFound(correlationId) : new CarrierTracking(true, boxes, correlationId);
    }

    /// <summary>WWEX appends its exception class names to messages ("weight is required; exception:
    /// AppException"); only the part before them is for people.</summary>
    public static string? CleanMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var cut = message.IndexOf("; exception:", StringComparison.OrdinalIgnoreCase);
        var text = (cut >= 0 ? message[..cut] : message).Trim().TrimEnd('.', ';', ':').Trim();
        return text.Length == 0 || text.StartsWith('<') ? null : text;
    }

    /// <summary>An event's local time and its offset ("2026-11-24 10:43:58", "-0600") as UTC.</summary>
    public static DateTime? EventTime(string? localTime, string? offset)
    {
        if (!DateTime.TryParseExact(
                localTime?.Trim(),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
        {
            return null;
        }

        var shift = TimeSpan.Zero;
        var text = (offset ?? string.Empty).Trim().Replace(":", string.Empty);
        if (text.Length == 5 && (text[0] == '-' || text[0] == '+')
            && int.TryParse(text.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            && int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            shift = new TimeSpan(hours, minutes, 0);
            if (text[0] == '-')
            {
                shift = -shift;
            }
        }

        return DateTime.SpecifyKind(local - shift, DateTimeKind.Utc);
    }

    private sealed record PricedBox(CarrierBoxRate Rate, decimal? WeightLb, decimal? LengthIn, decimal? WidthIn, decimal? HeightIn);

    /// <summary>Puts the carrier's boxes in the order they were asked for. A box is recognised by
    /// its weight and size; boxes that look alike are interchangeable, and anything left over
    /// keeps the carrier's order.</summary>
    private static List<CarrierBoxRate> InRequestOrder(List<PricedBox> priced, IReadOnlyList<CarrierBox> boxes)
    {
        var result = new CarrierBoxRate?[boxes.Count];
        var unused = new List<PricedBox>(priced);
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            var match = unused.FirstOrDefault(p =>
                p.WeightLb == box.WeightLb
                && (!box.HasDimensions || (p.LengthIn == box.LengthIn && p.WidthIn == box.WidthIn && p.HeightIn == box.HeightIn)));
            if (match is not null)
            {
                result[i] = match.Rate;
                unused.Remove(match);
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] is null)
            {
                result[i] = unused[0].Rate;
                unused.RemoveAt(0);
            }
        }

        return result.Select(r => r!).ToList();
    }

    private static List<CarrierCharge> ReadCharges(JsonElement product)
    {
        var charges = new List<CarrierCharge>();
        var list = Child(product, "chargeItemList");
        if (list.ValueKind != JsonValueKind.Array)
        {
            return charges;
        }

        foreach (var charge in list.EnumerateArray())
        {
            var amount = Money(Child(charge, "customerPrice"));
            if (amount is null)
            {
                continue;
            }

            charges.Add(new CarrierCharge(
                Text(charge, "customerChargeCode") ?? Text(charge, "itemCode") ?? string.Empty,
                Sentence(Text(charge, "customerDescription") ?? Text(charge, "description") ?? "Charge"),
                amount.Value));
        }

        return charges;
    }

    private static CarrierBillTo? ReadBillTo(JsonElement shipment)
    {
        var entry = First(Child(shipment, "billToInfoList"));
        var account = Text(entry, "billToAccountNbr");
        var postalCode = Text(entry, "billToPostalCode");
        return string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(postalCode)
            ? null
            : new CarrierBillTo(account.Trim(), postalCode.Trim(), Text(entry, "billToCountryCode")?.Trim() is { Length: > 0 } country ? country : "US");
    }

    private static string? TrackingNumber(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (string.Equals(Text(entry, "type"), "TRACKING_ID", StringComparison.OrdinalIgnoreCase)
                && Text(entry, "value") is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>Opens a 2xx answer and hands back its <c>response</c>. <c>clientStatus.success:
    /// false</c> is a refusal whatever the status code was.</summary>
    private static JsonDocument Open(string json, out JsonElement response, out string? correlationId)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable, $"{CarrierName} returned an answer that could not be read.", inner: ex);
        }

        var root = document.RootElement;
        correlationId = Text(root, "correlationId");
        var status = Child(root, "clientStatus");
        if (Child(status, "success").ValueKind == JsonValueKind.False)
        {
            var said = CleanMessage(Text(status, "message"));
            document.Dispose();
            throw new CarrierException(
                CarrierErrorKind.Refused,
                said is null ? $"{CarrierName} refused the request." : $"{CarrierName}: {said}",
                correlationId: correlationId);
        }

        response = Child(root, "response");
        return document;
    }

    /// <summary>The account and plan a token was issued for, from its payload. A token that can't
    /// be read this way still works; the two are only shown on the settings page.</summary>
    private static (string? Account, string? Plan) ReadClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return (null, null);
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
            string? account = null;
            string? plan = null;
            foreach (var claim in claims.RootElement.EnumerateObject())
            {
                if (claim.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (claim.Name.EndsWith("/primaryNamespace", StringComparison.Ordinal))
                {
                    var value = claim.Value.GetString() ?? string.Empty;
                    account = value[(value.LastIndexOf('.') + 1)..];
                }
                else if (claim.Name.EndsWith("/apiPlan", StringComparison.Ordinal))
                {
                    plan = claim.Value.GetString();
                }
            }

            return (string.IsNullOrWhiteSpace(account) ? null : account, string.IsNullOrWhiteSpace(plan) ? null : plan);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    private static JsonElement First(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0 ? array[0] : default;

    private static string? FirstText(JsonElement array)
    {
        var first = First(array);
        return first.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(first.GetString()) ? first.GetString() : null;
    }

    private static string? Text(JsonElement parent, string name)
    {
        var value = Child(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static decimal? Number(JsonElement parent, string name) => Decimal(parent, name);

    private static decimal? Decimal(JsonElement parent, string name)
    {
        var value = Child(parent, name);
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    /// <summary>A <c>{"value": 18.99, "unit": "USD"}</c> amount.</summary>
    private static decimal? Money(JsonElement amount) => Decimal(amount, "value");

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>"23:00:00" as "11:00 PM"; anything else is passed over.</summary>
    private static string? ClockTime(string? value) =>
        TimeOnly.TryParseExact(value?.Trim(), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : null;

    /// <summary>"FUEL SURCHARGE" as "Fuel surcharge".</summary>
    private static string Sentence(string value)
    {
        var text = value.Trim();
        return text.Length == 0 || text.Any(char.IsLower)
            ? text
            : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();
    }
}
