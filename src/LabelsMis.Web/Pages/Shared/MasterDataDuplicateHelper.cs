namespace LabelsMis.Web.Pages.Shared;

internal static class MasterDataDuplicateHelper
{
    public static string DuplicateCode(string code, int maxLength = 50)
    {
        const string suffix = "-COPY";
        var trimmed = code.Trim();
        if (trimmed.Length + suffix.Length <= maxLength)
        {
            return trimmed + suffix;
        }

        return trimmed[..(maxLength - suffix.Length)] + suffix;
    }

    public static string DuplicateDescription(string description, int maxLength = 500)
    {
        const string suffix = " (copy)";
        var trimmed = description.Trim();
        if (trimmed.Length + suffix.Length <= maxLength)
        {
            return trimmed + suffix;
        }

        return trimmed[..(maxLength - suffix.Length)] + suffix;
    }
}
