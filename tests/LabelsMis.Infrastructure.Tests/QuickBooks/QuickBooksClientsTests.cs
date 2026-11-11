using System.Net;
using System.Text;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Infrastructure.QuickBooks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.Tests.QuickBooks;

/// <summary>Answers from a queue and keeps what it was sent.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();

    public List<(HttpMethod Method, string Url, string? Authorization, string? Body, string? ContentType, string? HardExpiryHeader)> Sent { get; } = [];

    public StubHandler Answer(HttpStatusCode status, string body, string? intuitTid = "tid-1")
    {
        _answers.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (intuitTid is not null)
            {
                response.Headers.TryAddWithoutValidation("intuit_tid", intuitTid);
            }

            return response;
        });
        return this;
    }

    public StubHandler Fail(Exception exception)
    {
        _answers.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent.Add((
            request.Method,
            request.RequestUri!.AbsoluteUri,
            request.Headers.Authorization?.ToString(),
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Content?.Headers.ContentType?.MediaType,
            request.Headers.TryGetValues("x-include-refresh-token-hard-expires-in", out var v) ? v.First() : null));
        return _answers.Dequeue()(request);
    }
}

public class IntuitAuthClientTests
{
    private const string TokenJson = """{"token_type":"bearer","expires_in":3600,"refresh_token":"RT1","x_refresh_token_expires_in":8726400,"access_token":"AT1"}""";
    private static readonly QuickBooksAppKeys Keys = new("client-id", "client-secret");

    private static (IntuitAuthClient Client, StubHandler Handler) Create()
    {
        var handler = new StubHandler();
        return (new IntuitAuthClient(new HttpClient(handler), Options.Create(new QuickBooksOptions())), handler);
    }

    [Fact]
    public void BuildAuthorizeUrl_AsksForTheAccountingScopeAndEscapesEverything()
    {
        var (client, _) = Create();

        var url = client.BuildAuthorizeUrl("client id", "http://localhost:5000/quickbooks/callback", "s+t/a=te");

        url.Should().Be(
            "https://appcenter.intuit.com/connect/oauth2?client_id=client%20id&response_type=code"
            + "&scope=com.intuit.quickbooks.accounting"
            + "&redirect_uri=http%3A%2F%2Flocalhost%3A5000%2Fquickbooks%2Fcallback&state=s%2Bt%2Fa%3Dte");
    }

    [Fact]
    public async Task ExchangeCodeAsync_PostsTheCodeWithBasicAuthAndReadsTheTokens()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson, "tid-7");

        var tokens = await client.ExchangeCodeAsync(Keys, "the-code", "http://localhost:5000/quickbooks/callback");

        tokens.Should().Be(new QuickBooksTokenResponse("AT1", 3600, "RT1", 8726400, null, "tid-7"));
        var sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Should().Satisfy<(HttpMethod Method, string Url, string? Authorization, string? Body, string? ContentType, string? HardExpiryHeader)>(s =>
        {
            s.Method.Should().Be(HttpMethod.Post);
            s.Url.Should().Be("https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer");
            s.Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-id:client-secret")));
            s.Body.Should().Be("grant_type=authorization_code&code=the-code&redirect_uri=http%3A%2F%2Flocalhost%3A5000%2Fquickbooks%2Fcallback");
            s.ContentType.Should().Be("application/x-www-form-urlencoded");
            s.HardExpiryHeader.Should().Be("true");
        });
    }

    [Fact]
    public async Task RefreshAsync_PostsTheRefreshToken()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, TokenJson);

        await client.RefreshAsync(Keys, "RT0");

        handler.Sent.Single().Body.Should().Be("grant_type=refresh_token&refresh_token=RT0");
    }

    [Fact]
    public async Task RefreshAsync_WhenIntuitRefusesTheToken_ThrowsInvalidGrant()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Incorrect Token type or clientID"}""");

        var act = () => client.RefreshAsync(Keys, "RT0");

        (await act.Should().ThrowAsync<QuickBooksAuthException>()).Which.IsInvalidGrant.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_WhenIntuitCannotBeReached_ThrowsATransientError()
    {
        var (client, handler) = Create();
        handler.Fail(new HttpRequestException("connection refused"));

        var act = () => client.RefreshAsync(Keys, "RT0");

        var ex = (await act.Should().ThrowAsync<QuickBooksAuthException>()).Which;
        ex.IsTransient.Should().BeTrue();
        ex.IsInvalidGrant.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_PostsTheTokenAsJson()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, "");

        await client.RevokeAsync(Keys, "RT1");

        var sent = handler.Sent.Single();
        sent.Url.Should().Be("https://developer.api.intuit.com/v2/oauth2/tokens/revoke");
        sent.Body.Should().Be("""{"token":"RT1"}""");
        sent.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task RevokeAsync_WhenRefused_Throws()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.BadRequest, """{"error":"invalid_token"}""");

        var act = () => client.RevokeAsync(Keys, "RT1");

        await act.Should().ThrowAsync<QuickBooksAuthException>();
    }
}

public class QuickBooksApiClientTests
{
    private static readonly QuickBooksCompanyAccess Sandbox = new(QuickBooksEnvironment.Sandbox, "9341", "access-token");

    private static (QuickBooksApiClient Client, StubHandler Handler) Create()
    {
        var handler = new StubHandler();
        var options = Options.Create(new QuickBooksOptions { ReadRetryDelaysMs = [0, 0] });
        return (new QuickBooksApiClient(new HttpClient(handler), options, NullLogger<QuickBooksApiClient>.Instance), handler);
    }

