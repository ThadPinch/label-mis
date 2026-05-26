using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Payment : EntityBase
{
    private Payment()
    {
    }

    public Guid InvoiceId { get; private set; }
    public Invoice Invoice { get; private set; } = null!;
    public DateOnly PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }

    public static Payment Create(
        Guid id,
        Guid invoiceId,
        DateOnly paymentDate,
        decimal amount,
        PaymentMethod method,
        string? reference,
        string? notes,
        Guid recordedById,
        DateTime createdAt)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        var payment = new Payment
        {
            InvoiceId = invoiceId,
            PaymentDate = paymentDate,
            Amount = amount,
            Method = method,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        payment.SetCreated(id, recordedById, createdAt);
        return payment;
    }
}
