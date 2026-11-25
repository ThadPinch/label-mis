using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Carriers;
using LabelsMis.Web.Services.Shipments;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests.Carriers;

/// <summary>Voiding a shipment: the label at the carrier first, then everything the shipment did
/// to the jobs, the order and the invoice, and nothing at all when the carrier doesn't confirm.</summary>
[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierVoidTests : CarrierShipmentTestHost
{
    private static readonly CarrierException Silence =
        new(CarrierErrorKind.NoAnswer, "Worldwide Express did not answer in time.");

    private Task<string> VoidAsync(Guid shipmentId, string? reason = null) =>
        WithLabelsAsync(labels => labels.VoidAsync(shipmentId, reason));

    private Task<string> JobNumberAsync(Guid jobId) =>
        InDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.JobNumber).SingleAsync());

    private Task<decimal> InvoiceShippingAsync(Guid invoiceId) =>
        InDbAsync(db => db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.ShippingAmount).SingleAsync());

    /// <summary>Buys a label for part of the order's first line.</summary>
    private async Task<BuyLabelResult> BuyPartAsync(SeededOrder order, int quantity)
    {
        var quote = await RatesAsync(order, Box(5m));
        return await WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)), [new ManualShipmentLineInput(order.LineIds[0], order.JobIds[0], quantity)],
            quote.Token, quote.Rates.Single(r => r.ServiceCode == "GND").OfferId));
    }

    [Fact]
    public async Task Void_TakesTheLabelBackAtTheCarrier_AndTheShipmentOutOfTheOrder()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var invoice = await WithInvoicesAsync(invoices => invoices.CreateFromSalesOrderAsync(order.OrderId));
        var bought = await BuyAsync(order, "GND", Box(5m));
        var shipment = (await ReadShipmentAsync(bought.ShipmentId))!;
        var shippingWhileShipped = await InvoiceShippingAsync(invoice.Id);
        Storage.Files.Should().ContainSingle();

        var message = await VoidAsync(bought.ShipmentId, "wrong address");

        Carrier.IsVoided(shipment.CarrierQuoteId!).Should().BeTrue();
        var voided = (await ReadShipmentAsync(bought.ShipmentId))!;
        voided.Status.Should().Be(ShipmentStatus.Voided);
        voided.LabelState.Should().Be(ShipmentLabelState.Voided);
        voided.VoidReason.Should().Be("wrong address");
        voided.VoidedById.Should().Be(TestUserId);
        voided.VoidedAt.Should().NotBeNull();
        voided.Packages.Single().TrackingNumber.Should().Be(
            shipment.Packages.Single().TrackingNumber, "the number stays on file in case the label is ever billed");
        voided.Lines.Should().ContainSingle("the record of what it carried stays");

        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.InProduction);
        var line = (await WithShipmentsAsync(s => s.GetOrderForManualShipmentAsync(order.OrderId)))!.Lines.Single();
        line.QuantityShipped.Should().Be(0);
        line.IsReady.Should().BeTrue("the order can ship again");
        (await WithShipmentsAsync(s => s.GetJobShippingStateAsync(order.JobIds[0])))!.QuantityShipped.Should().Be(0);

        var shippingNow = await InvoiceShippingAsync(invoice.Id);
        shippingWhileShipped.Should().BeGreaterThan(0m);
        shippingNow.Should().Be(0m, "the voided shipment's shipping comes off the draft invoice");
        (await WithInvoicesAsync(invoices => invoices.GetWaitingReasonsAsync(invoice.Id))).Should().NotBeEmpty(
            "an invoice for an order that has not shipped waits again");
        Storage.Files.Should().BeEmpty("a voided label must not be printed again");
        message.Should().Be(
            $"Label voided with Worldwide Express. {shipment.ShipmentNumber} no longer counts as shipped. "
            + $"{await JobNumberAsync(order.JobIds[0])} is back at ready to ship. {order.OrderNumber} is back in production. "
            + $"Draft invoice {invoice.InvoiceNumber} now bills $0.00 shipping.");

        var print = () => WithLabelsAsync(labels => labels.GetLabelAsync(bought.ShipmentId, null, CarrierLabelFormat.Pdf4x6));
        (await print.Should().ThrowAsync<InvalidOperationException>()).WithMessage("This label was voided, so it can't be printed.");
        var entry = (await ReadLogAsync()).Single(l => l.Operation == CarrierOperations.Void);
        entry.Succeeded.Should().BeTrue();
        entry.ShipmentId.Should().Be(bought.ShipmentId);
    }

    [Fact]
    public async Task AfterAVoid_TheOrderShipsAgainWithANewLabel()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var first = await BuyAsync(order, "GND", Box(5m));
        await VoidAsync(first.ShipmentId);

        var second = await BuyAsync(order, "2DA", Box(6m));

        second.Confirmed.Should().BeTrue();
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Shipped);
        var statuses = await InDbAsync(db => db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrderId == order.OrderId).OrderBy(s => s.CreatedAt).Select(s => s.Status).ToListAsync());
        statuses.Should().Equal(ShipmentStatus.Voided, ShipmentStatus.InTransit);
        (await WithShipmentsAsync(s => s.GetOrderForManualShipmentAsync(order.OrderId)))!.Lines.Single().QuantityShipped
            .Should().Be(500, "only the live shipment counts");
    }

    [Fact]
    public async Task Void_WhenTheCarrierDoesNotConfirm_ChangesNothing_AndASecondTryFinishesIt()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        var quoteId = (await ReadShipmentAsync(bought.ShipmentId))!.CarrierQuoteId!;
        // The carrier voids it and the answer is lost.
        Carrier.VoidScript.Enqueue(quote =>
        {
            Carrier.Void(quote);
            throw Silence;
        });

        var first = () => VoidAsync(bought.ShipmentId);

        (await first.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Worldwide Express did not answer in time. Worldwide Express did not confirm the void, so nothing was changed here. "
            + "Try again: voiding twice does no harm.");
        (await ReadShipmentAsync(bought.ShipmentId))!.Status.Should().Be(ShipmentStatus.InTransit);
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Shipped);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);

        var second = await VoidAsync(bought.ShipmentId);

        second.Should().StartWith("Worldwide Express had already voided this label.");
        Carrier.IsVoided(quoteId).Should().BeTrue();
        (await ReadShipmentAsync(bought.ShipmentId))!.Status.Should().Be(ShipmentStatus.Voided);
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
    }

    [Fact]
    public async Task Void_RefusedByTheCarrier_ChangesNothing()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        Carrier.VoidScript.Enqueue(_ => throw new CarrierException(
            CarrierErrorKind.Refused, "Worldwide Express: the shipment has been picked up and can't be cancelled", 422));

        var act = () => VoidAsync(bought.ShipmentId);

        (await act.Should().ThrowAsync<CarrierException>())
            .WithMessage("Worldwide Express: the shipment has been picked up and can't be cancelled");
        var shipment = (await ReadShipmentAsync(bought.ShipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.LabelState.Should().Be(ShipmentLabelState.Purchased);
        Storage.Files.Should().ContainSingle("the label is still good");
    }

    [Fact]
    public async Task Void_WithOtherKeysThanTheLabelWasBoughtWith_ChangesNothing()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        await SaveKeysAsync(CarrierEnvironment.Production);

        var act = () => VoidAsync(bought.ShipmentId);

        (await act.Should().ThrowAsync<CarrierNotConfiguredException>())
            .WithMessage("This label was bought with the carrier's staging keys*");
        Carrier.CountOf(CarrierOperations.Void).Should().Be(0);
        (await ReadShipmentAsync(bought.ShipmentId))!.Status.Should().Be(ShipmentStatus.InTransit);
    }

    [Fact]
    public async Task Void_IsRefusedOnceTheInvoiceIsInQuickBooks_BeforeTheCarrierIsAsked()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var invoice = await WithInvoicesAsync(invoices => invoices.CreateFromSalesOrderAsync(order.OrderId));
        var bought = await BuyAsync(order, "GND", Box(5m));
        await WithInvoicesAsync(invoices => invoices.MarkExportedAsync([invoice.Id]));

        var act = () => VoidAsync(bought.ShipmentId);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            $"Invoice {invoice.InvoiceNumber} for this order is already in QuickBooks. Void that invoice first; then this shipment can be voided.");
        Carrier.CountOf(CarrierOperations.Void).Should().Be(0, "the label stays good while the shipment stays");
        (await ReadShipmentAsync(bought.ShipmentId))!.Status.Should().Be(ShipmentStatus.InTransit);
        var blocker = await WithShipmentsAsync(s => s.GetVoidBlockerAsync(bought.ShipmentId));
        blocker!.InvoiceId.Should().Be(invoice.Id);
        blocker.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent, QuickBooksExportStatus.None, "has already gone to the customer. Void that invoice first; then this shipment can be voided.")]
    [InlineData(InvoiceStatus.Draft, QuickBooksExportStatus.Queued, "is on its way to QuickBooks right now. Try again in a minute.")]
    public async Task Void_IsRefusedWhileTheInvoiceIsOut(InvoiceStatus status, QuickBooksExportStatus export, string expected)
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var invoice = await WithInvoicesAsync(invoices => invoices.CreateFromSalesOrderAsync(order.OrderId));
        var bought = await BuyAsync(order, "GND", Box(5m));
        await InDbAsync(async db =>
        {
            var tracked = await db.Invoices.SingleAsync(i => i.Id == invoice.Id);
            db.Entry(tracked).Property(i => i.Status).CurrentValue = status;
            db.Entry(tracked).Property(i => i.QbExportStatus).CurrentValue = export;
            await db.SaveChangesAsync();
        });

        var act = () => VoidAsync(bought.ShipmentId);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"Invoice {invoice.InvoiceNumber} for this order {expected}");
        Carrier.CountOf(CarrierOperations.Void).Should().Be(0);

        // Put the row back so the QuickBooks worker tests find nothing queued.
        await InDbAsync(async db =>
        {
            var tracked = await db.Invoices.SingleAsync(i => i.Id == invoice.Id);
            db.Entry(tracked).Property(i => i.QbExportStatus).CurrentValue = QuickBooksExportStatus.None;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Void_AVoidedInvoice_IsNotInTheWay()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var invoice = await WithInvoicesAsync(invoices => invoices.CreateFromSalesOrderAsync(order.OrderId));
        var bought = await BuyAsync(order, "GND", Box(5m));
        await WithInvoicesAsync(async invoices =>
        {
            await invoices.VoidAsync(invoice.Id, "made in error");
            return true;
        });

        var message = await VoidAsync(bought.ShipmentId);

        message.Should().NotContain("Draft invoice");
        (await ReadShipmentAsync(bought.ShipmentId))!.Status.Should().Be(ShipmentStatus.Voided);
    }

    [Fact]
    public async Task Void_OneOfTwoShipmentsOfALine_LeavesTheJobShipped_AndReopensTheOrder()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var first = await BuyPartAsync(order, 200);
        var second = await BuyPartAsync(order, 300);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);

        var message = await VoidAsync(first.ShipmentId);

        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Shipped, "300 of it are still on the other shipment");
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.InProduction);
        (await WithShipmentsAsync(s => s.GetOrderForManualShipmentAsync(order.OrderId)))!.Lines.Single().QuantityShipped
            .Should().Be(300);
        message.Should().NotContain("ready to ship").And.Contain($"{order.OrderNumber} is back in production.");
        (await ReadShipmentAsync(second.ShipmentId))!.Status.Should().Be(ShipmentStatus.InTransit);
    }

    [Fact]
    public async Task Void_OneLineOfTwo_LeavesTheOtherLineShipped()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 300, 200);
        var quote = await RatesAsync(order, Box(5m));
        var lineOne = await WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(5m)), [new ManualShipmentLineInput(order.LineIds[0], order.JobIds[0], 300)],
            quote.Token, quote.Rates[0].OfferId));
        var quoteTwo = await RatesAsync(order, Box(6m));
        await WithLabelsAsync(labels => labels.BuyLabelAsync(
            order.OrderId, Request(Box(6m)), [new ManualShipmentLineInput(order.LineIds[1], order.JobIds[1], 200)],
            quoteTwo.Token, quoteTwo.Rates[0].OfferId));
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);

        await VoidAsync(lineOne.ShipmentId);

        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
        (await JobStatusAsync(order.JobIds[1])).Should().Be(JobStatus.Shipped);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.InProduction);
    }

    [Fact]
    public async Task Void_AShipmentRecordedByHand_IsTakenBackWithoutAskingTheCarrier()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var tracking = $"1ZHAND{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        var shipment = await WithShipmentsAsync(s => s.CreateManualShipmentAsync(
            order.OrderId,
            new ManualShipmentInput(
                new DateOnly(2026, 11, 24), LabelsMis.Domain.Enums.Carrier.Ups, CustomerAddress, order.ShipLines(),
                [new ManualPackageInput(5m, 12m, 10m, 8m, 0m, tracking, 0m)], 18.99m)));
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped);

        var message = await VoidAsync(shipment.Id, "recorded on the wrong order");

        Carrier.Calls.Should().BeEmpty("LabelShop did not make this label, so it has nothing to void at the carrier");
        var voided = (await ReadShipmentAsync(shipment.Id))!;
        voided.Status.Should().Be(ShipmentStatus.Voided);
        voided.LabelState.Should().Be(ShipmentLabelState.None);
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.InProduction);
        message.Should().StartWith($"Shipment voided. {voided.ShipmentNumber} no longer counts as shipped.");
    }

    [Fact]
    public async Task Void_Twice_IsRefused()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        await VoidAsync(bought.ShipmentId);

        var again = () => VoidAsync(bought.ShipmentId);

        (await again.Should().ThrowAsync<InvalidOperationException>()).WithMessage("This shipment is already voided.");
        Carrier.CountOf(CarrierOperations.Void).Should().Be(1);
    }

    [Fact]
    public async Task AVoidedShipment_IsLeftOutOfThePackagelessShipmentAndTheOrdersProgress()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        await VoidAsync(bought.ShipmentId);

        var progress = (await WithShipmentsAsync(s => s.GetOrderShipProgressAsync(order.OrderId)))!;

        progress.Lines.Single().Shipped.Should().Be(0);
        progress.CanMarkShipped.Should().BeFalse("nothing has shipped, so it is not an under-run");
        var markShipped = () => WithShipmentsAsync(async s =>
        {
            await s.MarkOrderShippedAsync(order.OrderId);
            return true;
        });
        (await markShipped.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Line 1 hasn't shipped anything yet*");
    }
}
