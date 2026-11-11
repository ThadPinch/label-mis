using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksApiExceptionTests
{
    private const string DuplicateNumberFault = """
        {"Fault":{"Error":[{"Message":"Duplicate Document Number Error","Detail":"Duplicate Document Number Error : You must specify a different number. This number has already been used. DocNumber=INV-2026-00123 is assigned to TxnType=Invoice with TxnId=145","code":"6140","element":""}],"type":"ValidationFault"},"time":"2026-11-10T12:00:00.000-08:00"}
        """;

    private const string AuthenticationFault = """
        {"warnings":null,"intuitObject":null,"fault":{"error":[{"message":"message=AuthenticationFailed; errorCode=003200; statusCode=401","detail":"Token expired: AB01...","code":"3200","element":null}],"type":"AUTHENTICATION"},"report":null,"requestId":null,"time":1794300000000}
        """;

    private const string ThrottleFault = """
        {"Fault":{"Error":[{"Message":"message=ThrottleExceeded; errorCode=003001; statusCode=429","Detail":"Too many requests","code":"3001"}],"type":"SERVICE"}}
        """;

    [Fact]
    public void FromResponse_ValidationFault_CarriesTheCodeAndIsNotTransient()
    {
        var ex = QuickBooksApiException.FromResponse(400, DuplicateNumberFault, "tid-1");

        ex.Should().Satisfy<QuickBooksApiException>(e =>
        {
            e.Kind.Should().Be(QuickBooksErrorKind.Validation);
            e.StatusCode.Should().Be(400);
            e.Code.Should().Be("6140");
            e.IntuitTid.Should().Be("tid-1");
            e.IsTransient.Should().BeFalse();
            e.Message.Should().StartWith("Duplicate Document Number Error: ").And.Contain("INV-2026-00123");
        });
    }

    [Fact]
    public void FromResponse_401WithLowercaseFault_IsUnauthorized()
    {
        var ex = QuickBooksApiException.FromResponse(401, AuthenticationFault, null);

        ex.Should().Satisfy<QuickBooksApiException>(e =>
        {
            e.Kind.Should().Be(QuickBooksErrorKind.Unauthorized);
            e.Code.Should().Be("3200");
            e.IsTransient.Should().BeFalse();
            e.Errors.Should().ContainSingle();
        });
    }

    [Fact]
    public void FromResponse_429_IsThrottledAndTransient()
    {
        var ex = QuickBooksApiException.FromResponse(429, ThrottleFault, null);

        ex.Kind.Should().Be(QuickBooksErrorKind.Throttled);
        ex.IsTransient.Should().BeTrue();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public void FromResponse_5xx_IsServerAndTransient(int status)
    {
        var ex = QuickBooksApiException.FromResponse(status, "<html>Service Unavailable</html>", null);

        ex.Should().Satisfy<QuickBooksApiException>(e =>
        {
            e.Kind.Should().Be(QuickBooksErrorKind.Server);
            e.IsTransient.Should().BeTrue();
            e.Errors.Should().BeEmpty();
            e.Message.Should().Be($"QuickBooks answered {status}.");
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Fault\":{}}")]
    [InlineData("[1,2,3]")]
    public void FromResponse_WithNoReadableFault_StillSaysWhatHappened(string? body)
    {
        var ex = QuickBooksApiException.FromResponse(400, body, null);

        ex.Message.Should().Be("QuickBooks answered 400.");
        ex.Code.Should().BeNull();
    }

    [Fact]
    public void FromResponse_403_IsOtherAndNotTransient()
    {
        var ex = QuickBooksApiException.FromResponse(403, null, null);

        ex.Kind.Should().Be(QuickBooksErrorKind.Other);
        ex.IsTransient.Should().BeFalse();
    }

    [Fact]
    public void FromResponse_WhenDetailRepeatsTheMessage_SaysItOnce()
    {
        var body = """{"Fault":{"Error":[{"Message":"Object Not Found","Detail":"Object Not Found","code":"610"}]}}""";

        QuickBooksApiException.FromResponse(400, body, null).Message.Should().Be("Object Not Found");
    }

    [Fact]
    public void Network_IsTransientWithNoStatus()
    {
        var ex = QuickBooksApiException.Network("QuickBooks did not answer.", new TimeoutException());

        ex.Kind.Should().Be(QuickBooksErrorKind.Network);
        ex.StatusCode.Should().BeNull();
        ex.IsTransient.Should().BeTrue();
    }
}

public class QuickBooksAuthExceptionTests
{
    [Fact]
    public void FromResponse_InvalidGrant_IsRecognised()
    {
        var ex = QuickBooksAuthException.FromResponse(400, """{"error":"invalid_grant","error_description":"Incorrect Token type or clientID"}""", "tid-9");

        ex.Should().Satisfy<QuickBooksAuthException>(e =>
        {
            e.IsInvalidGrant.Should().BeTrue();
            e.IsInvalidClient.Should().BeFalse();
            e.IsTransient.Should().BeFalse();
            e.IntuitTid.Should().Be("tid-9");
            e.Message.Should().Be("QuickBooks sign-in refused: invalid_grant (Incorrect Token type or clientID).");
        });
    }

    [Fact]
    public void FromResponse_InvalidClient_IsRecognised()
    {
        var ex = QuickBooksAuthException.FromResponse(401, """{"error":"invalid_client"}""", null);

        ex.IsInvalidClient.Should().BeTrue();
        ex.Message.Should().Be("QuickBooks sign-in refused: invalid_client.");
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(400, false)]
    public void FromResponse_TransientOnlyForServerSideTrouble(int status, bool transient)
    {
        QuickBooksAuthException.FromResponse(status, "<html>oops</html>", null).IsTransient.Should().Be(transient);
    }

    [Fact]
    public void FromResponse_WithAnHtmlBody_FallsBackToTheStatus()
    {
        QuickBooksAuthException.FromResponse(502, "<html>Bad Gateway</html>", null)
            .Message.Should().Be("QuickBooks sign-in answered 502.");
    }
}

public class QuickBooksTokenResponseTests
{
    [Fact]
    public void Parse_ReadsBothTokensAndTheirLifetimes()
    {
        var json = """{"token_type":"bearer","expires_in":3600,"refresh_token":"RT1","x_refresh_token_expires_in":8726400,"access_token":"AT1"}""";

        QuickBooksTokenResponse.Parse(json, "tid").Should().Be(
            new QuickBooksTokenResponse("AT1", 3600, "RT1", 8726400, null, "tid"));
    }

    [Fact]
    public void Parse_ReadsTheHardExpiryWhenIntuitSendsIt()
    {
        var json = """{"expires_in":3600,"refresh_token":"RT1","x_refresh_token_expires_in":8726400,"x_refresh_token_hard_expires_in":157680000,"access_token":"AT1"}""";

        QuickBooksTokenResponse.Parse(json, null).RefreshTokenHardExpiresInSeconds.Should().Be(157680000);
    }

    [Theory]
    [InlineData("""{"expires_in":3600,"x_refresh_token_expires_in":8726400,"access_token":"AT1"}""")]
    [InlineData("""{"expires_in":3600,"refresh_token":"RT1","x_refresh_token_expires_in":8726400}""")]
    [InlineData("""{"refresh_token":"RT1","x_refresh_token_expires_in":8726400,"access_token":"AT1"}""")]
    [InlineData("""{"expires_in":0,"refresh_token":"RT1","x_refresh_token_expires_in":8726400,"access_token":"AT1"}""")]
    [InlineData("""{"expires_in":3600,"refresh_token":"","x_refresh_token_expires_in":8726400,"access_token":"AT1"}""")]
    [InlineData("[]")]
    public void Parse_AnIncompleteAnswer_Throws(string json)
    {
        var act = () => QuickBooksTokenResponse.Parse(json, null);

        act.Should().Throw<QuickBooksAuthException>().WithMessage("*incomplete sign-in answer*");
    }

    [Fact]
    public void Parse_NotJson_Throws()
    {
        var act = () => QuickBooksTokenResponse.Parse("<html>", null);

        act.Should().Throw<QuickBooksAuthException>().WithMessage("*could not be read*");
    }
}
