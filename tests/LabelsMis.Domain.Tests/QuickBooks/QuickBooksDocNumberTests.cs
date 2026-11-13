using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksDocNumberTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData("INV-2026-00549", "00549")]
    [InlineData("INV-2027-00550", "00550")]
    [InlineData("INV-2026-00001", "00001")]
    [InlineData("INV-2031-100000", "100000")]
    [InlineData("  INV-2026-00549 ", "00549")]
    public void For_NumberOnly_SendsTheNumberAtTheEndWithItsZeros(string invoiceNumber, string expected)
    {
        QuickBooksDocNumber.For(invoiceNumber, QuickBooksInvoiceNumberFormat.NumberOnly).Should().Be(expected);
    }

    [Theory]
    [InlineData("INV-2026-00549")]
    [InlineData("12345")]
    [InlineData("  INV-2026-00549 ")]
    public void For_Full_SendsTheLabelShopNumber(string invoiceNumber)
    {
        QuickBooksDocNumber.For(invoiceNumber, QuickBooksInvoiceNumberFormat.Full).Should().Be(invoiceNumber.Trim());
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("A-1042")]
    [InlineData("INV-1042")]
    [InlineData("INV-2026-")]
    [InlineData("INV-2026-00549-R1")]
    [InlineData("INV-26-00549")]
    [InlineData("INV-2026-0054A")]
    [InlineData("-2026-00549")]
    [InlineData("QB 2025/118")]
    public void For_NumberOnly_ANumberOfAnotherShape_GoesAsItIs(string invoiceNumber)
    {
        QuickBooksDocNumber.For(invoiceNumber, QuickBooksInvoiceNumberFormat.NumberOnly).Should().Be(invoiceNumber);
    }

    [Fact]
    public void For_NumberOnly_AppliedTwice_ChangesNothingMore()
    {
        var once = QuickBooksDocNumber.For("INV-2026-00549", QuickBooksInvoiceNumberFormat.NumberOnly);

        QuickBooksDocNumber.For(once, QuickBooksInvoiceNumberFormat.NumberOnly).Should().Be("00549");
    }

    [Fact]
    public void PushSettings_Default_SendsTheFullNumber()
    {
        QuickBooksPushSettings.Default.NumberFormat.Should().Be(QuickBooksInvoiceNumberFormat.Full);
        QuickBooksPushSettings.Default.DocNumberFor("INV-2026-00549").Should().Be("INV-2026-00549");
    }

    [Fact]
    public void PushSettings_NumberOnly_GivesTheShortNumber()
    {
        var settings = QuickBooksPushSettings.Default with { NumberFormat = QuickBooksInvoiceNumberFormat.NumberOnly };

        settings.DocNumberFor("INV-2026-00549").Should().Be("00549");
    }

    [Fact]
    public void Settings_NewRow_SendsTheFullNumber()
    {
        var settings = QuickBooksSettings.CreateDefault(UserId, DateTime.UtcNow);

        settings.InvoiceNumberFormat.Should().Be(QuickBooksInvoiceNumberFormat.Full);
        settings.ToPushSettings().NumberFormat.Should().Be(QuickBooksInvoiceNumberFormat.Full);
    }

    [Fact]
    public void Settings_UpdatePush_KeepsTheNumberFormat()
    {
        var settings = QuickBooksSettings.CreateDefault(UserId, DateTime.UtcNow);

        settings.UpdatePush(
            settings.ToPushSettings() with { NumberFormat = QuickBooksInvoiceNumberFormat.NumberOnly }, UserId, DateTime.UtcNow);

        settings.InvoiceNumberFormat.Should().Be(QuickBooksInvoiceNumberFormat.NumberOnly);
        settings.ToPushSettings().DocNumberFor("INV-2026-00549").Should().Be("00549");
    }

    [Fact]
    public void Settings_UpdatePush_WithAFormatThatDoesNotExist_IsRefused()
    {
        var settings = QuickBooksSettings.CreateDefault(UserId, DateTime.UtcNow);

        var act = () => settings.UpdatePush(
            settings.ToPushSettings() with { NumberFormat = (QuickBooksInvoiceNumberFormat)7 }, UserId, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>().WithMessage("*invoice number*");
        settings.InvoiceNumberFormat.Should().Be(QuickBooksInvoiceNumberFormat.Full);
    }
}
