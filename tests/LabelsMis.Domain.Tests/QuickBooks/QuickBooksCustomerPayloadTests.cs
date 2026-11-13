using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksCustomerPayloadTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 13, 15, 0, 0, DateTimeKind.Utc);

    private static Customer NewCustomer(string name = "Hey Lucky Labels")
    {
        return Customer.Create(Guid.NewGuid(), name, "HLL", PaymentTerms.Net30, false, 0.45m,
            CustomerStatus.Active, null, null, UserId, At);
    }

    private static void AddContact(Customer customer, string first, string? email, string? phone, bool primary, bool billing) =>
        customer.AddContact(Contact.Create(Guid.NewGuid(), customer.Id, first, "Smith", email, phone, null, primary, billing, UserId, At));

    private static void AddAddress(Customer customer, AddressType type, string street, bool isDefault = true) =>
        customer.AddAddress(Address.Create(Guid.NewGuid(), customer.Id, type, street, null, "Phoenix", "AZ", "85001", "US", isDefault, UserId, At));

    private static JsonElement Body(QuickBooksCustomerCreate create) => JsonDocument.Parse(create.Json).RootElement;

    [Fact]
    public void Build_CarriesNameBillingEmailPhoneAndBothAddresses()
    {
        var customer = NewCustomer();
        AddContact(customer, "Pat", "pat@example.com", "602-555-0100", primary: true, billing: false);
        AddContact(customer, "Bea", "ap@example.com", "602-555-0199", primary: false, billing: true);
        AddAddress(customer, AddressType.Billing, "1 Bill St");
        AddAddress(customer, AddressType.Shipping, "2 Ship St");

        var body = Body(QuickBooksCustomerPayload.Build(customer));

        body.GetProperty("DisplayName").GetString().Should().Be("Hey Lucky Labels");
        body.GetProperty("CompanyName").GetString().Should().Be("Hey Lucky Labels");
        body.GetProperty("PrimaryEmailAddr").GetProperty("Address").GetString().Should().Be("ap@example.com");
        body.GetProperty("PrimaryPhone").GetProperty("FreeFormNumber").GetString().Should().Be("602-555-0199");
        body.GetProperty("BillAddr").GetProperty("Line1").GetString().Should().Be("1 Bill St");
        body.GetProperty("BillAddr").GetProperty("CountrySubDivisionCode").GetString().Should().Be("AZ");
        body.GetProperty("BillAddr").GetProperty("PostalCode").GetString().Should().Be("85001");
        body.GetProperty("ShipAddr").GetProperty("Line1").GetString().Should().Be("2 Ship St");
    }

    [Fact]
    public void Build_WithNoBillingContact_UsesThePrimaryContactsEmail()
    {
        var customer = NewCustomer();
        AddContact(customer, "Pat", "pat@example.com", null, primary: true, billing: false);

        var body = Body(QuickBooksCustomerPayload.Build(customer));

        body.GetProperty("PrimaryEmailAddr").GetProperty("Address").GetString().Should().Be("pat@example.com");
        body.TryGetProperty("PrimaryPhone", out _).Should().BeFalse();
    }

    [Fact]
    public void Build_WithNoContactsOrAddresses_SendsTheNamesOnly()
    {
        var body = Body(QuickBooksCustomerPayload.Build(NewCustomer()));

        body.EnumerateObject().Select(p => p.Name).Should().Equal("DisplayName", "CompanyName");
    }

    [Fact]
    public void Build_WithOneAddress_UsesItForBothBillToAndShipTo()
    {
        var customer = NewCustomer();
        AddAddress(customer, AddressType.Shipping, "2 Ship St");

        var body = Body(QuickBooksCustomerPayload.Build(customer));

        body.GetProperty("BillAddr").GetProperty("Line1").GetString().Should().Be("2 Ship St");
        body.GetProperty("ShipAddr").GetProperty("Line1").GetString().Should().Be("2 Ship St");
    }

    [Fact]
    public void Build_PrefersTheDefaultAddressOfAType()
    {
        var customer = NewCustomer();
        AddAddress(customer, AddressType.Shipping, "Old dock", isDefault: false);
        AddAddress(customer, AddressType.Shipping, "Main dock", isDefault: true);

        Body(QuickBooksCustomerPayload.Build(customer)).GetProperty("ShipAddr").GetProperty("Line1").GetString()
            .Should().Be("Main dock");
    }

    [Theory]
    [InlineData("Acme: West", "Acme - West")]
    [InlineData("Acme\tWest\nCo", "Acme West Co")]
    [InlineData("  Plain  Name ", "Plain Name")]
    public void DisplayNameFor_RemovesWhatQuickBooksRefuses(string name, string expected)
    {
        QuickBooksCustomerPayload.DisplayNameFor(name).Should().Be(expected);
    }

    [Fact]
    public void DisplayNameFor_CutsAt500Characters()
    {
        QuickBooksCustomerPayload.DisplayNameFor(new string('x', 600)).Should().HaveLength(500);
    }

    [Fact]
    public void Build_TheSameCustomerTwice_HasTheSameRequestId()
    {
        var customer = NewCustomer();
        AddContact(customer, "Pat", "pat@example.com", null, primary: true, billing: true);

        var first = QuickBooksCustomerPayload.Build(customer);
        var second = QuickBooksCustomerPayload.Build(customer);

        second.RequestId.Should().Be(first.RequestId);
        first.RequestId.Should().HaveLength(QuickBooksCustomerPayload.RequestIdLength).And.MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void Build_AChangedCustomer_HasANewRequestId()
    {
        var customer = NewCustomer();
        var before = QuickBooksCustomerPayload.Build(customer);

        customer.Update("Hey Lucky Labels LLC", "HLL", PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, UserId, At);

        QuickBooksCustomerPayload.Build(customer).RequestId.Should().NotBe(before.RequestId);
    }

    [Fact]
    public void RequestId_DiffersBetweenCustomersWithTheSameBody()
    {
        QuickBooksCustomerPayload.RequestId(Guid.NewGuid(), "{}")
            .Should().NotBe(QuickBooksCustomerPayload.RequestId(Guid.NewGuid(), "{}"));
    }
}
