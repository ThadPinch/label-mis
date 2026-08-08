namespace LabelsMis.Domain.Common;

public static class TenantConstants
{
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Seeded non-interactive user that background services stamp on audit columns.</summary>
    public static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");
}
