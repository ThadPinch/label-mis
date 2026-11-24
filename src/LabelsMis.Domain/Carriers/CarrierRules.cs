using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Carriers;

/// <summary>The carrier's tracking status words, as Worldwide Express lists them: CREATED,
/// PENDING_PICKUP, IN_TRANSIT, OUT_FOR_DELIVERY, DELIVERED, EXCEPTION, VOID. Only CREATED and VOID
/// can be seen on staging; the rest are read from WWEX's documentation, so a word that is not on the
/// list is matched loosely rather than dropped.</summary>
public static class CarrierTrackingCodes
{
    public static CarrierTrackingStatus Parse(string? code)
    {
        var word = (code ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
        return word switch
        {
            "" => CarrierTrackingStatus.Unknown,
            "CREATED" or "ORDERED" or "BOOKED" => CarrierTrackingStatus.LabelCreated,
            "PENDING_PICKUP" or "PICKUP_SCHEDULED" => CarrierTrackingStatus.PendingPickup,
            "IN_TRANSIT" => CarrierTrackingStatus.InTransit,
            "OUT_FOR_DELIVERY" => CarrierTrackingStatus.OutForDelivery,
            "DELIVERED" => CarrierTrackingStatus.Delivered,
            "EXCEPTION" => CarrierTrackingStatus.Exception,
            "VOID" or "VOIDED" or "CANCELLED" or "CANCELED" => CarrierTrackingStatus.Voided,
            _ when word.Contains("OUT_FOR_DELIVERY") => CarrierTrackingStatus.OutForDelivery,
            _ when word.Contains("DELIVERED") => CarrierTrackingStatus.Delivered,
            _ when word.Contains("EXCEPTION") => CarrierTrackingStatus.Exception,
            _ when word.Contains("TRANSIT") => CarrierTrackingStatus.InTransit,
            _ when word.Contains("PICKUP") => CarrierTrackingStatus.PendingPickup,
            _ when word.Contains("VOID") => CarrierTrackingStatus.Voided,
            _ => CarrierTrackingStatus.Unknown
        };
    }

    /// <summary>What the shipment page prints for an event.</summary>
    public static string Describe(string? code) => Parse(code) switch
    {
        CarrierTrackingStatus.LabelCreated => "Label created",
        CarrierTrackingStatus.PendingPickup => "Waiting for pickup",
        CarrierTrackingStatus.InTransit => "In transit",
        CarrierTrackingStatus.OutForDelivery => "Out for delivery",
        CarrierTrackingStatus.Delivered => "Delivered",
        CarrierTrackingStatus.Exception => "Delivery exception",
        CarrierTrackingStatus.Voided => "Voided at the carrier",
        _ => string.IsNullOrWhiteSpace(code) ? "Unknown" : code.Trim()
    };
}

/// <summary>The UPS services Worldwide Express sells for domestic small package, in the order the
/// shop reads them: slowest and cheapest first.</summary>
public static class UpsServices
{
    public const string Ground = "GND";

    public static readonly IReadOnlyList<(string Code, string Name)> Domestic =
    [
        ("GND", "UPS Ground"),
        ("3DS", "UPS 3 Day Select"),
        ("2DA", "UPS 2nd Day Air"),
        ("2DM", "UPS 2nd Day Air A.M."),
        ("1DP", "UPS Next Day Air Saver"),
        ("1DA", "UPS Next Day Air"),
        ("1DM", "UPS Next Day Air Early")
    ];

