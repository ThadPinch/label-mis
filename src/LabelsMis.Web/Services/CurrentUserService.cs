using System.Security.Claims;
using LabelsMis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace LabelsMis.Web.Services;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    bool CanEditMasterData { get; }
    Task<ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default);
}

public class CurrentUserService(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool CanEditMasterData =>
        httpContextAccessor.HttpContext?.User.IsInRole(AppRoles.Admin) == true
        || httpContextAccessor.HttpContext?.User.IsInRole(AppRoles.Estimator) == true;

    public Task<ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default)
    {
        if (UserId is null)
        {
            return Task.FromResult<ApplicationUser?>(null);
        }

        return userManager.FindByIdAsync(UserId.Value.ToString());
    }
}
