using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Carriers;

namespace LabelsMis.Web.Tests.Carriers;

/// <summary>Record shipment's label path against the fake carrier: rates, the purchase, and what a
/// purchase does to the shipment, the jobs, the order and the invoice.</summary>
[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierLabelPurchaseTests : CarrierShipmentTestHost
{
    [Fact]
    public async Task GetRates_AsksTheCarrierAboutTheOrdersBoxes_AndBuysNothing()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);

        var quote = await RatesAsync(order, Box(5m), Box(7.5m, declaredValue: 500m));

        quote.Rates.Select(r => (r.ServiceCode, r.Cost)).Should().Equal(("GND", 32.5m), ("2DA", 97.5m), ("1DA", 195m));
        quote.Rates[0].Boxes.Select(b => b.Cost).Should().Equal(15m, 17.5m);
        quote.Rates[0].Charges.Should().ContainSingle().Which.Should().Be(new QuotedCharge("FRT", "Freight", 32.5m));
        quote.BillTo.Should().Be(new CarrierBillTo("V01714", "75226"));
        quote.Environment.Should().Be(CarrierEnvironment.Staging);
        quote.Token.Should().NotBeNullOrWhiteSpace().And.NotContain(quote.QuoteId, "the page gets it sealed");

        var asked = Carrier.RateRequests.Should().ContainSingle().Subject;
        asked.ShipFrom.Should().Be(ShopAddress.ToParty());
        asked.ShipTo.Should().Be(new CarrierParty(
            "Desert Bloom Coffee", "Pat Receiver", "480-555-0142", "2200 E Camelback Rd", null, "Phoenix", "AZ", "85016"));
        asked.Reference1.Should().Be(order.OrderNumber);
        asked.Reference2.Should().StartWith("PO ");
        asked.Boxes.Should().Equal(new CarrierBox(5m, 12m, 10m, 8m, 0m), new CarrierBox(7.5m, 12m, 10m, 8m, 500m));
        Carrier.CountOf(CarrierOperations.Book).Should().Be(0);
        (await InDbAsync(db => Task.FromResult(db.Shipments.Any(s => s.SalesOrderId == order.OrderId)))).Should().BeFalse();
        (await ReadLogAsync()).Should().ContainSingle().Which.SalesOrderId.Should().Be(order.OrderId);
    }

    [Fact]
    public async Task GetRates_WhatTheCarrierWouldRefuse_IsSaidFirstInShopWords()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var request = Request(Box(0m), Box(151m)) with { ShipToPhone = "555-0142", ShipDate = new DateOnly(2026, 12, 25) };

        var act = () => WithLabelsAsync(labels => labels.GetRatesAsync(order.OrderId, request));

        (await act.Should().ThrowAsync<LabelRequestException>()).Which.Problems.Should().Equal(
            "Package 1 needs a weight.",
            "Package 2 weighs 151 lb; UPS takes at most 150 lb a package.",
            "Ship-to needs a 10-digit phone number for the label.",
            "The ship date has to be today or within the next two weeks to buy a label.");
        Carrier.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRates_AnAddressOutsideTheUnitedStates_IsRefused()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var request = Request(Box(5m)) with { ShipTo = CustomerAddress with { Country = "CA" } };

        var act = () => WithLabelsAsync(labels => labels.GetRatesAsync(order.OrderId, request));

        (await act.Should().ThrowAsync<LabelRequestException>()).Which.Problems.Should().Equal(
            "LabelShop buys UPS labels for addresses in the United States only.");
    }

    [Fact]
    public async Task GetRates_WithNoKeys_SaysWhereToAddThem()
    {
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);

        var act = () => RatesAsync(order, Box(5m));

        (await act.Should().ThrowAsync<CarrierNotConfiguredException>()).WithMessage(CarrierSettingsService.NotConfiguredMessage);
    }

    [Fact]
    public async Task Buy_RecordsTheShipmentWithItsTrackingNumberAndTheCarriersPrice()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var invoice = await WithInvoicesAsync(invoices => invoices.CreateFromSalesOrderAsync(order.OrderId));
        var multiplier = await CarrierMultiplierAsync();
        var quote = await RatesAsync(order, Box(5m));

        var result = await BuyAsync(order, quote, "GND", Box(5m));

        result.Confirmed.Should().BeTrue();
        var tracking = Carrier.BookingOf(quote.QuoteId)!.Boxes.Single().TrackingNumber;
        result.Message.Should().Be(
            $"Label bought: UPS Ground, tracking {tracking}. Carrier cost $15.00, billed shipping {CarrierGateway.Money(15m * multiplier)}. "
            + "This is a staging sample: it can't ship and nothing is billed.");

        var shipment = (await ReadShipmentAsync(result.ShipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.LabelState.Should().Be(ShipmentLabelState.Purchased);
        shipment.Carrier.Should().Be(LabelsMis.Domain.Enums.Carrier.Ups);
        shipment.CarrierEnvironment.Should().Be(CarrierEnvironment.Staging);
        shipment.CarrierQuoteId.Should().Be(quote.QuoteId);
        shipment.CarrierServiceCode.Should().Be("GND");
        shipment.CarrierServiceName.Should().Be("UPS Ground");
        shipment.CarrierOrderNumber.Should().StartWith("NUD");
        shipment.CarrierBillToAccount.Should().Be("V01714");
        shipment.CarrierCost.Should().Be(15m);
        shipment.BilledShipping.Should().Be(15m * multiplier);
        shipment.TotalShippingCost.Should().Be(15m);
        shipment.ShipDate.Should().Be(new DateOnly(2026, 11, 24));
        shipment.ShipToSnapshot.Should().Be(CustomerAddress);
        shipment.ShipToContact.Should().Be("Pat Receiver");
        shipment.ShipToPhone.Should().Be("480-555-0142");
        shipment.ShipFromSnapshot.Street1.Should().Be("100 W Main St");
        shipment.Lines.Should().ContainSingle().Which.QuantityShipped.Should().Be(500);
        var package = shipment.Packages.Should().ContainSingle().Subject;
        package.TrackingNumber.Should().Be(tracking);
        package.CarrierPackageId.Should().NotBeNullOrWhiteSpace();
        package.ShippingCost.Should().Be(15m);
        package.WeightLb.Should().Be(5m);

        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Shipped);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);
        (await InDbAsync(db => Task.FromResult(db.Invoices.Single(i => i.Id == invoice.Id).ShippingAmount)))
            .Should().Be(15m * multiplier, "the draft invoice follows the carrier's price with no typing");
        (await WithInvoicesAsync(invoices => invoices.GetWaitingReasonsAsync(invoice.Id))).Should().BeEmpty();

        var log = await ReadLogAsync();
        log.Select(l => l.Operation).Should().BeEquivalentTo(
            CarrierOperations.Rates, CarrierOperations.Book, CarrierOperations.Label);
        var booked = log.Single(l => l.Operation == CarrierOperations.Book);
        booked.ShipmentId.Should().Be(shipment.Id);
        booked.SalesOrderId.Should().Be(order.OrderId);
        booked.Detail.Should().Be(
            $"{shipment.ShipmentNumber}: UPS Ground $15.00, 1 box: Order {shipment.CarrierOrderNumber}: {tracking}");
    }

    [Fact]
    public async Task Buy_SeveralBoxes_PutsEachTrackingNumberOnItsOwnBox()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 300, 200);
        var quote = await RatesAsync(order, Box(12m), Box(7.5m));
        // The carrier answers with the boxes the other way round: the keys decide, not the order.
        Carrier.BookScript.Enqueue(request =>
        {
            var booking = Carrier.Book(request);
            return booking with { Boxes = booking.Boxes.Reverse().ToList() };
        });

        var result = await BuyAsync(order, quote, "2DA", Box(12m), Box(7.5m));

        var shipment = (await ReadShipmentAsync(result.ShipmentId))!;
        var booked = Carrier.BookingOf(quote.QuoteId)!.Boxes;
        var boxes = shipment.Packages.OrderBy(p => p.PackageNumber).ToList();
        boxes.Select(b => (b.WeightLb, b.TrackingNumber, b.ShippingCost)).Should().Equal(
            (12m, booked[0].TrackingNumber, 66m),
            (7.5m, booked[1].TrackingNumber, 52.5m));
        shipment.CarrierCost.Should().Be(118.5m);
        shipment.CarrierServiceCode.Should().Be("2DA");
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);
        (await JobStatusAsync(order.JobIds[1])).Should().Be(JobStatus.Shipped);
    }

    [Fact]
    public async Task Buy_KeepsTheLabelFileInTheShopsFormat()
    {
        await SaveKeysAsync();
        await WithSettingsAsync(s => s.SaveDefaultsAsync("GND", true, [], CarrierLabelFormat.PdfLetter));
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 300);

        var result = await BuyAsync(order, "GND", Box(5m), Box(6m));

        var shipment = (await ReadShipmentAsync(result.ShipmentId))!;
        Storage.Files.Keys.Should().Equal(
            $"carrier-labels/{shipment.Id:N}/{shipment.ShipmentNumber}-labels-letter.pdf");
        Carrier.LabelRequests.Should().Equal((shipment.CarrierQuoteId!, null, CarrierLabelFormat.PdfLetter));
    }

    [Fact]
    public async Task Buy_WhenTheLabelFileCannotBeFetchedOrKept_TheLabelIsStillBought()
    {
        await SaveKeysAsync();
        var first = await SeedOrderAsync(ShippingMethodType.Carrier, 300);
        var second = await SeedOrderAsync(ShippingMethodType.Carrier, 300);
        Carrier.LabelScript.Enqueue(() => throw new CarrierException(
            CarrierErrorKind.Server, "Worldwide Express had a problem and could not complete the request (answer 500).", 500));

        var carrierDown = await BuyAsync(first, "GND", Box(5m));
        Storage.FailUploads = true;
        var storageDown = await BuyAsync(second, "GND", Box(5m));

        carrierDown.Confirmed.Should().BeTrue();
        storageDown.Confirmed.Should().BeTrue();
        Storage.Files.Should().BeEmpty();
        (await ReadShipmentAsync(carrierDown.ShipmentId))!.LabelState.Should().Be(ShipmentLabelState.Purchased);
        (await ReadShipmentAsync(storageDown.ShipmentId))!.LabelState.Should().Be(ShipmentLabelState.Purchased);
    }

    [Fact]
    public async Task Buy_AfterABoxChanged_IsRefused_AndNothingIsBought()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));

        var heavier = () => BuyAsync(order, quote, "GND", Box(50m));
        var elsewhere = () => WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)) with { ShipTo = CustomerAddress with { Zip = "10118" } }, order.ShipLines(),
            quote.Token, quote.Rates[0].OfferId));
        var residential = () => WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)) with { ShipToResidential = true }, order.ShipLines(),
            quote.Token, quote.Rates[0].OfferId));

        const string stale = "The address, the ship date or a package changed after these rates were fetched. Get rates again.";
        (await heavier.Should().ThrowAsync<StaleQuoteException>()).WithMessage(stale);
        (await elsewhere.Should().ThrowAsync<StaleQuoteException>()).WithMessage(stale);
        (await residential.Should().ThrowAsync<StaleQuoteException>()).WithMessage(stale);
        Carrier.CountOf(CarrierOperations.Book).Should().Be(0);
        (await InDbAsync(db => Task.FromResult(db.Shipments.Any(s => s.SalesOrderId == order.OrderId)))).Should().BeFalse();
    }

    [Fact]
    public async Task Buy_TheSameWeightTypedAnotherWay_IsTheSameShipment()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, new LabelBoxInput(5m, 12m, 10m, 8m, 0m));

        var result = await BuyAsync(order, quote, "GND", new LabelBoxInput(5.0m, 12.00m, 10.0m, 8m, 0.00m));

        result.Confirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Buy_WithATokenThePageDidNotGetFromTheServer_IsRefused()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        var offer = quote.Rates[0].OfferId;

        var tampered = () => WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)), order.ShipLines(), quote.Token[..^6] + "AAAAAA", offer));
        var missing = () => WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)), order.ShipLines(), null, offer));

        (await tampered.Should().ThrowAsync<StaleQuoteException>()).WithMessage("These rates can't be read any more. Get rates again.");
        await missing.Should().ThrowAsync<StaleQuoteException>();
        Carrier.CountOf(CarrierOperations.Book).Should().Be(0);
    }

    [Fact]
    public async Task Buy_FromRatesThatAreHalfAnHourOld_IsRefused()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        Time.Advance(TimeSpan.FromMinutes(31));

        var act = () => BuyAsync(order, quote, "GND", Box(5m));

        (await act.Should().ThrowAsync<StaleQuoteException>()).WithMessage("These rates are more than 30 minutes old. Get rates again.");
        (await WithLabelsAsync(labels => Task.FromResult(labels.TryReadQuote(quote.Token)))).Should().BeNull();
    }

    [Fact]
    public async Task Buy_AfterTheKeysChangedWorlds_IsRefused()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        await SaveKeysAsync(CarrierEnvironment.Production);

        var act = () => BuyAsync(order, quote, "GND", Box(5m));

        (await act.Should().ThrowAsync<StaleQuoteException>())
            .WithMessage("The carrier keys changed after these rates were fetched. Get rates again.");
    }

    [Fact]
    public async Task Buy_TheSameRatesTwice_BuysOnce()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 300, 200);
        var quote = await RatesAsync(order, Box(5m));
        var first = await BuyAsync(order, quote, "GND", Box(5m));

        var again = () => BuyAsync(order, quote, "GND", Box(5m));

        (await again.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "A label has already been bought from these rates, or is being bought right now. "
            + "Look for it under Shipments, or get rates again to buy another.");
        Carrier.CountOf(CarrierOperations.Book).Should().Be(1);
        (await InDbAsync(db => Task.FromResult(db.Shipments.Count(s => s.SalesOrderId == order.OrderId)))).Should().Be(1);
        first.Confirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Buy_WithNoItemTicked_IsRefused_AndTheRatesStayGood()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));

        var act = () => WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)), [], quote.Token, quote.Rates[0].OfferId));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Select at least one item to ship.");
        Carrier.CountOf(CarrierOperations.Book).Should().Be(0);
        (await BuyAsync(order, quote, "GND", Box(5m))).Confirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Buy_RefusedByTheCarrier_LeavesNothingBehind()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        Carrier.BookScript.Enqueue(_ => throw new CarrierException(
            CarrierErrorKind.Refused, "Worldwide Express: no product transaction records found", 422, "NG-refused"));

        var act = () => BuyAsync(order, quote, "GND", Box(5m));

        (await act.Should().ThrowAsync<CarrierException>()).WithMessage("Worldwide Express: no product transaction records found");
        (await InDbAsync(db => Task.FromResult(db.Shipments.Any(s => s.SalesOrderId == order.OrderId)))).Should().BeFalse();
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
        (await OrderStatusAsync(order)).Should().NotBe(SalesOrderStatus.Shipped);
        var refused = (await ReadLogAsync()).Single(l => l.Operation == CarrierOperations.Book);
        refused.Succeeded.Should().BeFalse();
        refused.CorrelationId.Should().Be("NG-refused");
        refused.Detail.Should().Contain("UPS Ground $15.00", "the log keeps the story of a shipment that is gone");
    }
}

