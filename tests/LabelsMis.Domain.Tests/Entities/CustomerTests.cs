using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class CustomerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Create_TrimsNotes_AndBlankBecomesNull()
    {
        var withNotes = CreateCustomer("  Always ship on 3\" cores.  ");
        var blank = CreateCustomer("   ");

        withNotes.Notes.Should().Be("Always ship on 3\" cores.");
        blank.Notes.Should().BeNull();
    }

    [Fact]
    public void Update_ReplacesNotes()
    {
        var customer = CreateCustomer("old");

        customer.Update("Acme", "ACME", PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null,
            " new notes ", UserId, Now);

        customer.Notes.Should().Be("new notes");

        customer.Update("Acme", "ACME", PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null,
            null, UserId, Now);

        customer.Notes.Should().BeNull();
    }

    private static Customer CreateCustomer(string? notes) => Customer.Create(
        Guid.NewGuid(), "Acme", "ACME", PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null,
        notes, UserId, Now);
}
