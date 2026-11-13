using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksSendsRateTests
{
    [Theory]
    [InlineData(1000, 0.12345, 123.45, true)]   // 123.45 exactly
    [InlineData(2500, 0.12345, 308.63, true)]   // 308.625 rounds half up to 308.63
    [InlineData(2500, 0.12345, 308.62, false)]  // half-even rounding would have said 308.62
    [InlineData(1000, 0.50, 300.00, false)]     // ordered 1000, billed 600 from a shipment
    [InlineData(0, 0.50, 0.00, false)]          // no quantity: no rate
    public void SendsRate_OnlyWhenQuantityTimesRateIsTheAmount(int quantity, double rate, double amount, bool expected) =>
        QuickBooksInvoicePayloadBuilder.SendsRate(quantity, (decimal)rate, (decimal)amount).Should().Be(expected);
}