/// <summary>A purchase the carrier never answered: nothing may count as shipped, no second label
/// may be bought, and Check with carrier has to end with either a recorded label or none at all.</summary>
[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierUnansweredPurchaseTests : CarrierShipmentTestHost
{
    private static readonly CarrierException Silence =
        new(CarrierErrorKind.NoAnswer, "Worldwide Express did not answer in time.");

    private Task<LabelCheckResult> CheckAsync(Guid shipmentId) =>
        WithLabelsAsync(labels => labels.CheckUnconfirmedAsync(shipmentId));

    /// <summary>The carrier books it and the answer is lost on the way back.</summary>
    private async Task<(SeededOrder Order, LabelQuote Quote, BuyLabelResult Result)> BuyWithTheAnswerLostAsync()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        Carrier.BookScript.Enqueue(request =>
        {
            Carrier.Book(request);
            throw Silence;
        });
        return (order, quote, await BuyAsync(order, quote, "GND", Box(5m)));
    }

    [Fact]
    public async Task Buy_WithNoAnswer_KeepsThePurchaseOpen_AndNothingCountsAsShipped()
    {
        var (order, quote, result) = await BuyWithTheAnswerLostAsync();

        result.Confirmed.Should().BeFalse();
        result.Message.Should().Be(
            "Worldwide Express did not answer in time. LabelShop doesn't know whether the label was bought, "
            + "so nothing is recorded as shipped yet. Use Check with carrier below to settle it.");
        var shipment = (await ReadShipmentAsync(result.ShipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.Pending);
        shipment.LabelState.Should().Be(ShipmentLabelState.Unconfirmed);
        shipment.CarrierQuoteId.Should().Be(quote.QuoteId);
        shipment.CarrierCost.Should().Be(15m);
        shipment.BilledShipping.Should().BeNull();
        shipment.Packages.Single().TrackingNumber.Should().BeNull();
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
        (await OrderStatusAsync(order)).Should().NotBe(SalesOrderStatus.Shipped);
        (await WithShipmentsAsync(s => s.GetOrderForManualShipmentAsync(order.OrderId)))!.Lines.Single().QuantityShipped
            .Should().Be(0);
        Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task WhileAPurchaseIsOpen_NoOtherLabelCanBeBoughtForTheOrder()
    {
        var (order, _, result) = await BuyWithTheAnswerLostAsync();
        var number = (await ReadShipmentAsync(result.ShipmentId))!.ShipmentNumber;

        var rates = () => RatesAsync(order, Box(5m));

        (await rates.Should().ThrowAsync<InvalidOperationException>()).WithMessage(CarrierShipmentService.UnconfirmedMessage(number));
        (await WithShipmentsAsync(s => s.GetUnconfirmedShipmentAsync(order.OrderId)))!.ShipmentNumber.Should().Be(number);
    }

    [Fact]
    public async Task Check_WhenTheFirstAttemptWentThrough_VoidsThatOrder_AndRemovesTheShipment()
    {
        var (order, quote, result) = await BuyWithTheAnswerLostAsync();
        var number = (await ReadShipmentAsync(result.ShipmentId))!.ShipmentNumber;

        var check = await CheckAsync(result.ShipmentId);

        check.Outcome.Should().Be(LabelCheckOutcome.Discarded);
        check.SalesOrderId.Should().Be(order.OrderId);
        check.Message.Should().Be(
            "The first attempt had gone through, but its answer never arrived, so the tracking numbers were lost. "
            + "LabelShop voided that order with Worldwide Express: nothing ships and nothing is billed. "
            + $"{number} is removed. Record the shipment again to buy a new label.");
        Carrier.IsVoided(quote.QuoteId).Should().BeTrue("no label may be left alive at the carrier that LabelShop can't see");
        (await ReadShipmentAsync(result.ShipmentId)).Should().BeNull();
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
        (await WithShipmentsAsync(s => s.GetUnconfirmedShipmentAsync(order.OrderId))).Should().BeNull();
        (await ReadLogAsync()).Select(l => (l.Operation, l.Succeeded)).Should().BeEquivalentTo(new[]
        {
            (CarrierOperations.Rates, true),
            (CarrierOperations.Book, false),
            (CarrierOperations.BookCheck, false),
            (CarrierOperations.Void, true)
        });

        // The order ships again with a fresh label.
        (await BuyAsync(order, "GND", Box(5m))).Confirmed.Should().BeTrue();
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);
    }

    [Fact]
    public async Task Check_WhenTheFirstAttemptNeverArrived_BuysTheLabelNow()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        Carrier.BookScript.Enqueue(_ => throw Silence);
        var result = await BuyAsync(order, quote, "GND", Box(5m));
        Carrier.IsBooked(quote.QuoteId).Should().BeFalse();

        var check = await CheckAsync(result.ShipmentId);

        check.Outcome.Should().Be(LabelCheckOutcome.Bought);
        check.Message.Should().StartWith("Worldwide Express had not bought the label the first time, so it is bought now: UPS Ground, tracking ");
        var shipment = (await ReadShipmentAsync(result.ShipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.LabelState.Should().Be(ShipmentLabelState.Purchased);
        shipment.Packages.Single().TrackingNumber.Should().Be(Carrier.BookingOf(quote.QuoteId)!.Boxes.Single().TrackingNumber);
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Shipped);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);
        Storage.Files.Should().ContainSingle();
    }

    [Fact]
    public async Task Check_WhenTheCarrierIsStillSilent_ChangesNothing()
    {
        var (_, quote, result) = await BuyWithTheAnswerLostAsync();
        Carrier.BookScript.Enqueue(_ => throw Silence);

        var check = await CheckAsync(result.ShipmentId);

        check.Outcome.Should().Be(LabelCheckOutcome.StillOpen);
        check.Message.Should().Be("Worldwide Express did not answer in time. Nothing has changed. Check again in a few minutes.");
        (await ReadShipmentAsync(result.ShipmentId))!.LabelState.Should().Be(ShipmentLabelState.Unconfirmed);
        Carrier.IsVoided(quote.QuoteId).Should().BeFalse();
    }

    [Fact]
    public async Task Check_WhenTheVoidFails_KeepsTheShipment_SoItCanBeTriedAgain()
    {
        var (_, quote, result) = await BuyWithTheAnswerLostAsync();
        Carrier.VoidScript.Enqueue(_ => throw Silence);

        var first = await CheckAsync(result.ShipmentId);
        var second = await CheckAsync(result.ShipmentId);

        first.Outcome.Should().Be(LabelCheckOutcome.StillOpen);
        first.Message.Should().StartWith("Worldwide Express has an order for this shipment, but its tracking numbers were lost");
        second.Outcome.Should().Be(LabelCheckOutcome.Discarded);
        Carrier.IsVoided(quote.QuoteId).Should().BeTrue();
        (await ReadShipmentAsync(result.ShipmentId)).Should().BeNull();
    }

    [Fact]
    public async Task Check_WhenTheCarrierHasNoSuchOrderAndWillNotMakeOne_RemovesTheShipment()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        Carrier.BookScript.Enqueue(_ => throw Silence);
        var result = await BuyAsync(order, quote, "GND", Box(5m));
        Carrier.BookScript.Enqueue(_ => throw new CarrierException(
            CarrierErrorKind.Refused, "Worldwide Express: no product transaction records found", 422));

        var check = await CheckAsync(result.ShipmentId);

        check.Outcome.Should().Be(LabelCheckOutcome.Discarded);
        check.Message.Should().StartWith("No label was bought: Worldwide Express has no order for this shipment");
        (await ReadShipmentAsync(result.ShipmentId)).Should().BeNull();
        Carrier.CountOf(CarrierOperations.Void).Should().Be(0);
    }

    [Fact]
    public async Task Check_WithOtherKeysThanItWasBoughtWith_ChangesNothing()
    {
        var (_, _, result) = await BuyWithTheAnswerLostAsync();
        await SaveKeysAsync(CarrierEnvironment.Production);

        var check = await CheckAsync(result.ShipmentId);

        check.Outcome.Should().Be(LabelCheckOutcome.StillOpen);
        check.Message.Should().StartWith("This label was bought with the carrier's staging keys");
        (await ReadShipmentAsync(result.ShipmentId))!.LabelState.Should().Be(ShipmentLabelState.Unconfirmed);
    }

    [Fact]
    public async Task Check_OnAShipmentWithNothingOpen_IsRefused()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));

        var act = () => CheckAsync(bought.ShipmentId);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("This shipment has no label purchase waiting on the carrier.");
        Carrier.CountOf(CarrierOperations.Book).Should().Be(1);
    }
}

