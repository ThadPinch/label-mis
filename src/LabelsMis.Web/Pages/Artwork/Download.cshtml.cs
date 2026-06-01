using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Artwork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Artwork;

[Authorize(Policy = TransactionPolicies.ProductsRead)]
public class DownloadModel(ArtworkService artworkService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid productId, bool inline, CancellationToken cancellationToken)
    {
        var file = await artworkService.OpenForProductAsync(productId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        // inline = render in the browser (preview); otherwise download with the original file name.
        return inline
            ? File(file.Value.Stream, file.Value.ContentType)
            : File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }
}
