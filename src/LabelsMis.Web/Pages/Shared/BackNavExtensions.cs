using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace LabelsMis.Web.Pages.Shared;

/// <summary>
/// Server side of the back-nav query memory (see wwwroot/js/back-nav.js and Shared/_BackNav.cshtml).
/// The browser remembers, per tab, the search/filter/sort/page query of each list page and posts it
/// back as a hidden field on the forms of pages that have a back link. Handlers that leave the current
/// record (delete, deactivate, split) can then send the user back to the same view of the list.
/// </summary>
public static class BackNavExtensions
{
    /// <summary>Hidden form field added by back-nav.js: the query string remembered for the back link's target list.</summary>
    public const string ReturnQueryField = "backNavReturnQuery";

    /// <summary>
    /// Redirects to a list page (default: the sibling <c>Index</c>), restoring the search the user came
    /// from if the browser posted one; otherwise the plain list. The target page is always the one named
    /// here — only its query string comes from the request.
    /// </summary>
    public static RedirectToPageResult RedirectToListPage(this PageModel page, string pageName = "Index")
        => page.RedirectToPage(pageName, ReadReturnQuery(page.Request));

    private static RouteValueDictionary? ReadReturnQuery(HttpRequest request)
    {
        if (!request.HasFormContentType) return null;

        var raw = request.Form[ReturnQueryField].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var values = new RouteValueDictionary();
        foreach (var (key, value) in QueryHelpers.ParseQuery(raw))
        {
            if (string.IsNullOrEmpty(key)) continue;
            values[key] = value.Count == 1 ? value[0] : value.ToArray();
        }

        return values.Count > 0 ? values : null;
    }
}
