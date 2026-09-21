using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Dies;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Dies;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Dies;

[Authorize(Policy = MasterDataPolicies.Read)]
public class IndexModel(DieService dieService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    [Display(Name = "Across")]
    public decimal? LabelAcrossIn { get; set; }

    [BindProperty(SupportsGet = true)]
    [Display(Name = "Around")]
    public decimal? LabelAroundIn { get; set; }

    [BindProperty(SupportsGet = true)]
    [Display(Name = "Tolerance")]
    public decimal ToleranceIn { get; set; } = DieSizeMatcher.DefaultToleranceIn;

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool IncludeInactive { get; set; }

    public PagedResult<DieListItem> Result { get; private set; } = null!;

    /// <summary>The active size-proximity search, or null when the list is the plain text search.</summary>
    public DieSizeSearch? SizeSearch { get; private set; }

    /// <summary>Why the size fields didn't produce a size search; the plain list is shown underneath.</summary>
    public string? SizeSearchError { get; private set; }

    public bool CanEdit => currentUser.CanEditMasterData;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Search"] = Search;
        ViewData["IncludeInactive"] = IncludeInactive;
        SizeSearch = BuildSizeSearch();
        Result = await dieService.ListAsync(Search, SizeSearch, Sort, PageNumber, 20, IncludeInactive, cancellationToken);
    }

    private DieSizeSearch? BuildSizeSearch()
    {
        // Non-numeric text never reaches the decimal properties; model binding leaves the error
        // (worded by the app's ModelBindingMessageProvider) under the property's key.
        var bindingErrors = new[] { nameof(LabelAcrossIn), nameof(LabelAroundIn), nameof(ToleranceIn) }
            .Where(key => ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0)
            .SelectMany(key => ModelState[key]!.Errors.Select(e => e.ErrorMessage))
            .ToList();
        if (bindingErrors.Count > 0)
        {
            SizeSearchError = string.Join(" ", bindingErrors);
            return null;
        }

        if (LabelAcrossIn is null && LabelAroundIn is null)
        {
            return null;
        }

        if (LabelAcrossIn is null || LabelAroundIn is null)
        {
            SizeSearchError = "Enter both Across and Around to search by size.";
            return null;
        }

        if (LabelAcrossIn <= 0 || LabelAroundIn <= 0)
        {
            SizeSearchError = "Across and Around must be greater than zero.";
            return null;
        }

        if (!DieSizeMatcher.ToleranceChoicesIn.Contains(ToleranceIn))
        {
            ToleranceIn = DieSizeMatcher.DefaultToleranceIn;
        }

        return new DieSizeSearch(LabelAcrossIn.Value, LabelAroundIn.Value, ToleranceIn);
    }
}
