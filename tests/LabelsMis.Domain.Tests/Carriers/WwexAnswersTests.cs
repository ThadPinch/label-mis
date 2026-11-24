using System.Runtime.CompilerServices;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Carriers.Wwex;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Carriers;

/// <summary>Answers saved from Worldwide Express's staging environment on 2026-11-24, reduced for
/// git (null fields dropped, client id and contact details swapped, label files replaced with a few
/// bytes). Read from the source tree so the test project needs no copy step.</summary>
internal static class Staging
{
    public static string Read(string name, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name));

    public static readonly CarrierBox OneBox = new(5m, 12m, 10m, 8m, 0m);
    public static readonly IReadOnlyList<CarrierBox> TwoBoxes = [new(12m, 14m, 12m, 10m, 0m), new(7.5m, 12m, 10m, 6m, 0m)];
}

public class WwexAnswersRateTests
{
    [Fact]
    public void ParseRates_OneBox_ReturnsEveryUpsServiceCheapestFirst()
    {
        var quote = WwexAnswers.ParseRates(Staging.Read("shop-one-box.json"), [Staging.OneBox]);

        quote.Rates.Select(r => r.ServiceCode).Should().Equal("GND", "3DS", "2DA", "2DM", "1DP", "1DA", "1DM");
        quote.Rates.Select(r => r.TotalCost).Should().BeInAscendingOrder();
        quote.QuoteId.Should().Be("2b1e9bbe-a2ce-4c0c-80b9-8e74f76d62c8");
        quote.CorrelationId.Should().StartWith("NG-");
    }

    [Fact]
    public void ParseRates_OneBox_ReadsGroundWithItsChargesAndTransit()
    {
        var ground = WwexAnswers.ParseRates(Staging.Read("shop-one-box.json"), [Staging.OneBox])
            .Rates.Single(r => r.ServiceCode == "GND");

        ground.Should().BeEquivalentTo(new
        {
            ServiceName = "UPS Ground",
            TotalCost = 18.99m,
            TransitDays = 1,
            DeliveryDate = new DateOnly(2026, 11, 25),
            DeliveryBy = "11:00 PM",
            BilledWeightLb = 7m
        });
        ground.Boxes.Should().ContainSingle().Which.Cost.Should().Be(18.99m);
        ground.Boxes[0].Charges.Select(c => (c.Code, c.Description, c.Amount)).Should().BeEquivalentTo(new[]
        {
            ("FSC", "Fuel surcharge", 3.95m),
            ("FRT", "Small package freight", 15.04m)
        });
    }

    [Fact]
    public void ParseRates_ServiceNames_DropTheTrademarkSigns()
    {
        var quote = WwexAnswers.ParseRates(Staging.Read("shop-one-box.json"), [Staging.OneBox]);

        quote.Rates.Single(r => r.ServiceCode == "1DA").ServiceName.Should().Be("UPS Next Day Air");
        quote.Rates.Select(r => r.ServiceName).Should().NotContain(n => n.Contains('®'));
    }

    [Fact]
    public void ParseRates_NamesTheAccountTheBookingBills()
    {
        var quote = WwexAnswers.ParseRates(Staging.Read("shop-one-box.json"), [Staging.OneBox]);

        quote.BillTo.Should().Be(new CarrierBillTo("V01714", "75226", "US"));
    }

    [Fact]
    public void ParseRates_TwoBoxes_PricesEachBoxInTheOrderAsked()
    {
        var ground = WwexAnswers.ParseRates(Staging.Read("shop-two-boxes.json"), Staging.TwoBoxes)
            .Rates.Single(r => r.ServiceCode == "GND");

        ground.TotalCost.Should().Be(66.88m);
        ground.Boxes.Should().HaveCount(2);
        ground.Boxes.Sum(b => b.Cost).Should().Be(ground.TotalCost);
        ground.Boxes.Select(b => (b.BoxKey, b.Cost)).Should().Equal(
            ("748a6938-c6c1-458f-8cbd-69af6f81dc2a", 35.30m),
            ("82761ba7-57f1-48a6-bfeb-f97e297be9f4", 31.58m));
        ground.Boxes[0].Charges.Select(c => c.Code).Should().BeEquivalentTo("FSC", "RDC", "FRT");
    }

    [Fact]
    public void ParseRates_TwoBoxesAskedInTheOtherOrder_StillMatchesEachBoxByWeightAndSize()
    {
        var asked = WwexAnswers.ParseRates(Staging.Read("shop-two-boxes.json"), Staging.TwoBoxes)
            .Rates.Single(r => r.ServiceCode == "GND");
        var reversed = WwexAnswers.ParseRates(Staging.Read("shop-two-boxes.json"), Staging.TwoBoxes.Reverse().ToList())
            .Rates.Single(r => r.ServiceCode == "GND");

        reversed.Boxes.Select(b => b.BoxKey).Should().Equal(asked.Boxes.Select(b => b.BoxKey).Reverse());
    }

