using Microsoft.AspNetCore.Identity;

namespace LabelsMis.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; }
}
