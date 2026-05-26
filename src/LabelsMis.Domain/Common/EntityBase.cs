namespace LabelsMis.Domain.Common;

public abstract class EntityBase
{
    public Guid Id { get; protected set; }
    public Guid TenantId { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public Guid CreatedById { get; protected set; }
    public DateTime? ModifiedAt { get; protected set; }
    public Guid? ModifiedById { get; protected set; }

    protected void SetCreated(Guid id, Guid createdById, DateTime createdAt)
    {
        Id = id;
        TenantId = TenantConstants.DefaultTenantId;
        CreatedById = createdById;
        CreatedAt = createdAt;
    }

    protected void SetModified(Guid modifiedById, DateTime modifiedAt)
    {
        ModifiedById = modifiedById;
        ModifiedAt = modifiedAt;
    }
}
