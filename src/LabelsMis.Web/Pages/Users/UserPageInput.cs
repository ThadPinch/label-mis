using System.ComponentModel.DataAnnotations;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.Users;

namespace LabelsMis.Web.Pages.Users;

public class UserPageInput
{
    public bool IsEdit { get; set; }

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(100, MinimumLength = 8)]
    public string? Password { get; set; }

    [StringLength(100, MinimumLength = 8)]
    public string? NewPassword { get; set; }

    public List<string> SelectedRoles { get; set; } = [];

    public bool MustChangePassword { get; set; } = true;

    public bool IsLockedOut { get; set; }

    public IReadOnlyList<string> AvailableRoles { get; set; } = AppRoles.All;

    public static UserPageInput ForCreate() => new()
    {
        IsEdit = false,
        MustChangePassword = true,
        AvailableRoles = AppRoles.All
    };

    public static UserPageInput FromDetail(UserDetail detail) => new()
    {
        IsEdit = true,
        Email = detail.Email,
        SelectedRoles = detail.Roles.ToList(),
        MustChangePassword = detail.MustChangePassword,
        IsLockedOut = detail.IsLockedOut,
        AvailableRoles = AppRoles.All
    };
}
