using LabelsMis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace LabelsMis.Web.Middleware;

public class ForcePasswordChangeMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedPaths =
    [
        "/Account/ChangePassword",
        "/Account/Logout",
        "/Error"
    ];

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!IsAllowedPath(path))
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user?.MustChangePassword == true)
                {
                    context.Response.Redirect("/Account/ChangePassword");
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool IsAllowedPath(string path)
    {
        foreach (var allowedPath in AllowedPaths)
        {
            if (path.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