    public static bool IsKnown(string? code) =>
        Domestic.Any(s => string.Equals(s.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The carrier's own name for the service when it sent one (without the ® marks),
    /// else the name on the list, else the code.</summary>
    public static string Name(string? code, string? carrierName = null)
    {
        var cleaned = (carrierName ?? string.Empty).Replace("®", string.Empty).Replace("℠", string.Empty).Trim();
        if (cleaned.Length > 0)
        {
            return cleaned;
        }

        var known = Domestic.FirstOrDefault(s => string.Equals(s.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        return known.Name ?? (code ?? string.Empty).Trim();
    }

    /// <summary>UPS's public tracking page for a number.</summary>
    public static string TrackingUrl(string trackingNumber) =>
        "https://www.ups.com/track?tracknum=" + Uri.EscapeDataString(trackingNumber.Trim());

    /// <summary>A UPS tracking number: 1Z, the six-character shipper number, then ten more.</summary>
    public static bool LooksLikeTrackingNumber(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToUpperInvariant();
        return text.Length == 18 && text.StartsWith("1Z", StringComparison.Ordinal) && text.All(char.IsLetterOrDigit);
    }
}

/// <summary>What has to be true of a shipment before the carrier is asked about it. Worldwide
/// Express refuses a request without these in words the shipping desk can't act on ("AddressV3.Company
/// Name: ERR_EMPTY"), so LabelShop says it first.</summary>
public static class CarrierRequestRules
{
    public const decimal MaxBoxWeightLb = 150m;
    public const int MaxBoxes = 50;
    public const int MaxReferenceLength = 35;

    public static IReadOnlyList<string> Problems(CarrierRateRequest request)
    {
        var problems = new List<string>();
        AddPartyProblems(problems, request.ShipFrom, "Ship-from", "Set it in Settings → General and Setup → Shipping carrier.");
        AddPartyProblems(problems, request.ShipTo, "Ship-to", null);

        if (request.Boxes.Count == 0)
        {
            problems.Add("Add at least one package.");
        }
        else if (request.Boxes.Count > MaxBoxes)
        {
            problems.Add($"A shipment can have at most {MaxBoxes} packages.");
        }

        for (var i = 0; i < request.Boxes.Count; i++)
        {
            var box = request.Boxes[i];
            if (box.WeightLb <= 0)
            {
                problems.Add($"Package {i + 1} needs a weight.");
            }
            else if (box.WeightLb > MaxBoxWeightLb)
            {
                problems.Add($"Package {i + 1} weighs {box.WeightLb:0.##} lb; UPS takes at most {MaxBoxWeightLb:0} lb a package.");
            }

            if (box.LengthIn < 0 || box.WidthIn < 0 || box.HeightIn < 0 || box.DeclaredValue < 0)
            {
                problems.Add($"Package {i + 1} has a negative size or value.");
            }
        }

        return problems;
    }

    /// <summary>The extra a label needs over a rate: someone to call at the destination.</summary>
    public static IReadOnlyList<string> LabelProblems(CarrierRateRequest request)
    {
        var problems = new List<string>(Problems(request));
        if (Digits(request.ShipTo.Phone).Length < 10)
        {
            problems.Add("Ship-to needs a 10-digit phone number for the label.");
        }

        if (Digits(request.ShipFrom.Phone).Length < 10)
        {
            problems.Add("Ship-from needs a 10-digit phone number for the label. Set it in Setup → Shipping carrier.");
        }

        return problems;
    }

    /// <summary>References print on the label and are cut by the carrier at 35 characters.</summary>
    public static string? Reference(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text.Length > MaxReferenceLength ? text[..MaxReferenceLength] : text;
    }

    public static string Digits(string? value) =>
        new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static void AddPartyProblems(List<string> problems, CarrierParty party, string label, string? hint)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(party.Company)) missing.Add("a company or recipient name");
        if (string.IsNullOrWhiteSpace(party.Street1)) missing.Add("a street address");
        if (string.IsNullOrWhiteSpace(party.City)) missing.Add("a city");
        if (string.IsNullOrWhiteSpace(party.State)) missing.Add("a state");
        if (string.IsNullOrWhiteSpace(party.PostalCode)) missing.Add("a ZIP code");
        if (missing.Count > 0)
        {
            problems.Add($"{label} needs {string.Join(", ", missing)}.{(hint is null ? string.Empty : " " + hint)}");
        }
    }
}

/// <summary>How a carrier is written on screens and documents. UPS leads the list: it is the
/// carrier the shop ships with.</summary>
public static class CarrierNames
{
    public static readonly IReadOnlyList<Carrier> InPickOrder =
        [Carrier.Ups, Carrier.Fedex, Carrier.Usps, Carrier.Dhl, Carrier.Other];

    public static string For(Carrier carrier) => carrier switch
    {
        Carrier.Ups => "UPS",
        Carrier.Fedex => "FedEx",
        Carrier.Usps => "USPS",
        Carrier.Dhl => "DHL",
        _ => "Other"
    };
}
