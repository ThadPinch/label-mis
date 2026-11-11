using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Http;

namespace LabelsMis.Web.Tests.QuickBooks;

public class QuickBooksOAuthStateTests
{
    private static DefaultHttpContext BrowserHolding(string? state)
    {
        var context = new DefaultHttpContext();
        if (state is not null)
        {
            context.Request.Headers.Cookie = $"{QuickBooksOAuthState.CookieName}={state}";
        }

        return context;
    }

    [Fact]
    public void Issue_PutsTheSameUnguessableValueInAShortLivedHttpOnlyCookie()
    {
        var context = new DefaultHttpContext();

        var state = QuickBooksOAuthState.Issue(context);

        state.Should().HaveLength(43).And.MatchRegex("^[A-Za-z0-9_-]+$");
        var cookie = context.Response.Headers.SetCookie.ToString();
        cookie.Should().StartWith($"qb_oauth_state={state};")
            .And.Contain("max-age=600").And.Contain("path=/quickbooks").And.Contain("samesite=lax").And.Contain("httponly")
            .And.NotContain("secure");
    }

    [Fact]
    public void Issue_OverHttps_MarksTheCookieSecure()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        QuickBooksOAuthState.Issue(context);

        context.Response.Headers.SetCookie.ToString().Should().Contain("secure");
    }

    [Fact]
    public void Issue_Twice_GivesDifferentValues()
    {
        QuickBooksOAuthState.Issue(new DefaultHttpContext()).Should().NotBe(QuickBooksOAuthState.Issue(new DefaultHttpContext()));
    }

    [Fact]
    public void Consume_WithTheValueThisBrowserWasGiven_IsTrueAndRemovesTheCookie()
    {
        var context = BrowserHolding("the-state");

        QuickBooksOAuthState.Consume(context, "the-state").Should().BeTrue();

        context.Response.Headers.SetCookie.ToString().Should().StartWith("qb_oauth_state=;").And.Contain("expires=Thu, 01 Jan 1970");
    }

    [Theory]
    [InlineData("the-state", "another-state")]
    [InlineData("the-state", "the-stat")]
    [InlineData("the-state", "")]
    [InlineData("the-state", null)]
    [InlineData(null, "the-state")]
    [InlineData(null, null)]
    public void Consume_WithAnythingElse_IsFalse(string? held, string? presented)
    {
        QuickBooksOAuthState.Consume(BrowserHolding(held), presented).Should().BeFalse();
    }

    [Fact]
    public void Consume_WhenItFails_StillRemovesTheCookie()
    {
        var context = BrowserHolding("the-state");

        QuickBooksOAuthState.Consume(context, "another-state");

        context.Response.Headers.SetCookie.ToString().Should().StartWith("qb_oauth_state=;");
    }
}
