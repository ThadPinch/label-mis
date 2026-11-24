using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Carriers;

public class CarrierSettingsTests
{
    private static readonly Guid User = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);

    private static CarrierSettings Saved()
    {
        var settings = CarrierSettings.Create(User, Now);
        settings.SetCredentials(CarrierEnvironment.Staging, " client-id ", "PROTECTED", "cdef", User, Now);
        return settings;
    }

    [Fact]
    public void Create_HasNoKeysAndStartsOnStagingWithGround()
    {
        var settings = CarrierSettings.Create(User, Now);

        settings.Id.Should().Be(CarrierSettings.SingletonId);
        settings.HasCredentials.Should().BeFalse();
        settings.Environment.Should().Be(CarrierEnvironment.Staging);
        settings.DefaultServiceCode.Should().Be("GND");
        settings.TrackingEnabled.Should().BeTrue();
        settings.BoxPresets.Should().BeEmpty();
    }

    [Fact]
    public void SetCredentials_SavesTheKeysTrimmed()
    {
        var settings = Saved();

        settings.HasCredentials.Should().BeTrue();
        settings.ClientId.Should().Be("client-id");
        settings.ClientSecretProtected.Should().Be("PROTECTED");
        settings.ClientSecretTail.Should().Be("cdef");
    }

    [Theory]
    [InlineData("", "PROTECTED", "Client ID is required.")]
    [InlineData("client-id", null, "Client secret is required.")]
    [InlineData("client-id", "  ", "Client secret is required.")]
    public void SetCredentials_TheFirstSaveNeedsBothKeys(string clientId, string? secret, string expected)
    {
        var settings = CarrierSettings.Create(User, Now);

        var act = () => settings.SetCredentials(CarrierEnvironment.Staging, clientId, secret, null, User, Now);

        act.Should().Throw<ArgumentException>().WithMessage(expected);
    }

    [Fact]
    public void SetCredentials_ABlankSecret_KeepsTheOneOnFileAndTheLastTest()
    {
        var settings = Saved();
        settings.RecordTest(true, "Signed in.", Now);
        settings.RecordBillTo(new CarrierBillTo("V01714", "75226"));

        settings.SetCredentials(CarrierEnvironment.Staging, "client-id", null, null, User, Now.AddMinutes(1));

        settings.ClientSecretProtected.Should().Be("PROTECTED");
        settings.ClientSecretTail.Should().Be("cdef");
        settings.LastTestSucceeded.Should().BeTrue();
        settings.BillToAccountNumber.Should().Be("V01714");
    }

    [Theory]
    [InlineData(CarrierEnvironment.Production, "client-id", null)]
    [InlineData(CarrierEnvironment.Staging, "other-client", null)]
    [InlineData(CarrierEnvironment.Staging, "client-id", "NEW-PROTECTED")]
    public void SetCredentials_OtherKeys_ForgetTheTestAndTheAccountItNamed(
        CarrierEnvironment environment, string clientId, string? secret)
    {
        var settings = Saved();
        settings.RecordTest(true, "Signed in.", Now);
        settings.RecordBillTo(new CarrierBillTo("V01714", "75226"));

        settings.SetCredentials(environment, clientId, secret, secret is null ? null : "wxyz", User, Now.AddMinutes(1));

        settings.LastTestedAt.Should().BeNull();
        settings.LastTestSucceeded.Should().BeNull();
        settings.LastTestMessage.Should().BeNull();
        settings.BillToAccountNumber.Should().BeNull();
        settings.BillToPostalCode.Should().BeNull();
    }

    [Fact]
    public void ClearCredentials_RemovesTheKeysAndKeepsTheRest()
    {
        var settings = Saved();
        settings.SetShipFrom("Shipping Desk", "602-555-0100", User, Now);
        settings.RecordTest(true, "Signed in.", Now);

        settings.ClearCredentials(User, Now.AddMinutes(1));

        settings.HasCredentials.Should().BeFalse();
        settings.ClientId.Should().BeEmpty();
        settings.ClientSecretProtected.Should().BeEmpty();
        settings.ClientSecretTail.Should().BeEmpty();
        settings.LastTestedAt.Should().BeNull();
        settings.ShipFromContact.Should().Be("Shipping Desk");
    }

    [Fact]
    public void SetShipFrom_TrimsAndTreatsBlankAsNotSet()
    {
        var settings = Saved();

        settings.SetShipFrom("  Shipping Desk ", "  ", User, Now);

        settings.ShipFromContact.Should().Be("Shipping Desk");
        settings.ShipFromPhone.Should().BeNull();
    }

    [Theory]
    [InlineData("555-0100", "The ship-from phone needs at least 10 digits.")]
    [InlineData("call the front desk", "The ship-from phone needs at least 10 digits.")]
    public void SetShipFrom_APhoneUpsWouldRefuse_IsRefusedHere(string phone, string expected)
    {
        var settings = Saved();

        var act = () => settings.SetShipFrom("Shipping Desk", phone, User, Now);

        act.Should().Throw<ArgumentException>().WithMessage(expected);
    }

    [Fact]
    public void SetShipFrom_AContactTooLongForTheLabel_IsRefused()
    {
        var settings = Saved();

        var act = () => settings.SetShipFrom(new string('x', 61), null, User, Now);

        act.Should().Throw<ArgumentException>().WithMessage("The ship-from contact can be at most 60 characters.");
    }

    [Fact]
    public void SetDefaults_KeepsTheServiceTheSwitchAndTheBoxes()
    {
        var settings = Saved();

        settings.SetDefaults(" 2da ", false, [new CarrierBoxPreset("Small", 12m, 10m, 8m)], User, Now);

        settings.DefaultServiceCode.Should().Be("2DA");
        settings.TrackingEnabled.Should().BeFalse();
        settings.BoxPresets.Should().Equal(new CarrierBoxPreset("Small", 12m, 10m, 8m));
    }

    [Fact]
    public void SetDefaults_AServiceUpsDoesNotSell_IsRefused()
    {
        var settings = Saved();

        var act = () => settings.SetDefaults("FEDEX_GROUND", true, [], User, Now);

        act.Should().Throw<ArgumentException>().WithMessage("Pick one of the UPS services for the default.");
    }

    [Fact]
    public void RecordTest_KeepsTheOutcomeAndCutsALongMessage()
    {
        var settings = Saved();

        settings.RecordTest(false, new string('x', CarrierSettings.MaxMessageLength + 50), Now);

        settings.LastTestedAt.Should().Be(Now);
        settings.LastTestSucceeded.Should().BeFalse();
        settings.LastTestMessage.Should().HaveLength(CarrierSettings.MaxMessageLength);
    }

    [Fact]
    public void RecordBillTo_SaysWhetherTheAccountChanged()
    {
        var settings = Saved();

        settings.RecordBillTo(null).Should().BeFalse();
        settings.RecordBillTo(new CarrierBillTo("V01714", "75226")).Should().BeTrue();
        settings.RecordBillTo(new CarrierBillTo("V01714", "75226")).Should().BeFalse();
        settings.RecordBillTo(new CarrierBillTo("A1B2C3", "85001")).Should().BeTrue();
        settings.BillToAccountNumber.Should().Be("A1B2C3");
        settings.BillToPostalCode.Should().Be("85001");
    }
}

