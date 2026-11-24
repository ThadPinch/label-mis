using System.Net;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Carriers;
using LabelsMis.Infrastructure.Tests.QuickBooks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.Tests.Carriers;

public class WwexCarrierClientTests
{
    private const string TokenJson = """{"access_token":"TOKEN-1","expires_in":86400,"token_type":"Bearer"}""";
    private const string SecondTokenJson = """{"access_token":"TOKEN-2","expires_in":86400,"token_type":"Bearer"}""";

    private const string RatesJson = """
        {"clientStatus":{"success":true,"message":""},"correlationId":"NG-1","response":{"offerList":[{
          "offerId":"offer-1","productTransactionId":"quote-1","totalOfferPrice":{"value":18.99,"unit":"USD"},
          "offeredProductList":[{"offerPrice":{"value":18.99,"unit":"USD"},
            "chargeItemList":[{"customerChargeCode":"FRT","customerDescription":"SMALL PACKAGE FREIGHT","customerPrice":{"value":18.99,"unit":"USD"}}],
            "shopRQShipment":{"timeInTransit":{"upsServiceCode":"GND","serviceDescription":"UPS Ground","transitDays":1,"estimatedDeliveryDate":"2026-11-25","deliveryBy":"23:00:00"},
              "totalBilledWeight":{"value":5,"unit":"LB"},
              "billToInfoList":[{"billToAccountNbr":"V01714","billToPostalCode":"75226","billToCountryCode":"US"}],
              "handlingUnitList":[{"handlingUnitId":"box-1","weight":{"value":5,"unit":"LB"}}]}}]}]}}
        """;

    private const string VoidedJson =
        """{"clientStatus":{"success":true},"response":{"shipmentOrderCancelRS":{"orderCancelStatus":"VOIDED"}}}""";

    private static readonly CarrierCredentials Staging = new(CarrierEnvironment.Staging, "client-id", "client-secret");
    private static readonly CarrierParty Party = new("Co", "Pat", "6025550100", "1 Main St", null, "Phoenix", "AZ", "85001");

    private static readonly CarrierRateRequest RateRequest = new(
        Party, Party, false, new DateOnly(2026, 11, 24), [new CarrierBox(5m, 0m, 0m, 0m, 0m)], "SO-1", null);

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 17, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (WwexCarrierClient Client, StubHandler Handler, ManualTime Time, WwexTokenCache Tokens) Create(int timeoutSeconds = 30)
    {
        var handler = new StubHandler();
        var time = new ManualTime();
        var tokens = new WwexTokenCache();
        var client = new WwexCarrierClient(
            new HttpClient(handler),
            tokens,
            Options.Create(new WwexOptions { TimeoutSeconds = timeoutSeconds }),
            time,
            NullLogger<WwexCarrierClient>.Instance);
        return (client, handler, time, tokens);
    }

    [Fact]
    public async Task GetRatesAsync_SignsInThenAsksStagingWithTheBearerToken()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer(HttpStatusCode.OK, RatesJson);

        var quote = await client.GetRatesAsync(Staging, RateRequest);

