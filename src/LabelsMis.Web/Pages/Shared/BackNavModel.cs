namespace LabelsMis.Web.Pages.Shared;

public record BackNavModel(string Text = "Back", string Page = "Index")
{
    public IReadOnlyDictionary<string, object?>? RouteValues { get; init; }
}
