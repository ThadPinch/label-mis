using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksErrorMessagesTests
{
    private static readonly Guid InvoiceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid CustomerId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    [Theory]
    [InlineData("610", "/settings/quickbooks")]
    [InlineData("2500", "/settings/quickbooks")]
    [InlineData("3200", "/settings/quickbooks")]
    [InlineData("3100", "/settings/quickbooks")]
    [InlineData("2020", "/invoices/11111111-2222-3333-4444-555555555555/quickbooks")]
    [InlineData("2050", "/invoices/11111111-2222-3333-4444-555555555555/quickbooks")]
    [InlineData("6000", "/invoices/11111111-2222-3333-4444-555555555555/quickbooks")]
    [InlineData("6070", "/invoices/11111111-2222-3333-4444-555555555555/quickbooks")]
    [InlineData("6140", "/invoices/11111111-2222-3333-4444-555555555555")]
    [InlineData("6240", "/customers/edit/99999999-8888-7777-6666-555555555555")]
    [InlineData("2090", "/customers/edit/99999999-8888-7777-6666-555555555555")]
    [InlineData("5010", null)]
    [InlineData("6190", null)]
    [InlineData("5000", null)]
    public void Describe_KnownCode_IsAShopSentenceWithItsFixLink(string code, string? fixUrl)
    {
        var text = QuickBooksErrorMessages.Describe(code, "Some Intuit Error: raw words", InvoiceId, CustomerId);

        text.FixUrl.Should().Be(fixUrl);
        text.Message.Should().NotBeNullOrWhiteSpace().And.EndWith(".");
        text.Message.Should().NotStartWith("QuickBooks refused the invoice: Some Intuit Error", "known codes get their own words");
    }

    [Fact]
    public void Describe_ValidationCodes_QuoteIntuitsDetailWhereItNamesTheField()
    {
        var text = QuickBooksErrorMessages.Describe("2050", "String length is either shorter or longer than supported by specification. Min:0 Max:31", InvoiceId, CustomerId);

        text.Message.Should().Contain("Max:31").And.Contain("PO number");
    }

    [Fact]
    public void Describe_UnknownCode_KeepsIntuitsWords()
    {
        var text = QuickBooksErrorMessages.Describe("6999", "Something new went wrong", InvoiceId, CustomerId);

        text.Message.Should().Be("QuickBooks refused the invoice: Something new went wrong");
        text.FixUrl.Should().BeNull();
    }

    [Fact]
    public void Describe_ServerTrouble_FromStatusOrCode()
    {
        var byStatus = QuickBooksErrorMessages.Describe(null, null, InvoiceId, CustomerId, statusCode: 503);
        var byCode = QuickBooksErrorMessages.Describe("10000", "An application error has occurred", InvoiceId, CustomerId);

        byStatus.Message.Should().Contain("problem on its side");
        byCode.Message.Should().Contain("problem on its side");
        QuickBooksErrorMessages.IsServerCode("5010", 400).Should().BeFalse("5010 is a stale sync token, not a server fault");
    }

    [Fact]
    public void Describe_FromException_UsesTheFaultCode()
    {
        var exception = QuickBooksApiException.FromResponse(
            400,
            """{"Fault":{"Error":[{"Message":"Duplicate Document Number Error","Detail":"DocNumber=INV-1 is assigned to TxnType=Invoice with TxnId=145","code":"6140"}]}}""",
            "tid-1");

        QuickBooksErrorMessages.CodeOf(exception).Should().Be("6140");
        QuickBooksErrorMessages.Describe(exception, InvoiceId, CustomerId).Message.Should().Contain("already has an invoice with this number");
    }

    [Fact]
    public void CodeOf_WithoutAFault_IsTheStatusOrNetwork()
    {
        QuickBooksErrorMessages.CodeOf(QuickBooksApiException.FromResponse(429, null, null)).Should().Be("429");
        QuickBooksErrorMessages.CodeOf(QuickBooksApiException.Network("timed out", new TimeoutException())).Should().Be("network");
    }

    [Fact]
    public void Describe_LabelShopCode_KeepsTheMessageItWasGiven()
    {
        var text = QuickBooksErrorMessages.Describe(QuickBooksErrorMessages.Customer, "Acme isn't linked.", InvoiceId, CustomerId);

        text.Message.Should().Be("Acme isn't linked.");
        text.FixUrl.Should().Be($"/customers/edit/{CustomerId}");
    }
}
