namespace LabelsMis.Web.Services;

/// <summary>
/// "Today" for the person looking at the page. The server runs in UTC, so in the shop's evening
/// <see cref="DateTime.Today"/> is already tomorrow and due-date flags would flip early. site.js
/// stores the browser's IANA time zone in the <see cref="CookieName"/> cookie; this reads it and
/// falls back to the server clock when it is missing or unknown.
/// </summary>
public sealed class ClientClock(IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider)
{
    public const string CookieName = "ls_tz";

    public DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>The current local time in the browser's zone (server local time as a fallback).</summary>
    public DateTime Now
    {
        get
        {
            var utcNow = timeProvider.GetUtcNow().UtcDateTime;
            return TimeZone is { } zone
                ? TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone)
                : utcNow.ToLocalTime();
        }
    }

    private TimeZoneInfo? TimeZone
    {
        get
        {
            var id = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            {
                return null;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return null;
            }
        }
    }
}
