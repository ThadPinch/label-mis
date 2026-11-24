using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Carriers;
using Microsoft.Extensions.DependencyInjection;

namespace LabelsMis.Web.Tests.Carriers;

[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierSettingsServiceTests : CarrierTestHost
{
    [Fact]
    public async Task GetViewAsync_WithNothingSaved_IsNotSetUp_AndStillKnowsTheShipFrom()
    {
        var view = await WithSettingsAsync(s => s.GetViewAsync());

        view.HasCredentials.Should().BeFalse();
        view.IsReady.Should().BeFalse();
        view.CanStoreSecrets.Should().BeTrue();
        view.DefaultServiceCode.Should().Be("GND");
        view.ShipFrom.Should().Be(new CarrierParty(
            "Hey Lucky Label Co.", null, "(602) 555-0100", "100 W Main St", "Suite 4", "Phoenix", "AZ", "85001"));
    }

    [Fact]
    public async Task SaveCredentialsAsync_StoresTheSecretEncrypted_AndShowsOnlyItsTail()
    {
        await SaveKeysAsync();

        var row = await ReadSettingsAsync();
        row!.ClientId.Should().Be(ClientId);
        row.ClientSecretProtected.Should().NotBeNullOrWhiteSpace().And.NotContain(ClientSecret);
        row.ClientSecretTail.Should().Be("9f3c");

        var view = await WithSettingsAsync(s => s.GetViewAsync());
        view.IsReady.Should().BeTrue();
        view.IsStaging.Should().BeTrue();
        view.ClientSecretTail.Should().Be("9f3c");
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsTheKeysInTheClear()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);

        var credentials = await WithSettingsAsync(s => s.GetCredentialsAsync());

        credentials.Should().Be(new CarrierCredentials(CarrierEnvironment.Production, ClientId, ClientSecret));
    }

    [Fact]
    public async Task GetCredentialsAsync_WithNoKeys_SaysWhereToAddThem()
    {
        var act = () => WithSettingsAsync(s => s.GetCredentialsAsync());

        (await act.Should().ThrowAsync<CarrierNotConfiguredException>())
            .WithMessage("No carrier keys are saved. An admin adds them in Setup → Shipping carrier.");
    }

    [Fact]
    public async Task GetCredentialsAsync_ASecretSavedUnderAnotherKey_AsksForItToBeSavedAgain()
    {
        await SaveKeysAsync();
        await InDbAsync(async db =>
        {
            var row = db.CarrierSettings.Single();
            row.SetCredentials(CarrierEnvironment.Staging, ClientId, "bm90IGEgcmVhbCBjaXBoZXJ0ZXh0", "9f3c", TestUserId, Now);
            await db.SaveChangesAsync();
        });

        var act = () => WithSettingsAsync(s => s.GetCredentialsAsync());

        (await act.Should().ThrowAsync<CarrierNotConfiguredException>())
            .WithMessage("The saved carrier secret can't be read*Save it again in Setup → Shipping carrier.");
    }

    [Fact]
    public async Task SaveCredentialsAsync_ABlankSecret_KeepsTheSavedOne()
    {
        await SaveKeysAsync();

        await WithSettingsAsync(s => s.SaveCredentialsAsync(CarrierEnvironment.Staging, ClientId, "  "));

        (await WithSettingsAsync(s => s.GetCredentialsAsync())).ClientSecret.Should().Be(ClientSecret);
    }

    [Fact]
    public async Task RemoveCredentialsAsync_GoesBackToNotSetUp_AndKeepsTheDefaults()
    {
        await SaveKeysAsync();
        await WithSettingsAsync(s => s.SaveDefaultsAsync("2DA", false, [new CarrierBoxPreset("Small", 12m, 10m, 8m)]));

        await WithSettingsAsync(s => s.RemoveCredentialsAsync());

        var view = await WithSettingsAsync(s => s.GetViewAsync());
        view.HasCredentials.Should().BeFalse();
        view.DefaultServiceCode.Should().Be("2DA");
        view.BoxPresets.Should().ContainSingle().Which.Name.Should().Be("Small");
    }

    [Fact]
    public async Task SaveShipFromAsync_PutsTheContactAndPhoneOnTheShipFrom()
    {
        await WithSettingsAsync(s => s.SaveShipFromAsync("Shipping Desk", "602-555-0199"));

        var view = await WithSettingsAsync(s => s.GetViewAsync());

        view.ShipFrom.Contact.Should().Be("Shipping Desk");
        view.ShipFrom.Phone.Should().Be("602-555-0199", "the carrier phone wins over the company phone");
        view.ShipFrom.Street1.Should().Be("100 W Main St");
    }

    [Fact]
    public async Task TrackingEnabledAsync_NeedsBothTheSwitchAndTheKeys()
    {
        (await WithSettingsAsync(s => s.TrackingEnabledAsync())).Should().BeFalse("nothing is saved");

        await SaveKeysAsync();
        (await WithSettingsAsync(s => s.TrackingEnabledAsync())).Should().BeTrue();

        await WithSettingsAsync(s => s.SaveDefaultsAsync("GND", false, []));
        (await WithSettingsAsync(s => s.TrackingEnabledAsync())).Should().BeFalse("the switch is off");

        await WithSettingsAsync(s => s.SaveDefaultsAsync("GND", true, []));
        await WithSettingsAsync(s => s.RemoveCredentialsAsync());
        (await WithSettingsAsync(s => s.TrackingEnabledAsync())).Should().BeFalse("the keys are gone");
    }
}

