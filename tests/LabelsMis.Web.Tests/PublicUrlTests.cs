using LabelsMis.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests;

public class PublicUrlTests
{
    [Theory]
    [InlineData("https://mis.heyluckylabel.com", "https://mis.heyluckylabel.com")]
    [InlineData("https://mis.heyluckylabel.com/", "https://mis.heyluckylabel.com")]
    [InlineData("  http://localhost:5000  ", "http://localhost:5000")]
    public void ResolveBase_WhenConfigured_UsesTheConfiguredAddress(string configured, string expected)
    {
        // The request says http (TLS ended upstream); the configured address must still win.
        PublicUrl.ResolveBase(configured, "http", "internal-host:8080").Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveBase_WhenNotConfigured_UsesTheRequest(string? configured)
    {
        PublicUrl.ResolveBase(configured, "https", "mis.heyluckylabel.com").Should().Be("https://mis.heyluckylabel.com");
    }

    [Theory]
    [InlineData("mis.heyluckylabel.com")]
    [InlineData("ftp://mis.heyluckylabel.com")]
    public void ResolveBase_WhenConfiguredAddressIsNotHttp_Throws(string configured)
    {
        var act = () => PublicUrl.ResolveBase(configured, "https", "mis.heyluckylabel.com");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an http(s) address*");
    }

    [Fact]
    public void ResolveBase_WithNothingConfiguredAndNoRequest_Throws()
    {
        var act = () => PublicUrl.ResolveBase(null, null, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*set App:PublicBaseUrl*");
    }

    [Theory]
    [InlineData("https://mis.heyluckylabel.com", "/quickbooks/callback")]
    [InlineData("https://mis.heyluckylabel.com/", "quickbooks/callback")]
    public void Combine_JoinsWithExactlyOneSlash(string baseUrl, string path)
    {
        PublicUrl.Combine(baseUrl, path).Should().Be("https://mis.heyluckylabel.com/quickbooks/callback");
    }

    [Theory]
    [InlineData("https://mis.heyluckylabel.com", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsConfigured_SaysWhetherTheAddressComesFromConfiguration(string? configured, bool expected)
    {
        var service = new PublicUrlService(
            Options.Create(new AppUrlOptions { PublicBaseUrl = configured }), new HttpContextAccessor());

        service.IsConfigured.Should().Be(expected);
    }
}