        quote.QuoteId.Should().Be("quote-1");
        quote.Rates.Should().ContainSingle().Which.TotalCost.Should().Be(18.99m);
        handler.Sent.Select(s => (s.Method, s.Url, s.Authorization)).Should().Equal(
            (HttpMethod.Post, "https://auth.staging-wwex.com/oauth/token", null),
            (HttpMethod.Post, "https://speedship.staging-wwex.com/svc/shopFlow", "Bearer TOKEN-1"));
        handler.Sent[0].ContentType.Should().Be("application/x-www-form-urlencoded");
        handler.Sent[0].Body.Should().Be(
            "grant_type=client_credentials&client_id=client-id&client_secret=client-secret&audience=staging-wwex-apig");
        handler.Sent[1].ContentType.Should().Be("application/json");
        handler.Sent[1].Body.Should().Contain("\"productType\":\"SMALLPACK\"");
    }

    [Fact]
    public async Task ProductionKeys_GoToTheProductionAddressesWithTheProductionAudience()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer(HttpStatusCode.OK, VoidedJson);

        await client.VoidAsync(Staging with { Environment = CarrierEnvironment.Production }, "quote-1");

        handler.Sent.Select(s => s.Url).Should().Equal(
            "https://auth.wwex.com/oauth/token", "https://www.speedship.com/svc/integratedCancelFlow");
        handler.Sent[0].Body.Should().EndWith("audience=wwex-apig");
    }

    [Fact]
    public async Task ASecondCall_ReusesTheToken()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer(HttpStatusCode.OK, VoidedJson).Answer(HttpStatusCode.OK, VoidedJson);

        await client.VoidAsync(Staging, "quote-1");
        await client.VoidAsync(Staging, "quote-2");

        handler.Sent.Count(s => s.Url.EndsWith("/oauth/token", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task ATokenNearItsEnd_IsReplacedBeforeTheCall()
    {
        var (client, handler, time, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer(HttpStatusCode.OK, VoidedJson)
            .Answer(HttpStatusCode.OK, SecondTokenJson).Answer(HttpStatusCode.OK, VoidedJson);

        await client.VoidAsync(Staging, "quote-1");
        time.Now = time.Now.AddHours(24).AddMinutes(-4);
        await client.VoidAsync(Staging, "quote-2");

        handler.Sent.Select(s => s.Authorization).Should().Equal(null, "Bearer TOKEN-1", null, "Bearer TOKEN-2");
    }

    [Fact]
    public async Task NewKeys_NeverRideOnTheOldToken()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer(HttpStatusCode.OK, VoidedJson)
            .Answer(HttpStatusCode.OK, SecondTokenJson).Answer(HttpStatusCode.OK, VoidedJson);

        await client.VoidAsync(Staging, "quote-1");
        await client.VoidAsync(Staging with { ClientSecret = "rotated" }, "quote-1");

        handler.Sent[3].Authorization.Should().Be("Bearer TOKEN-2");
    }

    [Fact]
    public async Task A401FromTheApi_SignsInAgainAndTriesOnce()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson)
            .Answer(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}""")
            .Answer(HttpStatusCode.OK, SecondTokenJson)
            .Answer(HttpStatusCode.OK, VoidedJson);

        var outcome = await client.VoidAsync(Staging, "quote-1");

        outcome.Should().Be(CarrierVoidOutcome.Voided);
        handler.Sent.Select(s => s.Authorization).Should().Equal(null, "Bearer TOKEN-1", null, "Bearer TOKEN-2");
    }

    [Fact]
    public async Task A401Twice_IsUnauthorized()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson)
            .Answer(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}""")
            .Answer(HttpStatusCode.OK, SecondTokenJson)
            .Answer(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}""");

        var act = () => client.VoidAsync(Staging, "quote-1");

        (await act.Should().ThrowAsync<CarrierException>()).Which.Kind.Should().Be(CarrierErrorKind.Unauthorized);
        handler.Sent.Should().HaveCount(4);
    }

    [Fact]
    public async Task SignInAsync_RefusedKeys_AreUnauthorized()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.Unauthorized, """{"error":"access_denied","error_description":"Unauthorized"}""");

        var act = () => client.SignInAsync(Staging);

        (await act.Should().ThrowAsync<CarrierException>()).Which.Should().BeEquivalentTo(
            new { Kind = CarrierErrorKind.Unauthorized, StatusCode = 401 });
    }

    [Fact]
    public async Task SignInAsync_AlwaysAsksAgain_SoItProvesTheKeysOnFile()
    {
        var (client, handler, time, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer(HttpStatusCode.OK, SecondTokenJson);

        await client.SignInAsync(Staging);
        var second = await client.SignInAsync(Staging);

        handler.Sent.Should().HaveCount(2);
        second.ExpiresAtUtc.Should().Be(time.Now.UtcDateTime.AddDays(1));
    }

    [Theory]
    [InlineData(422, """{"clientStatus":{"message":"weight is required; exception: AppException"}}""", CarrierErrorKind.Refused, "Worldwide Express: weight is required")]
    [InlineData(500, """{"clientStatus":{"message":"<no-description>; exception: SystemException"}}""", CarrierErrorKind.Server, "Worldwide Express had a problem and could not complete the request (answer 500).")]
    [InlineData(502, "<html>Bad gateway</html>", CarrierErrorKind.Server, "Worldwide Express had a problem and could not complete the request (answer 502).")]
    public async Task AnAnswerThatIsNotA2xx_BecomesACarrierException(int status, string body, CarrierErrorKind kind, string message)
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Answer((HttpStatusCode)status, body);

        var act = () => client.GetRatesAsync(Staging, RateRequest);

        var thrown = (await act.Should().ThrowAsync<CarrierException>()).Which;
        thrown.Kind.Should().Be(kind);
        thrown.StatusCode.Should().Be(status);
        thrown.Message.Should().Be(message);
    }

    [Fact]
    public async Task ABrokenConnection_IsNoAnswer_WithTheOutcomeUnknown()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Fail(new HttpRequestException("connection reset"));

        var act = () => client.BookAsync(Staging, new CarrierBookRequest("quote-1", "offer-1", new CarrierBillTo("V01714", "75226")));

        var thrown = (await act.Should().ThrowAsync<CarrierException>()).Which;
        thrown.Kind.Should().Be(CarrierErrorKind.NoAnswer);
        thrown.OutcomeUnknown.Should().BeTrue();
        thrown.Message.Should().Be("Worldwide Express could not be reached.");
    }

    [Fact]
    public async Task AnAnswerThatNeverComes_IsNoAnswer()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson).Fail(new TaskCanceledException("timed out"));

        var act = () => client.BookAsync(Staging, new CarrierBookRequest("quote-1", "offer-1", new CarrierBillTo("V01714", "75226")));

        (await act.Should().ThrowAsync<CarrierException>()).Which.Message.Should().Be("Worldwide Express did not answer in time.");
    }

    [Fact]
    public async Task ACallerWhoGivesUp_IsNotReportedAsTheCarriersSilence()
    {
        var (client, handler, _, tokens) = Create();
        tokens.Store(Staging, new Domain.Carriers.Wwex.WwexToken("TOKEN-1", DateTime.MaxValue, null, null));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        handler.Fail(new OperationCanceledException(cancelled.Token));

        var act = () => client.VoidAsync(Staging, "quote-1", cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetLabelAsync_OneBox_AsksTheLabelFlowForThatTrackingNumber()
    {
        var (client, handler, _, _) = Create();
        var pdf = Convert.ToBase64String("%PDF-1.5 sample"u8.ToArray());
        handler.Answer(HttpStatusCode.OK, TokenJson)
            .Answer(HttpStatusCode.OK, """{"clientStatus":{"success":true},"response":{"reportList":[{"report":{"name":"x","documentBinary":"FILE"}}]}}""".Replace("FILE", pdf));

        var label = await client.GetLabelAsync(Staging, "quote-1", "1ZV017146460637994", CarrierLabelFormat.Pdf4x6);

        label.ContentType.Should().Be("application/pdf");
        handler.Sent[1].Url.Should().EndWith("/svc/generateUPSReportsFromLabelFlow");
        handler.Sent[1].Body.Should().Contain("\"trackingId\":\"1ZV017146460637994\"").And.Contain("\"thermalType\":null");
    }

    [Fact]
    public async Task GetLabelAsync_EveryBox_AsksTheDocumentFlow()
    {
        var (client, handler, _, _) = Create();
        var pdf = Convert.ToBase64String("%PDF-1.4 sample"u8.ToArray());
        handler.Answer(HttpStatusCode.OK, TokenJson)
            .Answer(HttpStatusCode.OK, """{"clientStatus":{"success":true},"response":{"fileContent":"FILE"}}""".Replace("FILE", pdf));

        var label = await client.GetLabelAsync(Staging, "quote-1", null, CarrierLabelFormat.PdfLetter);

        label.Format.Should().Be(CarrierLabelFormat.PdfLetter);
        handler.Sent[1].Url.Should().EndWith("/svc/documentDownloadFlow");
    }

    [Fact]
    public async Task GetLabelAsync_OneBoxWithoutATrackingNumber_IsRefusedBeforeAnyCall()
    {
        var (client, handler, _, _) = Create();

        var act = () => client.GetLabelAsync(Staging, "quote-1", " ", CarrierLabelFormat.Zpl);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TrackAsync_AnUnknownNumber_IsNotFound()
    {
        var (client, handler, _, _) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson)
            .Answer(HttpStatusCode.OK, """{"clientStatus":{"success":true},"response":{"message":"No shipments found for TRACKING_ID"}}""");

        var tracking = await client.TrackAsync(Staging, "1ZV017140000000000");

        tracking.Found.Should().BeFalse();
        handler.Sent[1].Url.Should().EndWith("/svc/searchShipmentsFlow");
    }
}
