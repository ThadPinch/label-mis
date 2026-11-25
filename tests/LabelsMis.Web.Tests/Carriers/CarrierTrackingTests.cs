using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Common;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Background;
using LabelsMis.Web.Services.Carriers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelsMis.Web.Tests.Carriers;

/// <summary>Tracking: what one answer from the carrier does to a shipment, and which shipments the
/// background check asks about. Requires the PostgreSQL test database.</summary>
[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierTrackingTests : CarrierShipmentTestHost
{
    private static readonly DateTime Created = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 11, 26, 15, 45, 0, DateTimeKind.Utc);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // The background check looks at every real label in the database, and the test database
        // outlives one test: labels left by earlier tests are marked as asked about far ahead.
        await InDbAsync(db => db.Shipments
            .Where(s => s.CarrierEnvironment == CarrierEnvironment.Production)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.TrackingCheckedAt, new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
    }

    private Task<TrackingRefreshResult> RefreshAsync(Guid shipmentId) =>
        WithLabelsAsync(labels => labels.RefreshTrackingAsync(shipmentId));

    private ShipmentTrackingPoller Poller() => new(
        Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ShipmentTrackingPoller>.Instance)
    {
        Pace = TimeSpan.Zero
    };

    private Task<List<TrackingEvent>> EventsAsync(Guid shipmentId) =>
        InDbAsync(db => db.TrackingEvents.AsNoTracking()
            .Where(e => e.ShipmentPackage.ShipmentId == shipmentId)
            .OrderBy(e => e.EventAt)
            .ThenBy(e => e.ShipmentPackage.PackageNumber)
            .ToListAsync());

    private int _bought;

    private async Task<Guid> BuyOneAsync(params LabelBoxInput[] boxes)
    {
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var shipmentId = (await BuyAsync(order, "GND", boxes.Length == 0 ? [Box(5m)] : boxes)).ShipmentId;

        // The purchase is stamped by the machine's clock and the poller reads the test clock: put
        // the purchase on the test clock too, or these tests pass or fail by the time of day. Each
        // one a second after the one before, because the poller asks in the order they were bought.
        var boughtAt = Now.AddSeconds(_bought++);
        await InDbAsync(db => db.Shipments.Where(s => s.Id == shipmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.LabelPurchasedAt, boughtAt)));
        return shipmentId;
    }

    [Fact]
    public async Task Refresh_KeepsTheBoxesStatusAndEachEventOnce()
    {
        await SaveKeysAsync();
        var shipmentId = await BuyOneAsync();

        var first = await RefreshAsync(shipmentId);
        var second = await RefreshAsync(shipmentId);

        first.Should().Be(new TrackingRefreshResult(true, true, false, false, "Tracking updated: Label created."));
        second.Should().Be(new TrackingRefreshResult(true, false, false, false, "Nothing new: label created."));
        var shipment = (await ReadShipmentAsync(shipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.TrackingCheckedAt.Should().Be(Now);
        shipment.Packages.Single().TrackingStatus.Should().Be(CarrierTrackingStatus.LabelCreated);
        var stored = (await EventsAsync(shipmentId)).Should().ContainSingle("asking twice stores the event once").Subject;
        stored.CarrierCode.Should().Be("CREATED");
        stored.StatusDescription.Should().Be("Label created");
        stored.EventAt.Should().Be(Created);
        stored.CreatedById.Should().Be(TestUserId);
        Carrier.TrackRequests.Should().Equal(shipment.Packages.Single().TrackingNumber, shipment.Packages.Single().TrackingNumber);
    }

    [Fact]
    public async Task Refresh_WhenTheBoxIsDelivered_DeliversTheShipment()
    {
        await SaveKeysAsync();
        var shipmentId = await BuyOneAsync();
        await RefreshAsync(shipmentId);
        Carrier.TrackingStatus = "DELIVERED";

        var result = await RefreshAsync(shipmentId);

        result.Should().Be(new TrackingRefreshResult(true, true, true, false, "Delivered."));
        var shipment = (await ReadShipmentAsync(shipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        shipment.Packages.Single().TrackingStatus.Should().Be(CarrierTrackingStatus.Delivered);
        (await EventsAsync(shipmentId)).Select(e => e.CarrierCode).Should().Equal("CREATED", "DELIVERED");
        (await WithShipmentsAsync(s => s.GetDetailAsync(shipmentId)))!.LatestTrackingStatus.Should().Be("Delivered");
    }

    [Fact]
    public async Task Refresh_SeveralBoxes_IsDeliveredOnlyWhenEveryBoxIs()
    {
        await SaveKeysAsync();
        var shipmentId = await BuyOneAsync(Box(5m), Box(6m));
        var quoteId = (await ReadShipmentAsync(shipmentId))!.CarrierQuoteId!;
        var booked = Carrier.BookingOf(quoteId)!.Boxes;
        CarrierTracking Answer(string second) => new(
            true,
            [
                new CarrierBoxTracking(booked[0].CarrierPackageId, "DELIVERED",
                    [new CarrierTrackingEvent("CREATED", Created), new CarrierTrackingEvent("DELIVERED", Later)]),
                new CarrierBoxTracking(booked[1].CarrierPackageId, second,
                    [new CarrierTrackingEvent("CREATED", Created), new CarrierTrackingEvent(second, Later)])
            ],
            "NG-track");
        Carrier.TrackScript.Enqueue(_ => Answer("OUT_FOR_DELIVERY"));
        Carrier.TrackScript.Enqueue(_ => Answer("DELIVERED"));

        var first = await RefreshAsync(shipmentId);
        var afterFirst = (await ReadShipmentAsync(shipmentId))!;
        var second = await RefreshAsync(shipmentId);

        first.Should().Be(new TrackingRefreshResult(
            true, true, false, false, "Tracking updated: package 1 delivered; package 2 out for delivery."));
        afterFirst.Status.Should().Be(ShipmentStatus.InTransit);
        afterFirst.Packages.OrderBy(p => p.PackageNumber).Select(p => p.TrackingStatus).Should().Equal(
            CarrierTrackingStatus.Delivered, CarrierTrackingStatus.OutForDelivery);
        second.Delivered.Should().BeTrue();
        second.Message.Should().Be("Every package is delivered.");
        (await ReadShipmentAsync(shipmentId))!.Status.Should().Be(ShipmentStatus.Delivered);
        (await EventsAsync(shipmentId)).Should().HaveCount(5, "two created, then delivered, out for delivery, delivered");
        Carrier.TrackRequests.Should().HaveCount(2, "one question covers every box of a shipment");
    }

    [Fact]
    public async Task Refresh_ALabelTheCarrierDoesNotListYet_ChangesNothingButNotesTheQuestion()
    {
        await SaveKeysAsync();
        var shipmentId = await BuyOneAsync();
        Carrier.TrackScript.Enqueue(_ => CarrierTracking.NotFound("NG-none"));

        var result = await RefreshAsync(shipmentId);

        result.Found.Should().BeFalse();
        result.Changed.Should().BeFalse();
        result.Message.Should().Be(
            "Worldwide Express doesn't list this label yet. Its tracking runs a little behind a new label: check again in a few minutes.");
        var shipment = (await ReadShipmentAsync(shipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.TrackingCheckedAt.Should().Be(Now);
        shipment.Packages.Single().TrackingStatus.Should().BeNull();
        (await EventsAsync(shipmentId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_ALabelVoidedAtTheCarrierBehindLabelShopsBack_IsFlagged_NotReversed()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var bought = await BuyAsync(order, "GND", Box(5m));
        Carrier.Void((await ReadShipmentAsync(bought.ShipmentId))!.CarrierQuoteId!);

        var result = await RefreshAsync(bought.ShipmentId);

        result.VoidedAtCarrier.Should().BeTrue();
        result.Message.Should().StartWith("Worldwide Express reports this label as voided, but the shipment still counts as shipped here.");
        var shipment = (await ReadShipmentAsync(bought.ShipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.InTransit, "a person decides; LabelShop doesn't take a shipment back by itself");
        shipment.Packages.Single().TrackingStatus.Should().Be(CarrierTrackingStatus.Voided);
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Shipped);

        // Void label then finishes it: the carrier says "already voided" and the shipment is taken back.
        var message = await WithLabelsAsync(labels => labels.VoidAsync(bought.ShipmentId, "voided in SpeedShip"));
        message.Should().StartWith("Worldwide Express had already voided this label.");
        (await JobStatusAsync(order.JobIds[0])).Should().Be(JobStatus.Rewound);
    }

    [Fact]
    public async Task Refresh_AWordLabelShopDoesNotKnow_IsKeptAsTheCarrierSentIt()
    {
        await SaveKeysAsync();
        var shipmentId = await BuyOneAsync();
        Carrier.TrackingStatus = "HELD_AT_CUSTOMS";

        var result = await RefreshAsync(shipmentId);

        result.Message.Should().Be("Tracking updated: HELD_AT_CUSTOMS.");
        (await ReadShipmentAsync(shipmentId))!.Packages.Single().TrackingStatus.Should().Be(CarrierTrackingStatus.Unknown);
        (await EventsAsync(shipmentId)).Select(e => e.StatusDescription).Should().Equal("Label created", "HELD_AT_CUSTOMS");
    }

    [Fact]
    public async Task Refresh_AShipmentRecordedByHand_IsNotAskedAbout()
    {
        await SaveKeysAsync();
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var tracking = $"1ZHAND{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        var shipment = await WithShipmentsAsync(s => s.CreateManualShipmentAsync(
            order.OrderId,
            new Services.Shipments.ManualShipmentInput(
                new DateOnly(2026, 11, 24), LabelsMis.Domain.Enums.Carrier.Ups, CustomerAddress, order.ShipLines(),
                [new Services.Shipments.ManualPackageInput(5m, 12m, 10m, 8m, 0m, tracking, 0m)], 18.99m)));

        var act = () => RefreshAsync(shipment.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Tracking is checked for labels bought through LabelShop. For this shipment, use the carrier's own tracking page.");
        Carrier.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSameCarrierEvent_CannotBeStoredTwice_EvenByTwoChecksAtOnce()
    {
        await SaveKeysAsync();
        var shipmentId = await BuyOneAsync();
        await RefreshAsync(shipmentId);
        var existing = (await EventsAsync(shipmentId)).Single();

        var duplicate = () => InDbAsync(async db =>
        {
            db.TrackingEvents.Add(TrackingEvent.Create(
                Guid.NewGuid(), existing.ShipmentPackageId, existing.EventAt, "Label created", null, null, TestUserId, Now, "created"));
            await db.SaveChangesAsync();
        });

        await duplicate.Should().ThrowAsync<DbUpdateException>("the unique index is what keeps two processes from doubling an event");
        (await EventsAsync(shipmentId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Poller_AsksAboutRealLabelsOnceTheyHaveSettled_AndThenLeavesThemAloneForHours()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);
        var first = await BuyOneAsync();
        var second = await BuyOneAsync();
        await InDbAsync(db => db.CarrierLogs.ExecuteDeleteAsync());

        // Bought moments ago: the carrier's tracking would not know them yet.
        (await Poller().RunOnceAsync(CancellationToken.None)).Should().Be(new TrackingRunResult(0, 0, 0, 0, null));
        Carrier.TrackRequests.Should().BeEmpty();

        Time.Advance(TimeSpan.FromDays(1));
        var run = await Poller().RunOnceAsync(CancellationToken.None);
        var again = await Poller().RunOnceAsync(CancellationToken.None);

        run.Should().Be(new TrackingRunResult(2, 2, 2, 0, null));
        again.Should().Be(new TrackingRunResult(0, 0, 0, 0, null), "they were asked about a moment ago");
        Carrier.TrackRequests.Should().HaveCount(2);
        (await ReadShipmentAsync(first))!.Packages.Single().TrackingStatus.Should().Be(CarrierTrackingStatus.LabelCreated);
        (await ReadShipmentAsync(second))!.TrackingCheckedAt.Should().Be(Now);

        var log = await ReadLogAsync();
        log.Should().HaveCount(2, "news is logged");
        log.Should().OnlyContain(l => l.Automatic && l.Succeeded && l.Operation == CarrierOperations.Track
            && l.CreatedById == TenantConstants.SystemUserId && l.Environment == CarrierEnvironment.Production);
        log.Select(l => l.ShipmentId).Should().BeEquivalentTo(new Guid?[] { first, second });
        log[0].Detail.Should().EndWith(": Label created");

        Time.Advance(TimeSpan.FromHours(4));
        var later = await Poller().RunOnceAsync(CancellationToken.None);

        later.Should().Be(new TrackingRunResult(2, 2, 0, 0, null));
        (await ReadLogAsync()).Should().HaveCount(2, "an answer with nothing new is not logged");
        (await EventsAsync(first)).Should().ContainSingle("row counts stay flat however often it is asked");
    }

    [Fact]
    public async Task Poller_TurnsAShipmentDelivered_AndStopsAskingAboutIt()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);
        var order = await SeedOrderAsync(ShippingMethodType.Carrier, 500);
        var shipmentId = (await BuyAsync(order, "GND", Box(5m))).ShipmentId;
        Time.Advance(TimeSpan.FromDays(1));
        await Poller().RunOnceAsync(CancellationToken.None);
        Carrier.TrackingStatus = "DELIVERED";
        Time.Advance(TimeSpan.FromHours(5));

        var run = await Poller().RunOnceAsync(CancellationToken.None);
        Time.Advance(TimeSpan.FromHours(5));
        var after = await Poller().RunOnceAsync(CancellationToken.None);

        run.Should().Be(new TrackingRunResult(1, 1, 1, 1, null));
        after.Due.Should().Be(0, "a delivered shipment has nothing more to say");
        var shipment = (await ReadShipmentAsync(shipmentId))!;
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        shipment.ModifiedById.Should().Be(TenantConstants.SystemUserId);
        (await OrderStatusAsync(order)).Should().Be(SalesOrderStatus.Shipped, "delivery doesn't move the order");
        (await ReadLogAsync()).Last(l => l.Operation == CarrierOperations.Track).Detail.Should().EndWith(": Delivered");
    }

    [Fact]
    public async Task Poller_DoesNothingForStagingSamples_OrWhenSwitchedOff_OrWithoutKeys()
    {
        await SaveKeysAsync();
        await BuyOneAsync();
        Time.Advance(TimeSpan.FromDays(1));

        (await Poller().RunOnceAsync(CancellationToken.None)).Due.Should().Be(0, "staging samples never move");

        await SaveKeysAsync(CarrierEnvironment.Production);
        await BuyOneAsync();
        Time.Advance(TimeSpan.FromDays(1));
        await WithSettingsAsync(s => s.SaveDefaultsAsync("GND", false, []));
        (await Poller().RunOnceAsync(CancellationToken.None)).Due.Should().Be(0, "tracking is switched off");

        await WithSettingsAsync(s => s.SaveDefaultsAsync("GND", true, []));
        await WithSettingsAsync(s => s.RemoveCredentialsAsync());
        (await Poller().RunOnceAsync(CancellationToken.None)).Due.Should().Be(0, "there are no keys");
        Carrier.TrackRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Poller_StopsAtTheFirstSignTheCarrierIsDown_AndStartsAgainNextTime()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);
        await BuyOneAsync();
        await BuyOneAsync();
        Time.Advance(TimeSpan.FromDays(1));
        Carrier.TrackScript.Enqueue(_ => throw new CarrierException(CarrierErrorKind.NoAnswer, "Worldwide Express did not answer in time."));

        var down = await Poller().RunOnceAsync(CancellationToken.None);
        var up = await Poller().RunOnceAsync(CancellationToken.None);

        down.Should().Be(new TrackingRunResult(2, 0, 0, 0, "Worldwide Express did not answer in time."));
        Carrier.TrackRequests.Should().HaveCount(3, "one question that failed, then both shipments on the next pass");
        up.Should().Be(new TrackingRunResult(2, 2, 2, 0, null));
        (await ReadLogAsync()).Count(l => l.Operation == CarrierOperations.Track && !l.Succeeded).Should().Be(1);
    }

    [Fact]
    public async Task Poller_AShipmentTheCarrierRefusesToAnswerAbout_IsSkippedForAWhile_NotRetriedEveryPass()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);
        var refused = await BuyOneAsync();
        var fine = await BuyOneAsync();
        Time.Advance(TimeSpan.FromDays(1));
        Carrier.TrackScript.Enqueue(_ => throw new CarrierException(
            CarrierErrorKind.Refused, "Worldwide Express: SearchShipmentsRQ.type: ERR_NO_MATCH", 422));

        var run = await Poller().RunOnceAsync(CancellationToken.None);
        var next = await Poller().RunOnceAsync(CancellationToken.None);

        run.Should().Be(new TrackingRunResult(2, 2, 1, 0, null));
        next.Due.Should().Be(0);
        (await ReadShipmentAsync(refused))!.TrackingCheckedAt.Should().Be(Now);
        (await ReadShipmentAsync(fine))!.Packages.Single().TrackingStatus.Should().Be(CarrierTrackingStatus.LabelCreated);
    }

    [Fact]
    public async Task Poller_GivesUpOnAShipmentThatNeverArrived_AfterThirtyDays()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);
        await BuyOneAsync();
        Time.Advance(TimeSpan.FromDays(CarrierShipmentService.TrackingGiveUpAfterDays + 1));

        (await Poller().RunOnceAsync(CancellationToken.None)).Due.Should().Be(0);
    }
}
