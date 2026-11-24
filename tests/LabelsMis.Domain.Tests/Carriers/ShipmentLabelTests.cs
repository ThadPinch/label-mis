using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Tests.Carriers;

public class ShipmentLabelTests
{
    private static readonly Guid User = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);
    private static readonly CarrierBillTo BillTo = new("V01714", "75226");

    private static Shipment Pending(int boxes = 1)
    {
        var shipment = Shipment.CreatePending(
            Guid.NewGuid(), "SHP-T-1", Guid.NewGuid(), new DateOnly(2026, 11, 24), Carrier.Other, null, null,
            new ShippingAddress("Shop", "1 Main St", null, "Phoenix", "AZ", "85001", "US"),
            new ShippingAddress("Customer", "2 Oak St", null, "Tempe", "AZ", "85281", "US"),
            null, 0m, BillingType.Sender, null, User, Now);
        if (boxes > 0)
        {
            shipment.ReplacePackages(Enumerable.Range(1, boxes).Select(n =>
                ShipmentPackage.Create(Guid.NewGuid(), shipment.Id, n, 5m, 12m, 10m, 8m, 0m, User, Now)));
        }

        return shipment;
    }

    private static void Start(Shipment shipment, CarrierEnvironment environment = CarrierEnvironment.Production) =>
        shipment.StartLabelPurchase(
            environment, " quote-1 ", " offer-9 ", " gnd ", " UPS Ground ", BillTo, 18.99m, " Pat ", " 480-555-0142 ", true, User, Now);

    private static Shipment Purchased(CarrierEnvironment environment = CarrierEnvironment.Production)
    {
        var shipment = Pending();
        Start(shipment, environment);
        shipment.Packages.Single().SetCarrierLabel("1ZV017146460637994", "item-1", User, Now);
        shipment.ConfirmLabelPurchase("NUD34772465", User, Now);
        shipment.MarkInTransit(18.99m, User, Now);
        return shipment;
    }

    [Fact]
    public void StartLabelPurchase_NotesTheRateBeforeTheCarrierIsAsked()
    {
        var shipment = Pending();

        Start(shipment);

        shipment.LabelState.Should().Be(ShipmentLabelState.Unconfirmed);
        shipment.Status.Should().Be(ShipmentStatus.Pending, "nothing has shipped until the carrier books it");
        shipment.Carrier.Should().Be(Carrier.Ups);
        shipment.CarrierEnvironment.Should().Be(CarrierEnvironment.Production);
        shipment.CarrierQuoteId.Should().Be("quote-1");
        shipment.CarrierOfferId.Should().Be("offer-9");
        shipment.CarrierServiceCode.Should().Be("GND");
        shipment.CarrierServiceName.Should().Be("UPS Ground");
        shipment.CarrierBillTo.Should().Be(BillTo);
        shipment.CarrierCost.Should().Be(18.99m);
        shipment.ShipToContact.Should().Be("Pat");
        shipment.ShipToPhone.Should().Be("480-555-0142");
        shipment.ShipToResidential.Should().BeTrue();
        shipment.CountsAsShipped.Should().BeFalse();
    }

    [Fact]
    public void StartLabelPurchase_WithNoPackages_IsRefused()
    {
        var act = () => Start(Pending(boxes: 0));

        act.Should().Throw<InvalidOperationException>().WithMessage("A label needs at least one package.");
    }

    [Fact]
    public void StartLabelPurchase_Twice_IsRefused()
    {
        var shipment = Pending();
        Start(shipment);

        var act = () => Start(shipment);

        act.Should().Throw<InvalidOperationException>().WithMessage("A label can only be bought for a shipment that has not shipped.");
    }

    [Fact]
    public void StartLabelPurchase_WithoutTheCarriersQuote_IsRefused()
    {
        var act = () => Pending().StartLabelPurchase(
            CarrierEnvironment.Staging, " ", "offer", "GND", "UPS Ground", BillTo, 1m, null, null, false, User, Now);

        act.Should().Throw<ArgumentException>().WithMessage("The carrier's quote and offer are required.");
    }

    [Fact]
    public void ConfirmLabelPurchase_NeedsATrackingNumberOnEveryPackage()
    {
        var shipment = Pending(boxes: 2);
        Start(shipment);
        shipment.Packages.First().SetCarrierLabel("1ZV017148167928755", "item-1", User, Now);

        var act = () => shipment.ConfirmLabelPurchase("NUD34772466", User, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Every package needs its tracking number before the purchase is confirmed.");
        shipment.LabelState.Should().Be(ShipmentLabelState.Unconfirmed);
    }

    [Fact]
    public void ConfirmLabelPurchase_RecordsTheCarriersOrderAndWhen()
    {
        var shipment = Purchased();

        shipment.LabelState.Should().Be(ShipmentLabelState.Purchased);
        shipment.CarrierOrderNumber.Should().Be("NUD34772465");
        shipment.LabelPurchasedAt.Should().Be(Now);
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.CountsAsShipped.Should().BeTrue();
        shipment.Packages.Single().CarrierPackageId.Should().Be("item-1");
    }

    [Fact]
    public void ConfirmLabelPurchase_WithNothingWaiting_IsRefused()
    {
        var act = () => Pending().ConfirmLabelPurchase("NUD1", User, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("There is no label purchase waiting to be confirmed on this shipment.");
    }

    [Theory]
    [InlineData(CarrierEnvironment.Staging, true)]
    [InlineData(CarrierEnvironment.Production, false)]
    public void IsSampleLabel_IsALabelBoughtWithStagingKeys(CarrierEnvironment environment, bool sample) =>
        Purchased(environment).IsSampleLabel.Should().Be(sample);

    [Fact]
    public void IsSampleLabel_AHandRecordedShipment_IsNot() =>
        Pending().IsSampleLabel.Should().BeFalse();

    [Fact]
    public void SetCarrierQuote_KeepsTheBoxKeyAndThePrice()
    {
        var package = Pending().Packages.Single();

        package.SetCarrierQuote(" box-1 ", 35.30m, User, Now);

        package.CarrierBoxKey.Should().Be("box-1");
        package.ShippingCost.Should().Be(35.30m);
        package.TrackingNumber.Should().BeNull();
    }

    [Fact]
    public void Void_TakesTheShipmentBack_AndMarksTheLabelVoided()
    {
        var shipment = Purchased();
        var later = Now.AddHours(2);

        shipment.Void("  wrong address  ", User, later);

        shipment.Status.Should().Be(ShipmentStatus.Voided);
        shipment.LabelState.Should().Be(ShipmentLabelState.Voided);
        shipment.VoidedAt.Should().Be(later);
        shipment.VoidedById.Should().Be(User);
        shipment.VoidReason.Should().Be("wrong address");
        shipment.CountsAsShipped.Should().BeFalse();
        shipment.CarrierCost.Should().Be(18.99m, "what it cost stays on record");
        shipment.Packages.Single().TrackingNumber.Should().Be("1ZV017146460637994", "so a bill for a voided label can be disputed");
    }

    [Fact]
    public void Void_AHandRecordedShipment_HasNoLabelToVoid()
    {
        var shipment = Pending();
        shipment.Packages.Single().SetManualTracking("748912345678", 12m, User, Now);
        shipment.MarkInTransit(12m, User, Now);

        shipment.Void(null, User, Now);

        shipment.Status.Should().Be(ShipmentStatus.Voided);
        shipment.LabelState.Should().Be(ShipmentLabelState.None);
        shipment.VoidReason.Should().BeNull();
    }

    [Fact]
    public void Void_CutsALongReason()
    {
        var shipment = Purchased();

        shipment.Void(new string('x', Shipment.MaxVoidReasonLength + 20), User, Now);

        shipment.VoidReason.Should().HaveLength(Shipment.MaxVoidReasonLength);
    }

    [Fact]
    public void Void_Twice_IsRefused()
    {
        var shipment = Purchased();
        shipment.Void(null, User, Now);

        var act = () => shipment.Void(null, User, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("This shipment is already voided.");
    }

    [Fact]
    public void Void_ADeliveredShipment_IsRefused()
    {
        var shipment = Purchased();
        shipment.MarkDelivered(User, Now);

        var act = () => shipment.Void(null, User, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("A delivered shipment can't be voided.");
    }

    [Fact]
    public void Void_AShipmentThatNeverShipped_IsRefused()
    {
        var act = () => Pending().Void(null, User, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("This shipment was never recorded as shipped, so there is nothing to void.");
    }

    [Fact]
    public void ApplyTracking_EveryBoxDelivered_DeliversTheShipment()
    {
        var shipment = Pending(boxes: 2);
        Start(shipment);
        var boxes = shipment.Packages.OrderBy(p => p.PackageNumber).ToList();
        boxes[0].SetCarrierLabel("1ZV017148167928755", "item-1", User, Now);
        boxes[1].SetCarrierLabel("1ZV017148167928756", "item-2", User, Now);
        shipment.ConfirmLabelPurchase("NUD34772466", User, Now);
        shipment.MarkInTransit(66.88m, User, Now);

        boxes[0].SetTrackingStatus(CarrierTrackingStatus.Delivered).Should().BeTrue();
        boxes[1].SetTrackingStatus(CarrierTrackingStatus.OutForDelivery).Should().BeTrue();
        shipment.ApplyTracking(User, Now.AddDays(2)).Should().BeFalse("one box is still on the truck");
        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.TrackingCheckedAt.Should().Be(Now.AddDays(2));

        boxes[1].SetTrackingStatus(CarrierTrackingStatus.Delivered).Should().BeTrue();
        boxes[1].SetTrackingStatus(CarrierTrackingStatus.Delivered).Should().BeFalse("the same word again is not news");
        shipment.ApplyTracking(User, Now.AddDays(3)).Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        shipment.ApplyTracking(User, Now.AddDays(4)).Should().BeFalse("it is delivered once");
    }

    [Fact]
    public void ApplyTracking_NeverBringsBackAVoidedShipment()
    {
        var shipment = Purchased();
        shipment.Packages.Single().SetTrackingStatus(CarrierTrackingStatus.Delivered);
        shipment.Void(null, User, Now);

        shipment.ApplyTracking(User, Now.AddDays(1)).Should().BeFalse();

        shipment.Status.Should().Be(ShipmentStatus.Voided);
    }

    [Fact]
    public void TrackingEvent_KeepsTheCarriersWordInCapitals()
    {
        var created = TrackingEvent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Now, "Label created", null, null, User, Now, " created ");
        var byHand = TrackingEvent.Create(Guid.NewGuid(), Guid.NewGuid(), Now, "Left the dock", null, null, User, Now);

        created.CarrierCode.Should().Be("CREATED");
        byHand.CarrierCode.Should().BeNull();
    }

    [Fact]
    public void SalesOrder_ReopenAfterShipmentVoid_OnlyMovesAShippedOrder()
    {
        SalesOrder Order() => SalesOrder.CreateOpen(
            Guid.NewGuid(), "SO-T-1", Guid.NewGuid(), null, null, null, Now, null, null, null, null, 0m,
            ShippingAddress.Empty, User, Now);

        var shipped = Order();
        shipped.AdvanceStatus(SalesOrderStatus.Shipped, User, Now);
        var invoiced = Order();
        invoiced.AdvanceStatus(SalesOrderStatus.Invoiced, User, Now);
        var open = Order();

        shipped.ReopenAfterShipmentVoid(User, Now).Should().BeTrue();
        shipped.Status.Should().Be(SalesOrderStatus.InProduction);
        invoiced.ReopenAfterShipmentVoid(User, Now).Should().BeFalse();
        invoiced.Status.Should().Be(SalesOrderStatus.Invoiced);
        open.ReopenAfterShipmentVoid(User, Now).Should().BeFalse();
        open.Status.Should().Be(SalesOrderStatus.Open);
    }

    [Theory]
    [InlineData("FRT", "SMALL PACKAGE FREIGHT", "Freight")]
    [InlineData("fsc", null, "Fuel")]
    [InlineData("RDC", "DELIVERY AREA SURCHARGE", "Delivery area")]
    [InlineData("HRS", null, "Residential")]
    [InlineData("DVS", null, "Declared value")]
    [InlineData("SUR", null, "Surcharge")]
    [InlineData("XYZ", "SOMETHING NEW FROM UPS", "Something new from ups")]
    [InlineData("XYZ", null, "XYZ")]
    public void UpsCharges_Name_SaysTheChargeInShopWords(string code, string? description, string expected) =>
        UpsCharges.Name(code, description).Should().Be(expected);

    [Fact]
    public void CarrierLabelFormats_ListEveryFormatOnce_WithANameForEach()
    {
        CarrierLabelFormats.InPickOrder.Should().BeEquivalentTo(Enum.GetValues<CarrierLabelFormat>());
        CarrierLabelFormats.InPickOrder.Select(CarrierLabelFormats.Short).Should().Equal("4 × 6 PDF", "Letter PDF", "ZPL");
        CarrierLabelFormats.Describe(CarrierLabelFormat.PdfLetter).Should().Be("Letter page PDF (plain paper)");
    }

    [Fact]
    public void CarrierSettings_SetDefaults_KeepsTheLabelFormat_AndLeavesItAloneWhenNotGiven()
    {
        var settings = CarrierSettings.Create(User, Now);
        settings.DefaultLabelFormat.Should().Be(CarrierLabelFormat.Pdf4x6);

        settings.SetDefaults("GND", true, [], User, Now, CarrierLabelFormat.PdfLetter);
        settings.SetDefaults("2DA", true, [], User, Now);

        settings.DefaultLabelFormat.Should().Be(CarrierLabelFormat.PdfLetter);
        var act = () => settings.SetDefaults("GND", true, [], User, Now, (CarrierLabelFormat)99);
        act.Should().Throw<ArgumentException>().WithMessage("Pick one of the label files for the default.");
    }
}