    [Fact]
    public async Task ReadAsync_CallsTheSandboxWithTheMinorVersionAndBearerToken()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, """{"CompanyInfo":{"CompanyName":"Sandbox Company"}}""", "tid-3");

        var response = await client.ReadAsync(Sandbox, "companyinfo/9341");

        response.Body.Should().Contain("Sandbox Company");
        response.IntuitTid.Should().Be("tid-3");
        var sent = handler.Sent.Single();
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Url.Should().Be("https://sandbox-quickbooks.api.intuit.com/v3/company/9341/companyinfo/9341?minorversion=75");
        sent.Authorization.Should().Be("Bearer access-token");
    }

    [Fact]
    public async Task ReadAsync_ForProductionKeys_CallsTheProductionHost()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, "{}");

        await client.ReadAsync(Sandbox with { Environment = QuickBooksEnvironment.Production }, "preferences");

        handler.Sent.Single().Url.Should().StartWith("https://quickbooks.api.intuit.com/v3/company/9341/preferences?");
    }

    [Fact]
    public async Task QueryAsync_EscapesTheQuery()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, """{"QueryResponse":{}}""");

        await client.QueryAsync(Sandbox, "select Id from Invoice where DocNumber = 'INV-2026-00123'");

        handler.Sent.Single().Url.Should().Be(
            "https://sandbox-quickbooks.api.intuit.com/v3/company/9341/query?minorversion=75"
            + "&query=select%20Id%20from%20Invoice%20where%20DocNumber%20%3D%20%27INV-2026-00123%27");
    }

    [Fact]
    public async Task ReadAsync_On401_ThrowsUnauthorizedWithoutRetrying()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.Unauthorized, """{"fault":{"error":[{"message":"AuthenticationFailed","detail":"Token expired","code":"3200"}],"type":"AUTHENTICATION"}}""");

        var act = () => client.ReadAsync(Sandbox, "preferences");

        (await act.Should().ThrowAsync<QuickBooksApiException>()).Which.Kind.Should().Be(QuickBooksErrorKind.Unauthorized);
        handler.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadAsync_On429ThenOk_TriesAgainAndSucceeds()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.TooManyRequests, """{"Fault":{"Error":[{"Message":"ThrottleExceeded","code":"3001"}]}}""")
               .Answer(HttpStatusCode.OK, """{"Preferences":{}}""");

        var response = await client.ReadAsync(Sandbox, "preferences");

        response.StatusCode.Should().Be(200);
        handler.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadAsync_WhenItKeepsFailing_GivesUpAfterTheConfiguredRetries()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.ServiceUnavailable, "").Answer(HttpStatusCode.ServiceUnavailable, "").Answer(HttpStatusCode.ServiceUnavailable, "");

        var act = () => client.ReadAsync(Sandbox, "preferences");

        (await act.Should().ThrowAsync<QuickBooksApiException>()).Which.Kind.Should().Be(QuickBooksErrorKind.Server);
        handler.Sent.Should().HaveCount(3);
    }

    [Fact]
    public async Task ReadAsync_WhenTheNetworkFailsOnce_TriesAgain()
    {
        var (client, handler) = Create();
        handler.Fail(new HttpRequestException("reset")).Answer(HttpStatusCode.OK, "{}");

        (await client.ReadAsync(Sandbox, "preferences")).StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task WriteAsync_PostsJsonWithTheRequestId()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, """{"Invoice":{"Id":"145","SyncToken":"0"}}""");

        await client.WriteAsync(Sandbox, "invoice", """{"DocNumber":"INV-1"}""", requestId: "inv-1-create");

        var sent = handler.Sent.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Url.Should().Be("https://sandbox-quickbooks.api.intuit.com/v3/company/9341/invoice?minorversion=75&requestid=inv-1-create");
        sent.Body.Should().Be("""{"DocNumber":"INV-1"}""");
        sent.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task WriteAsync_WithoutABody_SendsOctetStreamAndExtraParameters()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.OK, """{"Invoice":{"Id":"145"}}""");

        await client.WriteAsync(Sandbox, "invoice/145/send", null, parameters: new Dictionary<string, string> { ["sendTo"] = "ap@example.com" });

        var sent = handler.Sent.Single();
        sent.Url.Should().EndWith("/invoice/145/send?minorversion=75&sendTo=ap%40example.com");
        sent.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task WriteAsync_OnAServerError_DoesNotRetry()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.InternalServerError, "");

        var act = () => client.WriteAsync(Sandbox, "invoice", "{}", requestId: "r1");

        await act.Should().ThrowAsync<QuickBooksApiException>();
        handler.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteAsync_OnAValidationFault_CarriesTheCode()
    {
        var (client, handler) = Create();
        handler.Answer(HttpStatusCode.BadRequest, """{"Fault":{"Error":[{"Message":"Duplicate Document Number Error","Detail":"DocNumber=INV-1","code":"6140"}],"type":"ValidationFault"}}""");

        var act = () => client.WriteAsync(Sandbox, "invoice", "{}", requestId: "r1");

        var ex = (await act.Should().ThrowAsync<QuickBooksApiException>()).Which;
        ex.Code.Should().Be("6140");
        ex.IntuitTid.Should().Be("tid-1");
    }
}
