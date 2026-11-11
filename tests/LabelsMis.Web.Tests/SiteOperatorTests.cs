using LabelsMis.Domain.Entities;
using LabelsMis.Web.Pages.Legal;

namespace LabelsMis.Web.Tests;

public class SiteOperatorTests
{
    private static GeneralSettings Settings(
        string companyName, string? email = null, string? line1 = null, string? line2 = null,
        string? city = null, string? state = null, string? zip = null)
    {
        var settings = GeneralSettings.CreateDefault(Guid.NewGuid(), DateTime.UtcNow);
        settings.Update(
            companyName, line1, line2, city, state, zip, phone: null, email, website: null, termsText: null,
            taxRate: 0.0825m, defaultMarkupPct: 0.55m, modifiedById: Guid.NewGuid(), modifiedAt: DateTime.UtcNow);
        return settings;
    }

    [Fact]
    public void From_WithNoSettingsRow_NamesNoCompany()
    {
        var site = SiteOperator.From(null);

        site.Should().Be(SiteOperator.Unnamed);
        site.SentenceStart.Should().Be("The operator of this site");
    }

    [Fact]
    public void From_WithABlankCompanyName_NamesNoCompany()
    {
        var site = SiteOperator.From(Settings("   ", email: "office@example.com"));

        site.Should().Satisfy<SiteOperator>(s =>
        {
            s.IsNamed.Should().BeFalse();
            s.Name.Should().Be(SiteOperator.UnnamedOperator);
            s.Email.Should().Be("office@example.com");
        });
    }

    [Fact]
    public void From_WithACompany_UsesItsNameContactAndAddress()
    {
        var site = SiteOperator.From(Settings(
            " Example Label Co. ", " office@example.com ", "1 Press Way", "Suite 4", "Phoenix", "AZ", "85001"));

        site.Should().Satisfy<SiteOperator>(s =>
        {
            s.IsNamed.Should().BeTrue();
            s.Name.Should().Be("Example Label Co.");
            s.SentenceStart.Should().Be("Example Label Co.");
            s.Email.Should().Be("office@example.com");
            s.PostalAddress.Should().Be("1 Press Way, Suite 4, Phoenix, AZ 85001");
        });
    }

    [Theory]
    [InlineData("1 Press Way", null, null, null, "1 Press Way")]
    [InlineData(null, "Phoenix", null, null, "Phoenix")]
    [InlineData(null, "Phoenix", "AZ", null, "Phoenix, AZ")]
    [InlineData(null, null, "AZ", "85001", "AZ 85001")]
    [InlineData("1 Press Way", null, null, "85001", "1 Press Way, 85001")]
    public void From_WithPartOfAnAddress_JoinsOnlyWhatIsThere(
        string? line1, string? city, string? state, string? zip, string expected)
    {
        var site = SiteOperator.From(Settings("Example Label Co.", line1: line1, city: city, state: state, zip: zip));

        site.PostalAddress.Should().Be(expected);
    }

    [Fact]
    public void From_WithNoAddressOrEmail_LeavesBothEmpty()
    {
        var site = SiteOperator.From(Settings("Example Label Co."));

        site.Should().Satisfy<SiteOperator>(s =>
        {
            s.Email.Should().BeNull();
            s.PostalAddress.Should().BeNull();
        });
    }
}