[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierGatewayTests : CarrierTestHost
{
    private static readonly CarrierParty Customer = new(
        "Desert Bloom Coffee", "Pat", "480-555-0142", "2200 E Camelback Rd", null, "Phoenix", "AZ", "85016");

    private CarrierRateRequest Request(params decimal[] weights) => new(
        ShopAddress.ToParty(), Customer, false, new DateOnly(2026, 11, 24),
        weights.Select(w => new CarrierBox(w, 0m, 0m, 0m, 0m)).ToList(), "SO-1", null);

    [Fact]
    public async Task GetRatesAsync_UsesTheSavedKeys_AndLogsWhatCameBack()
    {
        await SaveKeysAsync();
        var order = Guid.NewGuid();

        var quote = await WithGatewayAsync(g => g.GetRatesAsync(Request(5m, 7.5m), g.CallerFor(salesOrderId: order)));

        quote.Rates.Should().HaveCount(3);
        Carrier.Calls.Should().ContainSingle().Which.Credentials.Should().Be(
            new CarrierCredentials(CarrierEnvironment.Staging, ClientId, ClientSecret));
        var entry = (await ReadLogAsync()).Should().ContainSingle().Subject;
        entry.Operation.Should().Be(CarrierOperations.Rates);
        entry.Succeeded.Should().BeTrue();
        entry.Automatic.Should().BeFalse();
        entry.CreatedById.Should().Be(TestUserId);
        entry.SalesOrderId.Should().Be(order);
        entry.Environment.Should().Be(CarrierEnvironment.Staging);
        entry.Detail.Should().Be("2 boxes, 12.5 lb to 85016: 3 services, from UPS Ground $32.50");
        entry.CorrelationId.Should().Be("NG-rates-1");
        entry.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task ARefusal_IsLoggedWithTheCarriersWordsAndCallId_AndStillThrown()
    {
        await SaveKeysAsync();
        Carrier.RatesScript.Enqueue(_ => throw new CarrierException(
            CarrierErrorKind.Refused, "Worldwide Express: Weight of the package is more than 150", 422, "NG-refused"));

        var act = () => WithGatewayAsync(g => g.GetRatesAsync(Request(200m), g.CallerFor()));

        await act.Should().ThrowAsync<CarrierException>();
        var entry = (await ReadLogAsync()).Should().ContainSingle().Subject;
        entry.Succeeded.Should().BeFalse();
        entry.HttpStatus.Should().Be(422);
        entry.ErrorKind.Should().Be("Refused");
        entry.Message.Should().Be("Worldwide Express: Weight of the package is more than 150");
        entry.CorrelationId.Should().Be("NG-refused");
        entry.Detail.Should().Be("1 box, 200 lb to 85016");
    }

    [Fact]
    public async Task WithNoKeys_NothingIsAskedOfTheCarrier_AndTheAttemptIsLogged()
    {
        var act = () => WithGatewayAsync(g => g.GetRatesAsync(Request(5m), g.CallerFor()));

        await act.Should().ThrowAsync<CarrierNotConfiguredException>();
        Carrier.Calls.Should().BeEmpty();
        var entry = (await ReadLogAsync()).Should().ContainSingle().Subject;
        entry.Succeeded.Should().BeFalse();
        entry.ErrorKind.Should().Be("NotConfigured");
    }

    [Fact]
    public async Task ALabelBoughtOnStaging_IsNotAskedAboutWithProductionKeys()
    {
        await SaveKeysAsync(CarrierEnvironment.Production);

        var act = () => WithGatewayAsync(g => g.VoidAsync(CarrierEnvironment.Staging, "quote-1", g.CallerFor()));

        (await act.Should().ThrowAsync<CarrierNotConfiguredException>()).WithMessage(
            "This label was bought with the carrier's staging keys, and the keys saved now are for production. "
            + "It can only be handled with the keys it was bought with.");
        Carrier.Calls.Should().BeEmpty();
        (await ReadLogAsync()).Should().ContainSingle().Which.Environment.Should().Be(CarrierEnvironment.Staging);
    }

    [Fact]
    public async Task BackgroundWork_IsLoggedAsAutomaticUnderTheSystemUser()
    {
        await SaveKeysAsync();
        var shipment = Guid.NewGuid();

        await WithGatewayAsync(g => g.TrackAsync(
            CarrierEnvironment.Staging, "1ZTEST000000000001", CarrierCaller.Background(shipment)));

        var entry = (await ReadLogAsync()).Should().ContainSingle().Subject;
        entry.Automatic.Should().BeTrue();
        entry.ShipmentId.Should().Be(shipment);
        entry.CreatedById.Should().Be(LabelsMis.Domain.Common.TenantConstants.SystemUserId);
        entry.Detail.Should().Be("1ZTEST000000000001: The carrier does not know this tracking number");
    }

    [Fact]
    public async Task GetRecentAsync_ListsNewestFirstWithWhoAndFiltersByShipment()
    {
        await SaveKeysAsync();
        var shipment = Guid.NewGuid();
        await WithGatewayAsync(g => g.GetRatesAsync(Request(5m), g.CallerFor()));
        Time.Advance(TimeSpan.FromMinutes(1));
        await WithGatewayAsync(g => g.TrackAsync(CarrierEnvironment.Staging, "1ZTEST000000000001", g.CallerFor(shipmentId: shipment)));

        var logger = Services.GetRequiredService<CarrierLogger>();
        var all = await logger.GetRecentAsync(10);
        var one = await logger.GetRecentAsync(10, shipment);

        all.Select(r => r.Operation).Should().Equal(CarrierOperations.Track, CarrierOperations.Rates);
        all[0].Who.Should().Be("test@labels-mis.local");
        one.Should().ContainSingle().Which.Operation.Should().Be(CarrierOperations.Track);
    }
}

[Collection(CarrierSettingsRowCollection.Name)]
public class CarrierConnectionTestTests : CarrierTestHost
{
    private Task<CarrierTestResult> RunAsync() =>
        InRequestAsync(services => services.GetRequiredService<CarrierConnectionTest>().RunAsync());

    [Fact]
    public async Task RunAsync_SignsInRatesATestBoxAndRemembersTheAccountBilled()
    {
        await SaveKeysAsync();

        var result = await RunAsync();

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be(
            "Signed in to Worldwide Express as account W0000000000 (plan small). "
            + "A 1 lb test box from the ship-from address came back with 3 UPS services (from UPS Ground at $11.00). "
            + "Labels bill UPS account V01714 (ZIP 75226).");
        Carrier.Calls.Select(c => c.Operation).Should().Equal(CarrierOperations.SignIn, CarrierOperations.Rates);
        var row = await ReadSettingsAsync();
        row!.LastTestSucceeded.Should().BeTrue();
        row.LastTestedAt.Should().Be(Now);
        row.LastTestMessage.Should().Be(result.Message);
        row.BillToAccountNumber.Should().Be("V01714");
        row.BillToPostalCode.Should().Be("75226");
        (await ReadLogAsync()).Select(l => l.Operation).Should().BeEquivalentTo(CarrierOperations.SignIn, CarrierOperations.Rates);
    }

    [Fact]
    public async Task RunAsync_RefusedKeys_FailWithTheCarriersWords()
    {
        await SaveKeysAsync();
        Carrier.SignInScript.Enqueue(() => throw new CarrierException(
            CarrierErrorKind.Unauthorized, "Worldwide Express refused the client ID and secret.", 401));

        var result = await RunAsync();

        result.Should().Be(new CarrierTestResult(false, "Worldwide Express refused the client ID and secret."));
        var row = await ReadSettingsAsync();
        row!.LastTestSucceeded.Should().BeFalse();
        row.LastTestMessage.Should().Be("Worldwide Express refused the client ID and secret.");
        Carrier.CountOf(CarrierOperations.Rates).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WithAnIncompleteCompanyAddress_SignsInAndSaysWhyNoRateWasAsked()
    {
        await SaveKeysAsync();
        await SetCompanyAddressAsync(ShopAddress with { Line1 = null, Zip = null });

        var result = await RunAsync();

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be(
            "Signed in to Worldwide Express as account W0000000000 (plan small). No test rate was asked for: "
            + "Ship-from needs a street address, a ZIP code. Set it in Settings → General and Setup → Shipping carrier. "
            + "Ship-to needs a street address, a ZIP code.");
        Carrier.CountOf(CarrierOperations.Rates).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ARateThatFailsAfterSignIn_IsAFailedTest()
    {
        await SaveKeysAsync();
        Carrier.RatesScript.Enqueue(_ => throw new CarrierException(
            CarrierErrorKind.Server, "Worldwide Express had a problem and could not complete the request (answer 500).", 500));

        var result = await RunAsync();

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("Worldwide Express had a problem and could not complete the request (answer 500).");
    }

    [Fact]
    public async Task RunAsync_WithNoKeys_SaysSo()
    {
        var result = await RunAsync();

        result.Should().Be(new CarrierTestResult(false, CarrierSettingsService.NotConfiguredMessage));
    }
}
