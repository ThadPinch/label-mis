namespace LabelsMis.Web.Pages.Shared;

/// <summary>A clickable column-header link that toggles between <c>key</c> and <c>key_desc</c>.
/// Rendered by Shared/_SortHeader inside the view's own &lt;th&gt;.</summary>
public record SortHeaderModel(
    string Label,
    string Key,
    string? CurrentSort,
    IReadOnlyDictionary<string, string?>? Route = null);
