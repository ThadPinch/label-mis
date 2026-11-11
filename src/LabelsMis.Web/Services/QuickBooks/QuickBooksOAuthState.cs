using System.Security.Cryptography;
using System.Text;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>The "state" value of the OAuth round trip. Connect puts a random value in a short-lived
/// cookie and sends the same value to Intuit; the callback is honoured only when Intuit hands back
/// the value this browser was given, which stops another site from finishing a connection in
/// someone's session.</summary>
public static class QuickBooksOAuthState
{
    public const string CookieName = "qb_oauth_state";
    public const string CookiePath = "/quickbooks";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static string Issue(HttpContext context)
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        context.Response.Cookies.Append(CookieName, state, Options(context, Lifetime));
        return state;
    }

    /// <summary>True when <paramref name="presented"/> is the value issued to this browser. The
    /// cookie is removed either way: a state is good for one answer.</summary>
    public static bool Consume(HttpContext context, string? presented)
    {
        var issued = context.Request.Cookies[CookieName];
        context.Response.Cookies.Delete(CookieName, Options(context, null));
        return !string.IsNullOrEmpty(issued)
            && !string.IsNullOrEmpty(presented)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(issued), Encoding.UTF8.GetBytes(presented));
    }

    // Lax, not Strict: the answer arrives as a top-level navigation from Intuit's site, and a
    // Strict cookie would be left behind on exactly that request.
    private static CookieOptions Options(HttpContext context, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = CookiePath,
        MaxAge = maxAge,
        IsEssential = true
    };
}
