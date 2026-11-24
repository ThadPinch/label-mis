using System.Globalization;
using System.Text.Json.Nodes;

namespace LabelsMis.Domain.Carriers.Wwex;

/// <summary>The request bodies of Worldwide Express's SpeedShip API for UPS small package, built
/// the way the calls were proven on staging (docs/carrier-labels.md). Flow names are the last part
/// of the address: <c>…/svc/shopFlow</c>.</summary>
public static class WwexRequests
{
    public const string ShopFlow = "shopFlow";
    public const string BookFlow = "integratedOrderFlow";
    public const string LabelFlow = "generateUPSReportsFromLabelFlow";
    public const string DocumentsFlow = "documentDownloadFlow";
    public const string CancelFlow = "integratedCancelFlow";
    public const string TrackFlow = "searchShipmentsFlow";

    private const string SmallPack = "SMALLPACK";

    /// <summary>A rate request for every UPS service. One handling unit per box, in the caller's order.</summary>
    public static string Shop(CarrierRateRequest request)
    {
        var references = new[] { CarrierRequestRules.Reference(request.Reference1), CarrierRequestRules.Reference(request.Reference2) }
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        var boxes = new JsonArray();
        foreach (var box in request.Boxes)
        {
            boxes.Add(Box(box, references));
        }

        var shipment = new JsonObject
        {
            ["adultSignatureRequiredFlag"] = false,
            ["destinationAddress"] = new JsonObject { ["address"] = Address(request.ShipTo) },
            ["handlingUnitList"] = boxes,
            ["insuranceRequestFlag"] = request.Boxes.Any(b => b.DeclaredValue > 0),
            ["isCarbonNeutral"] = false,
            ["isCOD"] = false,
            ["sendersReceipt"] = false,
            ["isSignatureRequired"] = false,
            ["classAlertIndicator"] = string.Empty,
            ["isSelfScheduled"] = false,
            ["originAddress"] = new JsonObject { ["stopSequence"] = null, ["address"] = Address(request.ShipFrom) },
            ["residentialDeliveryFlag"] = request.Residential,
            ["returnLabelFlag"] = false,
            ["shipmentDate"] = request.ShipDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00",
            ["shipperReleaseFlag"] = false,
            ["totalHandlingUnitCount"] = request.Boxes.Count,
            ["totalWeight"] = new JsonObject { ["value"] = request.Boxes.Sum(b => b.WeightLb), ["unit"] = "LB" },
            ["selectedServiceType"] = string.Empty,
            ["isSaturdayAvailable"] = false,
            ["skipAddressVerification"] = false
        };

        return Wrap(new JsonObject
        {
            ["productType"] = SmallPack,
            ["returnSelectedServiceOnly"] = false,
            ["service"] = UpsServices.Ground,
            ["shipment"] = shipment
        });
    }

    /// <summary>Books one offer of a rate request. The bill-to account is the one the rate named.</summary>
    public static string Book(CarrierBookRequest request) =>
        Wrap(new JsonObject
        {
            ["orderRQList"] = new JsonArray(new JsonObject
            {
                ["offerId"] = request.OfferId,
                ["productTransactionId"] = request.QuoteId,
                ["billToAccountNbr"] = request.BillTo.AccountNumber,
                ["billToPostalCode"] = request.BillTo.PostalCode,
                ["billToType"] = "SENDER",
                ["billToCountryCode"] = request.BillTo.CountryCode,
                ["sendersReceiptFlag"] = false
            })
        });

    /// <summary>One box's label as a 4 × 6 PDF, or as ZPL.</summary>
    public static string Label(string quoteId, string trackingNumber, bool zpl) =>
        Wrap(new JsonObject
        {
            ["productTransactionId"] = quoteId,
            ["labelSize"] = "UPS_4X6",
            ["includeReceipt"] = false,
            ["trackingId"] = trackingNumber,
            ["rotate180"] = false,
            ["thermalType"] = zpl ? "ZPL" : null
        });

