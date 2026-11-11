using System.Text.Json;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksCompanyInfoTests
{
    [Fact]
    public void ReadCompanyName_ReturnsTheCompanyName()
    {
        var json = """{"CompanyInfo":{"CompanyName":" Sandbox Company US ef89 ","LegalName":"Legal Co"},"time":"2026-11-10T12:00:00.000-08:00"}""";

        QuickBooksCompanyInfo.ReadCompanyName(json).Should().Be("Sandbox Company US ef89");
    }

    [Fact]
    public void ReadCompanyName_WithoutACompanyName_FallsBackToTheLegalName()
    {
        var json = """{"CompanyInfo":{"CompanyName":"","LegalName":"Legal Co"}}""";

        QuickBooksCompanyInfo.ReadCompanyName(json).Should().Be("Legal Co");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"CompanyInfo":"unexpected"}""")]
    [InlineData("""{"CompanyInfo":{"CompanyName":42}}""")]
    public void ReadCompanyName_WithNoNameInTheAnswer_IsNull(string json)
    {
        QuickBooksCompanyInfo.ReadCompanyName(json).Should().BeNull();
    }

    [Fact]
    public void ReadCompanyName_WhenTheAnswerIsNotJson_Throws()
    {
        var act = () => QuickBooksCompanyInfo.ReadCompanyName("<html>");

        act.Should().Throw<JsonException>();
    }
}
