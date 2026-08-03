using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Users;

public record UserListItem(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    bool MustChangePassword);

public record UserDetail(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    bool MustChangePassword);

public record CreateUserInput(
    string Email,
    string Password,
    IReadOnlyList<string> Roles,
    bool MustChangePassword);

public record UpdateUserInput(
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    bool MustChangePassword,
    string? NewPassword);

public class UserAdminService(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser)
{
    public async Task<IReadOnlyList<UserListItem>> ListAsync(
        string? search,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var query = userManager.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToUpper().Contains(term))
                || (u.UserName != null && u.UserName.ToUpper().Contains(term)));
        }

        var users = await query.OrderBy(u => u.Email ?? u.UserName).ToListAsync(cancellationToken);
        var items = new List<UserListItem>(users.Count);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new UserListItem(
                user.Id,
                user.Email ?? user.UserName ?? user.Id.ToString(),
                roles.OrderBy(r => r).ToList(),
                await userManager.IsLockedOutAsync(user),
                user.MustChangePassword));
        }

        // Roles and status only exist after materialization, so sorting happens in memory.
        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        return sortKey switch
        {
            "email" => (desc ? items.OrderByDescending(i => i.Email) : items.OrderBy(i => i.Email)).ToList(),
            "roles" => (desc
                ? items.OrderByDescending(i => string.Join(", ", i.Roles))
                : items.OrderBy(i => string.Join(", ", i.Roles))).ToList(),
            "status" => (desc
                ? items.OrderByDescending(i => i.IsLockedOut ? 2 : i.MustChangePassword ? 1 : 0)
                : items.OrderBy(i => i.IsLockedOut ? 2 : i.MustChangePassword ? 1 : 0)).ToList(),
            _ => items
        };
    }

    public async Task<UserDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new UserDetail(
            user.Id,
            user.Email ?? user.UserName ?? user.Id.ToString(),
            roles.OrderBy(r => r).ToList(),
            await userManager.IsLockedOutAsync(user),
            user.MustChangePassword);
    }

    public async Task<ApplicationUser> CreateAsync(CreateUserInput input, CancellationToken cancellationToken = default)
    {
        var email = input.Email.Trim();
        var roles = NormalizeRoles(input.Roles);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            MustChangePassword = input.MustChangePassword
        };

        var createResult = await userManager.CreateAsync(user, input.Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(FormatErrors(createResult));
        }

        await SetRolesAsync(user, roles);
        return user;
    }

    public async Task UpdateAsync(Guid id, UpdateUserInput input, CancellationToken cancellationToken = default)
    {
        var actorId = RequireUserId();
        var user = await userManager.FindByIdAsync(id.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var roles = NormalizeRoles(input.Roles);
        await EnsureNotRemovingLastAdminAsync(user, roles);

        if (input.IsLockedOut && user.Id == actorId)
        {
            throw new InvalidOperationException("You cannot lock your own account.");
        }

        await SetRolesAsync(user, roles);

        user.MustChangePassword = input.MustChangePassword;

        if (input.IsLockedOut)
        {
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }
        else
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new InvalidOperationException(FormatErrors(updateResult));
        }

        if (!string.IsNullOrWhiteSpace(input.NewPassword))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await userManager.ResetPasswordAsync(user, token, input.NewPassword);
            if (!resetResult.Succeeded)
            {
                throw new InvalidOperationException(FormatErrors(resetResult));
            }

            if (input.MustChangePassword)
            {
                user.MustChangePassword = true;
                await userManager.UpdateAsync(user);
            }
        }
    }

    private async Task SetRolesAsync(ApplicationUser user, IReadOnlyList<string> roles)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(roles).ToList();
        var toAdd = roles.Except(currentRoles).ToList();

        if (toRemove.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException(FormatErrors(removeResult));
            }
        }

        if (toAdd.Count > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded)
            {
                throw new InvalidOperationException(FormatErrors(addResult));
            }
        }
    }

    private async Task EnsureNotRemovingLastAdminAsync(ApplicationUser user, IReadOnlyList<string> newRoles)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(AppRoles.Admin) || newRoles.Contains(AppRoles.Admin))
        {
            return;
        }

        var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
        if (admins.Count <= 1 && admins.Any(a => a.Id == user.Id))
        {
            throw new InvalidOperationException("Cannot remove the last Admin user.");
        }
    }

    private static IReadOnlyList<string> NormalizeRoles(IReadOnlyList<string> roles) =>
        roles.Where(r => AppRoles.All.Contains(r)).Distinct(StringComparer.Ordinal).OrderBy(r => r).ToList();

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