public class CarrierBoxPresetsTests
{
    [Fact]
    public void ToJson_ThenFromJson_ReturnsTheSameBoxes()
    {
        IReadOnlyList<CarrierBoxPreset> boxes = [new("Small", 12m, 10m, 8m), new("Roll box", 14.5m, 12m, 10m)];

        CarrierBoxPresets.FromJson(CarrierBoxPresets.ToJson(boxes)).Should().Equal(boxes);
    }

    [Fact]
    public void ToJson_DropsRowsWithNoName_AndTrimsTheRest()
    {
        var json = CarrierBoxPresets.ToJson([new(" Small ", 12m, 10m, 8m), new("  ", 0m, 0m, 0m), new("", 5m, 5m, 5m)]);

        CarrierBoxPresets.FromJson(json).Should().Equal(new CarrierBoxPreset("Small", 12m, 10m, 8m));
    }

    [Fact]
    public void ToJson_NothingToKeep_IsNull() =>
        CarrierBoxPresets.ToJson([new("", 0m, 0m, 0m)]).Should().BeNull();

    [Theory]
    [InlineData(0, 10, 8)]
    [InlineData(12, 0, 8)]
    [InlineData(12, 10, -1)]
    public void ToJson_ANamedBoxWithoutAllThreeSides_IsRefused(int length, int width, int height)
    {
        var act = () => CarrierBoxPresets.ToJson([new("Small", length, width, height)]);

        act.Should().Throw<ArgumentException>().WithMessage("Box \"Small\" needs a length, a width and a height.");
    }

