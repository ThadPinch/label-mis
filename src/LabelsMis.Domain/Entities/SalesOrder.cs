using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class SalesOrder : EntityBase
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public Guid? SourceEstimateId { get; private set; }
    public Estimate? SourceEstimate { get; private set; }
    public string? CustomerPoNumber { get; private set; }
    public DateTime OrderedAt { get; private set; }
    public DateOnly? RequestedShipDate { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;

    public static SalesOrder CreateOpen(
        Guid id,
        string orderNumber,
        Guid customerId,
        Guid? sourceEstimateId,
        string? customerPoNumber,
        DateTime orderedAt,
        DateOnly? requestedShipDate,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            SourceEstimateId = sourceEstimateId,
            CustomerPoNumber = string.IsNullOrWhiteSpace(customerPoNumber) ? null : customerPoNumber.Trim(),
            OrderedAt = orderedAt,
            RequestedShipDate = requestedShipDate,
            Status = SalesOrderStatus.Open,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        order.SetCreated(id, createdById, createdAt);
        return order;
    }

    public void UpdateOpen(
        string? customerPoNumber,
        DateOnly? requestedShipDate,
        string? notes,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        EnsureOpen();

        CustomerPoNumber = string.IsNullOrWhiteSpace(customerPoNumber) ? null : customerPoNumber.Trim();
        RequestedShipDate = requestedShipDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void ReplaceLines(IEnumerable<SalesOrderLine> lines)
    {
        EnsureOpen();
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void AdvanceStatus(SalesOrderStatus status, Guid modifiedById, DateTime modifiedAt)
    {
        if (status <= Status)
        {
            throw new InvalidOperationException("Sales order status can only move forward.");
        }

        Status = status;
        SetModified(modifiedById, modifiedAt);
    }

    public void EnsureOpen()
    {
        if (Status is not SalesOrderStatus.Open)
        {
            throw new InvalidOperationException("Sales orders cannot be edited once production has started.");
        }
    }

    public void AddLine(SalesOrderLine line) => _lines.Add(line);
}