    [Fact]
    public void ParseRates_WhenTheBoxCountDiffers_IsUnreadable()
    {
        var act = () => WwexAnswers.ParseRates(Staging.Read("shop-two-boxes.json"), [Staging.OneBox]);

        act.Should().Throw<CarrierException>()
            .Where(e => e.Kind == CarrierErrorKind.Unreadable)
            .WithMessage("*priced 2 boxes for a shipment of 1*");
    }

    [Fact]
    public void ParseRates_WithNoOffers_IsARefusal()
    {
        var act = () => WwexAnswers.ParseRates(
            """{"clientStatus":{"success":true,"message":""},"correlationId":"NG-1","response":{"offerList":[]}}""", [Staging.OneBox]);

        act.Should().Throw<CarrierException>()
            .Where(e => e.Kind == CarrierErrorKind.Refused && e.CorrelationId == "NG-1")
            .WithMessage("*no UPS services*");
    }

    [Fact]
    public void ParseRates_WhenTheAnswerSaysSuccessFalse_IsARefusalWithTheCarriersWords()
    {
        var act = () => WwexAnswers.ParseRates(
            """{"clientStatus":{"success":false,"message":"Origin postal code is not serviced; exception: AppException"},"response":{}}""",
            [Staging.OneBox]);

        act.Should().Throw<CarrierException>()
            .Where(e => e.Kind == CarrierErrorKind.Refused)
            .WithMessage("Worldwide Express: Origin postal code is not serviced");
    }
}

public class WwexAnswersBookingTests
{
    [Fact]
    public void ParseBooking_OneBox_ReadsTheOrderAndItsTrackingNumber()
    {
        var booking = WwexAnswers.ParseBooking(Staging.Read("book-one-box.json"));

        booking.OrderNumber.Should().Be("NUD34772465");
        booking.Boxes.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            TrackingNumber = "1ZV017146460637994",
            CarrierPackageId = "64b94111-3608-4605-b4c7-363e52209a4a"
        });
        var ground = WwexAnswers.ParseRates(Staging.Read("shop-one-box.json"), [Staging.OneBox]).Rates.Single(r => r.ServiceCode == "GND");
        booking.Boxes[0].BoxKey.Should().Be(ground.Boxes[0].BoxKey, "the booking names the box the rate priced");
    }

    [Fact]
    public void ParseBooking_TwoBoxes_TiesEachTrackingNumberToTheBoxTheRatePriced()
    {
        var ground = WwexAnswers.ParseRates(Staging.Read("shop-two-boxes.json"), Staging.TwoBoxes)
            .Rates.Single(r => r.ServiceCode == "GND");

        var booking = WwexAnswers.ParseBooking(Staging.Read("book-two-boxes.json"));

        booking.Boxes.Should().HaveCount(2);
        booking.Boxes.Single(b => b.BoxKey == ground.Boxes[0].BoxKey).TrackingNumber.Should().Be("1ZV017148167928755");
        booking.Boxes.Single(b => b.BoxKey == ground.Boxes[1].BoxKey).TrackingNumber.Should().Be("1ZV017148167928756");
    }

    [Fact]
    public void ParseBooking_A200WithoutAnOrder_IsAlreadyBooked()
    {
        var act = () => WwexAnswers.ParseBooking(Staging.Read("book-already-booked.json"));

        act.Should().Throw<CarrierException>()
            .Where(e => e.Kind == CarrierErrorKind.AlreadyBooked && !e.OutcomeUnknown);
    }

    [Fact]
    public void ParseBooking_AnOrderWithoutATrackingNumber_LeavesTheOutcomeUnknown()
    {
        var act = () => WwexAnswers.ParseBooking(
            """{"clientStatus":{"success":true},"response":{"shipmentOrderResponse":{"order":{"orderId":"NUD1","orderedItemList":[{"orderedItemId":"a","handlingUnitIdList":["b"],"secondaryTxnIdList":[]}]}}}}""");

        act.Should().Throw<CarrierException>().Where(e => e.Kind == CarrierErrorKind.Unreadable && e.OutcomeUnknown);
    }

    [Fact]
    public void ParseBooking_AnEmptyAnswer_LeavesTheOutcomeUnknown()
    {
        var act = () => WwexAnswers.ParseBooking("""{"clientStatus":{"success":true},"response":{}}""");

        act.Should().Throw<CarrierException>().Where(e => e.OutcomeUnknown);
    }
}

