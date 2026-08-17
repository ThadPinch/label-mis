using LabelsMis.Web.Pages.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace LabelsMis.Web.Tests;

/// <summary>
/// <see cref="BackNavExtensions.RedirectToListPage"/> turns the query string that back-nav.js posts
/// as a hidden field into route values on the redirect, so a delete/deactivate lands on the same
/// filtered view of the list the user came from.
/// </summary>
public class BackNavExtensionsTests
{
    private sealed class TestPageModel : PageModel;

    private static TestPageModel PageWithRequest(Action<HttpRequest>? configure = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        configure?.Invoke(http.Request);

        var actionContext = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new TestPageModel
        {
            PageContext = new PageContext(actionContext) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), actionContext.ModelState) },
        };
    }

    private static void SetForm(HttpRequest request, params (string Key, string Value)[] fields)
    {
        request.ContentType = "application/x-www-form-urlencoded";
        request.Form = new FormCollection(fields
            .GroupBy(f => f.Key)
            .ToDictionary(g => g.Key, g => new StringValues(g.Select(f => f.Value).ToArray())));
    }

    [Fact]
    public void Restores_remembered_query_as_route_values()
    {
        var page = PageWithRequest(r => SetForm(r,
            ("id", "abc"),
            (BackNavExtensions.ReturnQueryField, "?Search=acme%20labels&Status=InProduction&Sort=number_desc&pageNumber=2")));

        var result = page.RedirectToListPage();

        result.PageName.Should().Be("Index");
        result.RouteValues.Should().NotBeNull();
        result.RouteValues!.Should().Contain(new KeyValuePair<string, object?>("Search", "acme labels"));
        result.RouteValues.Should().Contain(new KeyValuePair<string, object?>("Status", "InProduction"));
        result.RouteValues.Should().Contain(new KeyValuePair<string, object?>("Sort", "number_desc"));
        result.RouteValues.Should().Contain(new KeyValuePair<string, object?>("pageNumber", "2"));
        result.RouteValues.Should().NotContainKey("id", "only the remembered query is carried, not other form fields");
    }

    [Fact]
    public void Repeated_keys_become_arrays()
    {
        var page = PageWithRequest(r => SetForm(r, (BackNavExtensions.ReturnQueryField, "?Tag=a&Tag=b")));

        var result = page.RedirectToListPage();

        result.RouteValues!["Tag"].Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void Uses_the_page_name_it_is_given()
    {
        var page = PageWithRequest(r => SetForm(r, (BackNavExtensions.ReturnQueryField, "?Search=x")));

        var result = page.RedirectToListPage("./Index");

        result.PageName.Should().Be("./Index");
        result.RouteValues!["Search"].Should().Be("x");
    }

    [Fact]
    public void Falls_back_to_plain_list_when_nothing_was_posted()
    {
        PageWithRequest(r => SetForm(r, ("id", "abc")))
            .RedirectToListPage().RouteValues.Should().BeNull("field absent");

        PageWithRequest(r => SetForm(r, (BackNavExtensions.ReturnQueryField, "")))
            .RedirectToListPage().RouteValues.Should().BeNull("field empty");

        PageWithRequest(r => SetForm(r, (BackNavExtensions.ReturnQueryField, "?")))
            .RedirectToListPage().RouteValues.Should().BeNull("query has no keys");
    }

    [Fact]
    public void Ignores_requests_that_are_not_forms()
    {
        var page = PageWithRequest(r => r.ContentType = "application/json");

        var result = page.RedirectToListPage();

        result.PageName.Should().Be("Index");
        result.RouteValues.Should().BeNull();
    }
}
