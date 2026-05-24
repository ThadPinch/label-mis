using LabelsMis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Infrastructure.Persistence;

public class LabelsMisDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public LabelsMisDbContext(DbContextOptions<LabelsMisDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");
    }
}