public class WwexAnswersLabelVoidTrackTests
{
    [Theory]
    [InlineData("label-pdf.json", CarrierLabelFormat.Pdf4x6, "application/pdf", "pdf", "%PDF")]
    [InlineData("label-zpl.json", CarrierLabelFormat.Zpl, "text/plain", "zpl", "^XA")]
    [InlineData("label-letter.json", CarrierLabelFormat.PdfLetter, "application/pdf", "pdf", "%PDF")]
    public void ParseLabel_DecodesTheFile(string fixture, CarrierLabelFormat format, string contentType, string extension, string startsWith)
    {
        var label = WwexAnswers.ParseLabel(Staging.Read(fixture), format);

        label.ContentType.Should().Be(contentType);
        label.FileExtension.Should().Be(extension);
        System.Text.Encoding.ASCII.GetString(label.Content).Should().StartWith(startsWith);
    }

    [Fact]
    public void ParseLabel_AFileOfTheWrongKind_IsUnreadable()
    {
        var act = () => WwexAnswers.ParseLabel(Staging.Read("label-zpl.json"), CarrierLabelFormat.Pdf4x6);

        act.Should().Throw<CarrierException>().Where(e => e.Kind == CarrierErrorKind.Unreadable).WithMessage("*not a PDF label*");
    }

    [Fact]
    public void ParseLabel_WithNoFile_IsUnreadable()
    {
        var act = () => WwexAnswers.ParseLabel("""{"clientStatus":{"success":true},"response":{"reportList":[]}}""", CarrierLabelFormat.Pdf4x6);

        act.Should().Throw<CarrierException>().Where(e => e.Kind == CarrierErrorKind.Unreadable).WithMessage("*no label*");
    }

    [Fact]
    public void ParseVoid_TheFirstVoid_IsVoided() =>
        WwexAnswers.ParseVoid(Staging.Read("void-voided.json")).Should().Be(CarrierVoidOutcome.Voided);

    [Fact]
    public void ParseVoid_ARepeatVoid_IsAlreadyVoided() =>
        WwexAnswers.ParseVoid(Staging.Read("void-already-voided.json")).Should().Be(CarrierVoidOutcome.AlreadyVoided);

    [Fact]
    public void ParseVoid_AnAnswerThatConfirmsNothing_LeavesTheOutcomeUnknown()
    {
        var act = () => WwexAnswers.ParseVoid("""{"clientStatus":{"success":true},"response":{"message":"queued"}}""");

        act.Should().Throw<CarrierException>().Where(e => e.OutcomeUnknown).WithMessage("*did not confirm the void*queued*");
    }

    [Fact]
    public void ParseTracking_ANewLabel_IsCreatedWithOneEvent()
    {
        var tracking = WwexAnswers.ParseTracking(Staging.Read("track-created.json"));

        tracking.Found.Should().BeTrue();
        var box = tracking.Boxes.Should().ContainSingle().Subject;
        box.CarrierPackageId.Should().Be("64b94111-3608-4605-b4c7-363e52209a4a");
        box.Status.Should().Be(CarrierTrackingStatus.LabelCreated);
        box.Events.Should().ContainSingle().Which.Should().Be(
            new CarrierTrackingEvent("CREATED", new DateTime(2026, 11, 24, 16, 43, 58, DateTimeKind.Utc)));
    }

    [Fact]
    public void ParseTracking_AVoidedLabel_ListsItsEventsOldestFirst()
    {
        var box = WwexAnswers.ParseTracking(Staging.Read("track-voided.json")).Boxes.Single();

        box.Status.Should().Be(CarrierTrackingStatus.Voided);
        box.Events.Select(e => e.Code).Should().Equal("CREATED", "VOID");
        box.Events.Select(e => e.OccurredAtUtc).Should().BeInAscendingOrder();
    }

    [Fact]
    public void ParseTracking_OneNumberOfATwoBoxShipment_ReturnsBothBoxes()
    {
        var tracking = WwexAnswers.ParseTracking(Staging.Read("track-two-boxes.json"));

        tracking.Boxes.Select(b => b.CarrierPackageId).Should().Equal(
            "d9c44b70-e67e-4124-bce0-e14c8b39e909", "8a6eea9c-7df1-463a-b406-fb55649b7d11");
    }

