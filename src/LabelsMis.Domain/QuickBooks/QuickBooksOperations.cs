namespace LabelsMis.Domain.QuickBooks;

/// <summary>The names the QuickBooks log files each attempt under. Stored as text, so a name is
/// never changed once rows exist; add new ones instead.</summary>
public static class QuickBooksOperations
{
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string ListsRefresh = "lists.refresh";
    public const string PoFieldFind = "pofield.find";
    public const string TokenRefresh = "token.refresh";
    public const string CompanyRead = "company.read";
    public const string PreferencesRead = "preferences.read";

    /// <summary>What the log page calls an operation.</summary>
    public static string Describe(string operation) => operation switch
    {
        Connect => "Connect to QuickBooks",
        Disconnect => "Disconnect",
        ListsRefresh => "Refresh the lists",
        PoFieldFind => "Find the PO field",
        TokenRefresh => "Refresh the sign-in",
        CompanyRead => "Read the company name",
        PreferencesRead => "Read the company settings",
        _ => operation
    };
}