/// <summary>Printing: the label files of a bought label.</summary>
[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierLabelFileTests : CarrierShipmentTestHost
{
    private Task<LabelFile> LabelAsync(Guid shipmentId, Guid? packageId, CarrierLabelFormat format) =>
        WithLabelsAsync(labels => labels.GetLabelAsync(shipmentId, packageId, format));

    [Fact]
    public async Task TheFormatKeptAtPurchase_IsPrintedWithoutAskingTheCarrierAgain()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        var shipment = (await ReadShipmentAsync(bought.ShipmentId))!;
        var tracking = shipment.Packages.Single().TrackingNumber;

        var first = await LabelAsync(shipment.Id, null, CarrierLabelFormat.Pdf4x6);
        var second = await LabelAsync(shipment.Id, shipment.Packages.Single().Id, CarrierLabelFormat.Pdf4x6);

        first.FileName.Should().Be($"{shipment.ShipmentNumber}-{tracking}-4x6.pdf");
        first.ContentType.Should().Be("application/pdf");
        System.Text.Encoding.ASCII.GetString(first.Content).Should().Be($"%PDF-1.4 Pdf4x6 {tracking}");
        second.Content.Should().Equal(first.Content);
        Carrier.CountOf(CarrierOperations.Label).Should().Be(1, "the one made when the label was bought");
    }

    [Fact]
    public async Task AnotherFormat_IsFetchedOnce_ThenKept()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m), Box(6m));
        var shipment = (await ReadShipmentAsync(bought.ShipmentId))!;
        var second = shipment.Packages.Single(p => p.PackageNumber == 2);

        var zpl = await LabelAsync(shipment.Id, second.Id, CarrierLabelFormat.Zpl);
        await LabelAsync(shipment.Id, second.Id, CarrierLabelFormat.Zpl);
        var letter = await LabelAsync(shipment.Id, null, CarrierLabelFormat.PdfLetter);
        await LabelAsync(shipment.Id, null, CarrierLabelFormat.PdfLetter);

        zpl.FileName.Should().Be($"{shipment.ShipmentNumber}-{second.TrackingNumber}.zpl");
        zpl.ContentType.Should().Be("text/plain");
        System.Text.Encoding.ASCII.GetString(zpl.Content).Should().Be($"^XA^FD{second.TrackingNumber}^FS^XZ");
        letter.FileName.Should().Be($"{shipment.ShipmentNumber}-labels-letter.pdf");
        Carrier.LabelRequests.Skip(2).Should().Equal(
            (shipment.CarrierQuoteId!, second.TrackingNumber, CarrierLabelFormat.Zpl),
            (shipment.CarrierQuoteId!, null, CarrierLabelFormat.PdfLetter));
        Storage.Files.Should().HaveCount(4, "two 4 × 6 labels from the purchase, the ZPL and the Letter page");
    }

    [Fact]
    public async Task ALabelThatCouldNotBeKeptAtPurchase_IsFetchedWhenItIsPrinted()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        Storage.FailUploads = true;
        var bought = await BuyAsync(order, "GND", Box(5m));
        Storage.FailUploads = false;

        var label = await LabelAsync(bought.ShipmentId, null, CarrierLabelFormat.Pdf4x6);

        label.Content.Should().NotBeEmpty();
        Storage.Files.Should().ContainSingle();
    }

    [Fact]
    public async Task SeveralPackages_NeedOneToBePicked()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m), Box(6m));

        var noPick = () => LabelAsync(bought.ShipmentId, null, CarrierLabelFormat.Pdf4x6);
        var stranger = () => LabelAsync(bought.ShipmentId, Guid.NewGuid(), CarrierLabelFormat.Zpl);

        (await noPick.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("This shipment has several packages: pick the one to print.");
        (await stranger.Should().ThrowAsync<InvalidOperationException>()).WithMessage("That package is not on this shipment.");
    }

    [Fact]
    public async Task AShipmentRecordedByHand_HasNoLabelToPrint()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var shipment = await WithShipmentsAsync(s => s.CreateManualShipmentAsync(
            order.OrderId,
            new Services.Shipments.ManualShipmentInput(
                new DateOnly(2026, 11, 24), LabelsMis.Domain.Enums.Carrier.Ups, CustomerAddress, order.ShipLines(),
                [new Services.Shipments.ManualPackageInput(5m, 12m, 10m, 8m, 0m, "1ZHANDTYPED0000001", 0m)], 18.99m)));

        var act = () => LabelAsync(shipment.Id, null, CarrierLabelFormat.Pdf4x6);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("No label was bought through LabelShop for this shipment.");
        Carrier.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnconfirmedPurchase_HasNoLabelToPrintYet()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var quote = await RatesAsync(order, Box(5m));
        Carrier.BookScript.Enqueue(_ => throw new CarrierException(CarrierErrorKind.NoAnswer, "Worldwide Express did not answer in time."));
        var result = await BuyAsync(order, quote, "GND", Box(5m));

        var act = () => LabelAsync(result.ShipmentId, null, CarrierLabelFormat.Pdf4x6);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The label purchase hasn't been confirmed by the carrier yet.");
    }
}
