using System.Text.Json;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Carriers.Wwex;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Carriers;

public class WwexRequestsTests
{
    private static readonly CarrierParty Shop = new(
        "Hey Lucky Label Co.", "Shipping Desk", "(602) 555-0100", "100 W Main St", "Suite 4", "Phoenix", "az", "85001");

    private static readonly CarrierParty Customer = new(
        "Desert Bloom Coffee", null, "480.555.0142", "2200 E Camelback Rd", null, "Phoenix", "AZ", "85016");

    private static CarrierRateRequest Request(params CarrierBox[] boxes) =>
        new(Shop, Customer, Residential: false, new DateOnly(2026, 11, 24), boxes, "SO-2026-00046", "PO 7781");

    private static JsonElement Shipment(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("request").GetProperty("shipment");

    [Fact]
    public void Shop_AsksForEveryUpsServiceForSmallPackage()
    {
        var request = JsonDocument.Parse(WwexRequests.Shop(Request(new CarrierBox(5m, 12m, 10m, 8m, 0m)))).RootElement.GetProperty("request");

        request.GetProperty("productType").GetString().Should().Be("SMALLPACK");
        request.GetProperty("returnSelectedServiceOnly").GetBoolean().Should().BeFalse();
        request.GetProperty("shipment").GetProperty("shipmentDate").GetString().Should().Be("2026-11-24 00:00:00");
        request.GetProperty("shipment").GetProperty("returnLabelFlag").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Shop_WritesBothAddressesWithDigitsOnlyPhones()
    {
        var shipment = Shipment(WwexRequests.Shop(Request(new CarrierBox(5m, 12m, 10m, 8m, 0m))));

        var origin = shipment.GetProperty("originAddress").GetProperty("address");
        origin.GetProperty("companyName").GetString().Should().Be("Hey Lucky Label Co.");
        origin.GetProperty("addressLineList").EnumerateArray().Select(l => l.GetString()).Should().Equal("100 W Main St", "Suite 4");
        origin.GetProperty("region").GetString().Should().Be("AZ");
        origin.GetProperty("phone").GetString().Should().Be("6025550100");
        origin.GetProperty("contactList")[0].GetProperty("lastName").GetString().Should().Be("Shipping Desk");

        var destination = shipment.GetProperty("destinationAddress").GetProperty("address");
        destination.GetProperty("addressLineList").GetArrayLength().Should().Be(1);
        destination.GetProperty("phone").GetString().Should().Be("4805550142");
        destination.GetProperty("postalCode").GetString().Should().Be("85016");
        destination.GetProperty("countryCode").GetString().Should().Be("US");
    }

    [Fact]
    public void Shop_WithNoContact_PrintsTheCompanyInItsPlace()
    {
        var destination = Shipment(WwexRequests.Shop(Request(new CarrierBox(5m, 0m, 0m, 0m, 0m))))
            .GetProperty("destinationAddress").GetProperty("address");

        destination.GetProperty("contactList")[0].GetProperty("lastName").GetString().Should().Be("Desert Bloom Coffee");
    }

    [Fact]
    public void Shop_SendsOneHandlingUnitPerBoxWithTheReferencesOnEach()
    {
        var shipment = Shipment(WwexRequests.Shop(Request(new CarrierBox(12m, 14m, 12m, 10m, 0m), new CarrierBox(7.5m, 12m, 10m, 6m, 0m))));

        shipment.GetProperty("totalHandlingUnitCount").GetInt32().Should().Be(2);
        shipment.GetProperty("totalWeight").GetProperty("value").GetDecimal().Should().Be(19.5m);
        var units = shipment.GetProperty("handlingUnitList");
        units.GetArrayLength().Should().Be(2);
        units[1].GetProperty("weight").GetProperty("value").GetDecimal().Should().Be(7.5m);
        units[1].GetProperty("billedDimension").GetProperty("height").GetProperty("value").GetDecimal().Should().Be(6m);
        units[0].GetProperty("referenceList").EnumerateArray()
            .Select(r => (r.GetProperty("type").GetString(), r.GetProperty("value").GetString()))
            .Should().Equal(("Reference 1", "SO-2026-00046"), ("Reference 2", "PO 7781"));
    }

    [Fact]
    public void Shop_ABoxThatWasNotMeasured_SendsNoDimensions()
    {
        var unit = Shipment(WwexRequests.Shop(Request(new CarrierBox(5m, 12m, 0m, 8m, 0m)))).GetProperty("handlingUnitList")[0];

        foreach (var side in new[] { "length", "width", "height" })
        {
            unit.GetProperty("billedDimension").GetProperty(side).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public void Shop_ADeclaredValue_AsksForInsuranceOnThatBox()
    {
        var shipment = Shipment(WwexRequests.Shop(Request(new CarrierBox(5m, 12m, 10m, 8m, 500m), new CarrierBox(5m, 12m, 10m, 8m, 0m))));

        shipment.GetProperty("insuranceRequestFlag").GetBoolean().Should().BeTrue();
        var units = shipment.GetProperty("handlingUnitList");
        units[0].GetProperty("shippedItemList")[0].GetProperty("insuredValue").GetProperty("value").GetString().Should().Be("500");
        units[1].GetProperty("shippedItemList")[0].TryGetProperty("insuredValue", out _).Should().BeFalse();
    }

    [Fact]
    public void Shop_WithNoDeclaredValueAndNoReferences_SendsNeither()
    {
        var request = new CarrierRateRequest(Shop, Customer, true, new DateOnly(2026, 12, 1), [new CarrierBox(1m, 0m, 0m, 0m, 0m)], " ", null);

        var shipment = Shipment(WwexRequests.Shop(request));

        shipment.GetProperty("insuranceRequestFlag").GetBoolean().Should().BeFalse();
        shipment.GetProperty("residentialDeliveryFlag").GetBoolean().Should().BeTrue();
        shipment.GetProperty("handlingUnitList")[0].GetProperty("referenceList").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Book_NamesTheOfferTheQuoteAndTheAccountToBill()
    {
        var row = JsonDocument.Parse(WwexRequests.Book(new CarrierBookRequest("quote-1", "offer-9", new CarrierBillTo("V01714", "75226"))))
            .RootElement.GetProperty("request").GetProperty("orderRQList")[0];

        row.GetProperty("offerId").GetString().Should().Be("offer-9");
        row.GetProperty("productTransactionId").GetString().Should().Be("quote-1");
        row.GetProperty("billToAccountNbr").GetString().Should().Be("V01714");
        row.GetProperty("billToPostalCode").GetString().Should().Be("75226");
        row.GetProperty("billToType").GetString().Should().Be("SENDER");
        row.GetProperty("billToCountryCode").GetString().Should().Be("US");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Label_AsksForA4x6LabelAsPdfOrZpl(bool zpl)
    {
        var request = JsonDocument.Parse(WwexRequests.Label("quote-1", "1ZV017146460637994", zpl)).RootElement.GetProperty("request");

        request.GetProperty("labelSize").GetString().Should().Be("UPS_4X6");
        request.GetProperty("trackingId").GetString().Should().Be("1ZV017146460637994");
        request.GetProperty("thermalType").ValueKind.Should().Be(zpl ? JsonValueKind.String : JsonValueKind.Null);
    }

    [Fact]
    public void Documents_Cancel_AndTrack_GoByTheQuoteOrTheTrackingNumber()
    {
        JsonDocument.Parse(WwexRequests.Documents("quote-1")).RootElement.GetProperty("request")
            .GetProperty("referenceMap").GetProperty("PRODUCT_TRANSACTION_ID").GetString().Should().Be("quote-1");
        JsonDocument.Parse(WwexRequests.Cancel("quote-1")).RootElement.GetProperty("request")
            .GetProperty("cancelRQList")[0].GetProperty("productTransactionId").GetString().Should().Be("quote-1");
        var track = JsonDocument.Parse(WwexRequests.Track(" 1ZV017146460637994 ")).RootElement.GetProperty("request");
        track.GetProperty("trackingInfoList").EnumerateArray().Select(t => t.GetString()).Should().Equal("1ZV017146460637994");
        track.GetProperty("type").GetString().Should().Be("TRACKING_ID");
    }
}

public class CarrierRulesTests
{
    private static readonly CarrierParty Complete = new("Co", "Pat", "6025550100", "1 Main St", null, "Phoenix", "AZ", "85001");

    private static CarrierRateRequest Request(CarrierParty? from = null, CarrierParty? to = null, params CarrierBox[] boxes) =>
        new(from ?? Complete, to ?? Complete, false, new DateOnly(2026, 11, 24), boxes.Length == 0 ? [new CarrierBox(5m, 0m, 0m, 0m, 0m)] : boxes, null, null);

    [Fact]
    public void Problems_ACompleteRequest_HasNone() =>
        CarrierRequestRules.Problems(Request()).Should().BeEmpty();

    [Fact]
    public void Problems_AnIncompleteShipTo_NamesEveryMissingPart()
    {
        var problems = CarrierRequestRules.Problems(Request(to: new CarrierParty(null, null, null, " ", null, "Phoenix", null, "85001")));

        problems.Should().ContainSingle().Which.Should().Be("Ship-to needs a company or recipient name, a street address, a state.");
    }

    [Fact]
    public void Problems_AnIncompleteShipFrom_SaysWhereToSetIt()
    {
        var problems = CarrierRequestRules.Problems(Request(from: Complete with { PostalCode = null }));

        problems.Should().ContainSingle().Which.Should().Be(
            "Ship-from needs a ZIP code. Set it in Settings → General and Setup → Shipping carrier.");
    }

    [Theory]
    [InlineData(0, "Package 1 needs a weight.")]
    [InlineData(151, "Package 1 weighs 151 lb; UPS takes at most 150 lb a package.")]
    public void Problems_ABadWeight_IsNamedByPackage(int weight, string expected) =>
        CarrierRequestRules.Problems(Request(boxes: new CarrierBox(weight, 0m, 0m, 0m, 0m))).Should().Equal(expected);

    [Fact]
    public void Problems_ExactlyTheLimit_IsAccepted() =>
        CarrierRequestRules.Problems(Request(boxes: new CarrierBox(150m, 0m, 0m, 0m, 0m))).Should().BeEmpty();

    [Fact]
    public void Problems_NoBoxes_AsksForOne()
    {
        var request = new CarrierRateRequest(Complete, Complete, false, new DateOnly(2026, 11, 24), [], null, null);

        CarrierRequestRules.Problems(request).Should().Equal("Add at least one package.");
    }

    [Fact]
    public void LabelProblems_AsksForPhoneNumbersARateDoesNotNeed()
    {
        var request = Request(from: Complete with { Phone = null }, to: Complete with { Phone = "555-0100" });

        CarrierRequestRules.Problems(request).Should().BeEmpty();
        CarrierRequestRules.LabelProblems(request).Should().Equal(
            "Ship-to needs a 10-digit phone number for the label.",
            "Ship-from needs a 10-digit phone number for the label. Set it in Setup → Shipping carrier.");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" SO-2026-00046 ", "SO-2026-00046")]
    [InlineData("0123456789012345678901234567890123456789", "01234567890123456789012345678901234")]
    public void Reference_IsTrimmedAndCutAt35(string? value, string? expected) =>
        CarrierRequestRules.Reference(value).Should().Be(expected);

    [Theory]
    [InlineData("CREATED", CarrierTrackingStatus.LabelCreated)]
    [InlineData("PENDING_PICKUP", CarrierTrackingStatus.PendingPickup)]
    [InlineData("IN_TRANSIT", CarrierTrackingStatus.InTransit)]
    [InlineData("in transit", CarrierTrackingStatus.InTransit)]
    [InlineData("OUT_FOR_DELIVERY", CarrierTrackingStatus.OutForDelivery)]
    [InlineData("DELIVERED", CarrierTrackingStatus.Delivered)]
    [InlineData("EXCEPTION", CarrierTrackingStatus.Exception)]
    [InlineData("VOID", CarrierTrackingStatus.Voided)]
    [InlineData("SHIPMENT_DELIVERED", CarrierTrackingStatus.Delivered)]
    [InlineData("SHIPMENT_OUT_FOR_DELIVERY", CarrierTrackingStatus.OutForDelivery)]
    [InlineData("SOMETHING_NEW", CarrierTrackingStatus.Unknown)]
    [InlineData(null, CarrierTrackingStatus.Unknown)]
    public void TrackingCodes_Parse_KnowsWwexsWordsAndGuessesAtNewOnes(string? code, CarrierTrackingStatus expected) =>
        CarrierTrackingCodes.Parse(code).Should().Be(expected);

    [Fact]
    public void TrackingCodes_Describe_ShowsAnUnknownWordAsItCame() =>
        CarrierTrackingCodes.Describe("SOMETHING_NEW").Should().Be("SOMETHING_NEW");

    [Theory]
    [InlineData("GND", "UPS Ground®", "UPS Ground")]
    [InlineData("gnd", null, "UPS Ground")]
    [InlineData("2DM", "", "UPS 2nd Day Air A.M.")]
    [InlineData("XYZ", null, "XYZ")]
    public void UpsServices_Name_PrefersTheCarriersNameWithoutTheMarks(string code, string? carrierName, string expected) =>
        UpsServices.Name(code, carrierName).Should().Be(expected);

    [Theory]
    [InlineData("1ZV017146460637994", true)]
    [InlineData(" 1zv017146460637994 ", true)]
    [InlineData("748912345678", false)]
    [InlineData("1ZV01714646063799", false)]
    [InlineData(null, false)]
    public void UpsServices_LooksLikeTrackingNumber(string? value, bool expected) =>
        UpsServices.LooksLikeTrackingNumber(value).Should().Be(expected);

    [Fact]
    public void UpsServices_TrackingUrl_PointsAtUps() =>
        UpsServices.TrackingUrl("1ZV017146460637994").Should().Be("https://www.ups.com/track?tracknum=1ZV017146460637994");

    [Fact]
    public void CarrierNames_PutUpsFirstAndWriteEachCarrierProperly()
    {
        CarrierNames.InPickOrder.Should().StartWith(Carrier.Ups).And.BeEquivalentTo(Enum.GetValues<Carrier>());
        CarrierNames.InPickOrder.Select(CarrierNames.For).Should().Equal("UPS", "FedEx", "USPS", "DHL", "Other");
    }
}
