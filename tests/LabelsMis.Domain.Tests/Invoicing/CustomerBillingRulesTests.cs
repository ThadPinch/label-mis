using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.Tests.Invoicing;

public class CustomerBillingRulesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Billing", true)]
    [InlineData("billing / AP", true)]
    [InlineData("Accounts Payable", true)]
    [InlineData("A/P", true)]
    [InlineData("Invoices", true)]
    [InlineData("Accounting", true)]
    [InlineData("Purchasing", false)]
    [InlineData("Approver", false)]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    public void IsBillingRole_MatchesBillingWords(string? role, bool expected)
    {
        CustomerBillingRules.IsBillingRole(role).Should().Be(expected);
    }

    [Fact]
    public void BillingEmail_PrefersThePrimaryBillingContactWithAnEmail()
    {
        var email = CustomerBillingRules.BillingEmail([
            new BillingContactFacts("Purchasing", "buyer@x.com", true),
            new BillingContactFacts("Billing", null, true),
            new BillingContactFacts("Billing", "ap@x.com", false),
            new BillingContactFacts("Billing", " lead-ap@x.com ", true)
        ]);

        email.Should().Be("lead-ap@x.com");
    }

    [Fact]
    public void Blockers_NewClient_WaitsOnTheFormAndABillingContact()
    {
        CustomerBillingRules.Blockers(false, [new BillingContactFacts("Purchasing", "buyer@x.com", true)])
            .Should().Equal(CustomerBillingRules.NewClientFormBlocker, CustomerBillingRules.NoBillingContactBlocker);
    }

    [Fact]
    public void Blockers_SetUpCustomer_HasNone()
    {
        CustomerBillingRules.Blockers(true, [new BillingContactFacts("Billing", "ap@x.com", false)]).Should().BeEmpty();
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