    /// <summary>Every label of the shipment in one Letter PDF.</summary>
    public static string Documents(string quoteId) =>
        Wrap(new JsonObject
        {
            ["downloadMode"] = "SINGLE",
            ["docTypes"] = new JsonArray("UPS_LABEL_ONLY"),
            ["transactionType"] = SmallPack,
            ["referenceMap"] = new JsonObject { ["PRODUCT_TRANSACTION_ID"] = quoteId }
        });

    public static string Cancel(string quoteId) =>
        Wrap(new JsonObject
        {
            ["cancelRQList"] = new JsonArray(new JsonObject { ["productTransactionId"] = quoteId })
        });

    /// <summary>One tracking number: asking for several returns only the first one's shipment.</summary>
    public static string Track(string trackingNumber) =>
        Wrap(new JsonObject
        {
            ["trackingInfoList"] = new JsonArray(trackingNumber.Trim()),
            ["type"] = "TRACKING_ID"
        });

    private static string Wrap(JsonObject request) => new JsonObject { ["request"] = request }.ToJsonString();

    private static JsonObject Address(CarrierParty party)
    {
        var lines = new JsonArray();
        foreach (var line in new[] { party.Street1, party.Street2 })
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line.Trim());
            }
        }

        var company = Text(party.Company);
        var phone = CarrierRequestRules.Digits(party.Phone);
        return new JsonObject
        {
            ["addressLineList"] = lines,
            ["locality"] = Text(party.City),
            ["region"] = Text(party.State).ToUpperInvariant(),
            ["postalCode"] = Text(party.PostalCode),
            ["countryCode"] = string.IsNullOrWhiteSpace(party.CountryCode) ? "US" : party.CountryCode.Trim().ToUpperInvariant(),
            ["companyName"] = company,
            ["phone"] = phone,
            ["contactList"] = new JsonArray(new JsonObject
            {
                ["firstName"] = string.Empty,
                // The label prints the contact under the company; with no contact it repeats the company.
                ["lastName"] = string.IsNullOrWhiteSpace(party.Contact) ? company : party.Contact.Trim(),
                ["phone"] = phone,
                ["email"] = string.Empty,
                ["extension"] = null
            })
        };
    }

    private static JsonObject Box(CarrierBox box, IReadOnlyList<string> references)
    {
        var referenceList = new JsonArray();
        for (var i = 0; i < references.Count; i++)
        {
            referenceList.Add(new JsonObject
            {
                ["type"] = $"Reference {i + 1}",
                ["value"] = references[i],
                ["description"] = null,
                ["isPrintAsBarCode"] = false
            });
        }

        var item = new JsonObject { ["additionalHandlingFeeFlag"] = false };
        if (box.DeclaredValue > 0)
        {
            item["insuredValue"] = new JsonObject
            {
                ["unit"] = "USD",
                ["value"] = box.DeclaredValue.ToString("0.##", CultureInfo.InvariantCulture)
            };
        }

        return new JsonObject
        {
            ["billedDimension"] = new JsonObject
            {
                ["length"] = Inches(box.HasDimensions ? box.LengthIn : null),
                ["width"] = Inches(box.HasDimensions ? box.WidthIn : null),
                ["height"] = Inches(box.HasDimensions ? box.HeightIn : null),
                ["dimensionType"] = "NET"
            },
            ["packagingType"] = "02",
            ["packagingTypeName"] = "Custom",
            ["quantity"] = 1,
            ["referenceList"] = referenceList,
            ["shippedItemList"] = new JsonArray(item),
            ["weight"] = new JsonObject { ["unit"] = "LB", ["value"] = box.WeightLb },
            ["handlingGroup"] = string.Empty,
            ["totalWeight"] = new JsonObject(),
            ["saveToMyPackages"] = false,
            ["isLargePackage"] = false,
            ["additionalHandlingFeeFlag"] = false
        };
    }

    private static JsonObject Inches(decimal? value) =>
        new() { ["value"] = value is { } inches ? JsonValue.Create(inches) : null, ["unit"] = "in" };

    private static string Text(string? value) => (value ?? string.Empty).Trim();
}
