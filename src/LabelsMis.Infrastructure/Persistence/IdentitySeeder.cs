using LabelsMis.Domain.Common;
using LabelsMis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabelsMis.Infrastructure.Persistence;

public static class IdentitySeeder
{
    public const string DefaultAdminEmail = "admin@labels-mis.local";
    public const string DefaultAdminPassword = "pa55w0rd";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<LabelsMisDbContext>>();

        foreach (var roleName in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to create role '{roleName}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
        }

        var adminUser = await userManager.FindByEmailAsync(DefaultAdminEmail);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = DefaultAdminEmail,
                Email = DefaultAdminEmail,
                EmailConfirmed = true,
                MustChangePassword = true
            };

            var createResult = await userManager.CreateAsync(adminUser, DefaultAdminPassword);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create admin user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
            }

            var roleResult = await userManager.AddToRoleAsync(adminUser, AppRoles.Admin);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to assign Admin role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
            }

            logger.LogInformation("Seeded default admin user {Email}", DefaultAdminEmail);
        }

        var systemUser = await userManager.FindByIdAsync(TenantConstants.SystemUserId.ToString());
        if (systemUser is null)
        {
            systemUser = new ApplicationUser
            {
                Id = TenantConstants.SystemUserId,
                UserName = "system@labels-mis.local",
                Email = "system@labels-mis.local",
                EmailConfirmed = true,
                MustChangePassword = false,
                LockoutEnabled = true,
                LockoutEnd = DateTimeOffset.MaxValue
            };

            // No password: the account exists only so background services can satisfy
            // the audit-column foreign keys, and the permanent lockout keeps it non-interactive.
            var systemResult = await userManager.CreateAsync(systemUser);
            if (!systemResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create system user: {string.Join(", ", systemResult.Errors.Select(e => e.Description))}");
            }

            logger.LogInformation("Seeded system user for background services");
        }
    }
}
