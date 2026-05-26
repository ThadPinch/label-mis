using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabelsMis.Infrastructure.Persistence;

public static class MasterDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<LabelsMisDbContext>>();

        if (await db.Presses.AnyAsync(p => p.Id == Press.Indigo6800Id, cancellationToken))
        {
            return;
        }

        var adminUser = await userManager.FindByEmailAsync(IdentitySeeder.DefaultAdminEmail);
        if (adminUser is null)
        {
            logger.LogWarning("Skipping master data seed because admin user does not exist.");
            return;
        }

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Presses.Add(Press.CreateIndigo6800(adminUser.Id, seededAt));
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded default Indigo 6800 press.");
    }
}