    [Fact]
    public void ToJson_TwoBoxesWithOneName_AreRefused()
    {
        var act = () => CarrierBoxPresets.ToJson([new("Small", 12m, 10m, 8m), new("small", 6m, 6m, 6m)]);

        act.Should().Throw<ArgumentException>().WithMessage("Two boxes are named \"small\".");
    }

    [Fact]
    public void ToJson_ANameTooLong_IsRefused()
    {
        var act = () => CarrierBoxPresets.ToJson([new(new string('x', CarrierBoxPresets.MaxNameLength + 1), 1m, 1m, 1m)]);

        act.Should().Throw<ArgumentException>().WithMessage("A box name can be at most 40 characters*");
    }

    [Fact]
    public void ToJson_MoreThanTheLimit_IsRefused()
    {
        var boxes = Enumerable.Range(1, CarrierBoxPresets.MaxPresets + 1)
            .Select(i => new CarrierBoxPreset($"Box {i}", 1m, 1m, 1m))
            .ToList();

        var act = () => CarrierBoxPresets.ToJson(boxes);

        act.Should().Throw<ArgumentException>().WithMessage("At most 20 box presets can be kept.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"name\":\"an object, not a list\"}")]
    public void FromJson_AnythingUnreadable_IsNoBoxes(string? json) =>
        CarrierBoxPresets.FromJson(json).Should().BeEmpty();

    [Fact]
    public void Label_ShowsTheNameAndTheSize() =>
        new CarrierBoxPreset("Roll box", 14.5m, 12m, 10m).Label.Should().Be("Roll box (14.5 × 12 × 10 in)");
}

public class CarrierLogTests
{
    private static readonly Guid User = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_KeepsWhatHappenedWhenAndByWhom()
    {
        var shipment = Guid.NewGuid();

        var entry = CarrierLog.Create(
            Guid.NewGuid(), CarrierOperations.Book, true, User, false, CarrierEnvironment.Production, Now,
            shipmentId: shipment, detail: " UPS Ground $18.99 ", correlationId: "NG-1", durationMs: 412);

        entry.Operation.Should().Be("book");
        entry.Succeeded.Should().BeTrue();
        entry.Automatic.Should().BeFalse();
        entry.Environment.Should().Be(CarrierEnvironment.Production);
        entry.ShipmentId.Should().Be(shipment);
        entry.Detail.Should().Be("UPS Ground $18.99");
        entry.CorrelationId.Should().Be("NG-1");
        entry.DurationMs.Should().Be(412);
        entry.CreatedAt.Should().Be(Now);
        entry.CreatedById.Should().Be(User);
    }

    [Fact]
    public void Create_CutsTextToWhatTheColumnsHold_AndBlankIsNothing()
    {
        var entry = CarrierLog.Create(
            Guid.NewGuid(), new string('o', 60), false, User, true, CarrierEnvironment.Staging, Now,
            detail: new string('d', 400), errorKind: "  ", message: new string('m', 1200), correlationId: new string('c', 150));

        entry.Operation.Should().HaveLength(CarrierLog.MaxOperationLength);
        entry.Detail.Should().HaveLength(CarrierLog.MaxDetailLength);
        entry.Message.Should().HaveLength(CarrierLog.MaxMessageLength);
        entry.CorrelationId.Should().HaveLength(100);
        entry.ErrorKind.Should().BeNull();
    }

    [Fact]
    public void Create_WithNoOperation_IsRefused()
    {
        var act = () => CarrierLog.Create(Guid.NewGuid(), " ", true, User, false, CarrierEnvironment.Staging, Now);

        act.Should().Throw<ArgumentException>().WithMessage("A log entry needs an operation.");
    }

    [Theory]
    [InlineData(CarrierOperations.SignIn, "Test the connection")]
    [InlineData(CarrierOperations.Rates, "Get rates")]
    [InlineData(CarrierOperations.Book, "Buy the label")]
    [InlineData(CarrierOperations.BookCheck, "Check an unanswered purchase")]
    [InlineData(CarrierOperations.Label, "Fetch a label file")]
    [InlineData(CarrierOperations.Void, "Void the label")]
    [InlineData(CarrierOperations.Track, "Check tracking")]
    [InlineData("something.new", "something.new")]
    public void Operations_Describe_SaysItInShopWords(string operation, string expected) =>
        CarrierOperations.Describe(operation).Should().Be(expected);
}