    [Fact]
    public void ParseTracking_AnUnknownNumber_IsNotFound()
    {
        var tracking = WwexAnswers.ParseTracking(Staging.Read("track-not-found.json"));

        tracking.Found.Should().BeFalse();
        tracking.Boxes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-11-24 10:43:58", "-0600", 16, 43)]
    [InlineData("2026-11-24 10:43:58", "-07:00", 17, 43)]
    [InlineData("2026-11-24 23:30:00", "+0530", 18, 0)]
    [InlineData("2026-11-24 10:43:58", null, 10, 43)]
    public void EventTime_AppliesTheOffset(string local, string? offset, int hourUtc, int minute)
    {
        var at = WwexAnswers.EventTime(local, offset);

        at.Should().NotBeNull();
        at!.Value.Kind.Should().Be(DateTimeKind.Utc);
        (at.Value.Hour, at.Value.Minute).Should().Be((hourUtc, minute));
    }

    [Fact]
    public void EventTime_ATimeThatCannotBeRead_IsNull() =>
        WwexAnswers.EventTime("yesterday", "-0600").Should().BeNull();
}

public class WwexAnswersFailureTests
{
    [Fact]
    public void Failure_A422_IsARefusalInTheCarriersWords()
    {
        var failure = WwexAnswers.Failure(422, Staging.Read("shop-refused-422.json"));

        failure.Kind.Should().Be(CarrierErrorKind.Refused);
        failure.StatusCode.Should().Be(422);
        failure.Message.Should().Be("Worldwide Express: Weight of the package is more than 150");
        failure.OutcomeUnknown.Should().BeFalse();
    }

    [Fact]
    public void Failure_A500_IsAServerProblemWithTheOutcomeUnknown()
    {
        var failure = WwexAnswers.Failure(500, Staging.Read("shop-failed-500.json"));

        failure.Kind.Should().Be(CarrierErrorKind.Server);
        failure.Message.Should().Be("Worldwide Express could not complete the request: action did not complete normally: getProductCatalog");
        failure.OutcomeUnknown.Should().BeTrue();
    }

    [Fact]
    public void Failure_A500WithNoDescription_SaysOnlyThatItFailed()
    {
        var failure = WwexAnswers.Failure(500, Staging.Read("void-never-booked-500.json"));

        failure.Message.Should().Be("Worldwide Express had a problem and could not complete the request (answer 500).");
    }

    [Theory]
    [InlineData(401, """{"message":"Unauthorized"}""")]
    [InlineData(401, """{"error":"access_denied","error_description":"Unauthorized"}""")]
    [InlineData(403, "<html>Forbidden</html>")]
    public void Failure_ARefusedSignIn_PointsAtTheKeys(int status, string body)
    {
        var failure = WwexAnswers.Failure(status, body);

        failure.Kind.Should().Be(CarrierErrorKind.Unauthorized);
        failure.Message.Should().Contain("Setup → Shipping carrier");
    }

    [Fact]
    public void Failure_ABookingRefusal_KeepsTheCorrelationIdWwexSupportAsksFor()
    {
        var failure = WwexAnswers.Failure(422, Staging.Read("book-refused-422.json"));

        failure.Kind.Should().Be(CarrierErrorKind.Refused);
        failure.Message.Should().Be("Worldwide Express: no product transaction records found");
        failure.CorrelationId.Should().StartWith("NG-");
    }

    [Theory]
    [InlineData("weight is required; exception: AppException", "weight is required")]
    [InlineData("a; exception: SystemException; exception: SystemException", "a")]
    [InlineData("<no-description>; exception: SystemException", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData("Shipment already has order placed", "Shipment already has order placed")]
    public void CleanMessage_KeepsOnlyThePartForPeople(string? raw, string? expected) =>
        WwexAnswers.CleanMessage(raw).Should().Be(expected);
}

public class WwexAnswersTokenTests
{
    // A token shaped like WWEX's: header.payload.signature, with the two claims the settings page shows.
    private static string TokenWith(string payloadJson) =>
        "e30." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    [Fact]
    public void ParseToken_ReadsTheLifetimeAndWhoseTokenItIs()
    {
        var now = new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);
        var token = TokenWith(
            """{"https://speedship.wwex.com/primaryNamespace":"ss.wwex.customer.W000907595","https://speedship.wwex.com/apiPlan":"small","exp":1}""");

        var parsed = WwexAnswers.ParseToken($$"""{"access_token":"{{token}}","expires_in":86400,"token_type":"Bearer"}""", now);

        parsed.Should().Be(new WwexToken(token, now.AddDays(1), "W000907595", "small"));
    }

    [Fact]
    public void ParseToken_ATokenThatIsNotAJwt_StillWorks()
    {
        var parsed = WwexAnswers.ParseToken("""{"access_token":"opaque","expires_in":"3600"}""", DateTime.UnixEpoch);

        parsed.AccessToken.Should().Be("opaque");
        parsed.Account.Should().BeNull();
        parsed.ExpiresAtUtc.Should().Be(DateTime.UnixEpoch.AddHours(1));
    }

    [Theory]
    [InlineData("""{"expires_in":86400}""")]
    [InlineData("""{"access_token":"x"}""")]
    [InlineData("not json")]
    public void ParseToken_AnIncompleteAnswer_IsUnreadable(string json)
    {
        var act = () => WwexAnswers.ParseToken(json, DateTime.UnixEpoch);

        act.Should().Throw<CarrierException>().Where(e => e.Kind == CarrierErrorKind.Unreadable);
    }
}
