using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services.Artwork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Artwork;

[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Csr},{AppRoles.Estimator},{AppRoles.Operator}")]
public class UploadModel(ArtworkService artworkService) : PageModel
{
    public async Task<IActionResult> OnPostAsync(Guid productId, IFormFile file, CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            return BadRequest(new { success = false, error = "Select a product before uploading artwork." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { success = false, error = "Select a file to upload." });
        }

        try
        {
            await artworkService.UploadForProductAsync(productId, file, cancellationToken);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }
}
