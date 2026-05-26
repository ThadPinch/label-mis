using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class ProductCustomer : EntityBase
{
    private ProductCustomer()
    {
    }

    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;

    public static ProductCustomer Create(
        Guid id,
        Guid productId,
        Guid customerId,
        Guid createdById,
        DateTime createdAt)
    {
        var assignment = new ProductCustomer
        {
            ProductId = productId,
            CustomerId = customerId
        };
        assignment.SetCreated(id, createdById, createdAt);
        return assignment;
    }
}
