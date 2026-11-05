using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.Tests.Invoicing;

public class CustomerBillingRulesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ChooseBillingEmail_PrefersThePrimaryBillingContactWithAnEmail()
    {
        var choice = CustomerBillingRules.ChooseBillingEmail([
            new BillingContactFacts(false, "buyer@x.com", true),
            new BillingContactFacts(true, null, true),
            new BillingContactFacts(true, "ap@x.com", false),
            new BillingContactFacts(true, " lead-ap@x.com ", true)
        ]);

        choice.Should().Be(new BillingEmailChoice("lead-ap@x.com", BillingEmailSource.BillingContact));
    }

    [Fact]
    public void ChooseBillingEmail_NoBillingContactWithAnEmail_FallsBackToThePrimaryContact()
    {
        var choice = CustomerBillingRules.ChooseBillingEmail([
            new BillingContactFacts(true, "  ", false),
            new BillingContactFacts(false, "other@x.com", false),
            new BillingContactFacts(false, "owner@x.com", true)
        ]);

        choice.Should().Be(new BillingEmailChoice("owner@x.com", BillingEmailSource.PrimaryContact));
        choice!.IsPrimaryFallback.Should().BeTrue();
    }

    [Fact]
    public void ChooseBillingEmail_NeitherABillingNorAPrimaryContactWithAnEmail_IsNull()
    {
        CustomerBillingRules.ChooseBillingEmail([
            new BillingContactFacts(false, "other@x.com", false),
            new BillingContactFacts(false, null, true)
        ]).Should().BeNull();
    }

    [Fact]
    public void Blockers_NewClient_WaitsOnTheFormAndABillingEmail()
    {
        CustomerBillingRules.Blockers(false, [new BillingContactFacts(false, "buyer@x.com", false)])
            .Should().Equal(CustomerBillingRules.NewClientFormBlocker, CustomerBillingRules.NoBillingContactBlocker);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Blockers_SetUpCustomer_WithABillingOrPrimaryEmail_HasNone(bool isBillingContact, bool isPrimary)
    {
        CustomerBillingRules.Blockers(true, [new BillingContactFacts(isBillingContact, "ap@x.com", isPrimary)])
            .Should().BeEmpty();
    }

    [Fact]
    public void ContactCreateAndUpdate_CarryTheBillingTick()
    {
        var contact = Contact.Create(Guid.NewGuid(), Guid.NewGuid(), "Kim", "Wilson", "kim@x.com", null,
            "Brand Strategy", false, true, UserId, Now);
        contact.IsBillingContact.Should().BeTrue();

        contact.Update("Kim", "Wilson", "kim@x.com", null, "Brand Strategy", false, false, UserId, Now);

        contact.IsBillingContact.Should().BeFalse();
    }

    [Fact]
    public void SetQuickBooksSetup_StampsTodayWhenTicked_ClearsWhenUnticked_TrimsTheId()
    {
        var customer = Customer.Create(Guid.NewGuid(), "Acme", "ACME", PaymentTerms.Net30, false, 0.45m,
            CustomerStatus.Active, null, null, UserId, Now);

        customer.SetQuickBooksSetup("  58  ", true, null, UserId, Now);
        customer.QuickBooksCustomerId.Should().Be("58");
        customer.TaxSetupComplete.Should().BeTrue();
        customer.TaxSetupCompletedAt.Should().Be(new DateOnly(2026, 11, 1));

        customer.SetQuickBooksSetup("58", true, new DateOnly(2026, 10, 2), UserId, Now);
        customer.TaxSetupCompletedAt.Should().Be(new DateOnly(2026, 10, 2));

        customer.SetQuickBooksSetup(" ", false, new DateOnly(2026, 10, 2), UserId, Now);
        customer.QuickBooksCustomerId.Should().BeNull();
        customer.TaxSetupComplete.Should().BeFalse();
        customer.TaxSetupCompletedAt.Should().BeNull();
    }
}
