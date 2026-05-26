namespace LabelsMis.Domain.Common;

public abstract class MasterDataEntity : EntityBase
{
    public bool IsActive { get; protected set; } = true;

    public void Deactivate(Guid modifiedById, DateTime modifiedAt)
    {
        IsActive = false;
        SetModified(modifiedById, modifiedAt);
    }

    public void Reactivate(Guid modifiedById, DateTime modifiedAt)
    {
        IsActive = true;
        SetModified(modifiedById, modifiedAt);
    }
}
