namespace LabelsMis.Infrastructure.QuickBooks;

/// <summary>Intuit's addresses and API version. The defaults are the real ones; the section exists
/// so tests and local smokes can point the clients at a stub server.</summary>
public class QuickBooksOptions
{
    public const string SectionName = "QuickBooks";

    public string AuthorizeUrl { get; set; } = "https://appcenter.intuit.com/connect/oauth2";
    public string TokenUrl { get; set; } = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
    public string RevokeUrl { get; set; } = "https://developer.api.intuit.com/v2/oauth2/tokens/revoke";
    public string SandboxApiBase { get; set; } = "https://sandbox-quickbooks.api.intuit.com";
    public string ProductionApiBase { get; set; } = "https://quickbooks.api.intuit.com";

    /// <summary>Accounting only. The Intuit app may have more scopes switched on; LabelShop asks
    /// for this one.</summary>
    public string Scope { get; set; } = "com.intuit.quickbooks.accounting";

    /// <summary>Intuit ignores anything below 75 and answers as 75.</summary>
    public int MinorVersion { get; set; } = 75;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Waits between attempts of a read or query that failed for a passing reason (429,
    /// 5xx, no answer). Writes are never repeated here: the caller owns that, with a request id.</summary>
    public int[] ReadRetryDelaysMs { get; set; } = [500, 2000];
}
