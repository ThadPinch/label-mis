using System.Text.Json;

namespace LabelsMis.Domain.QuickBooks;

public enum QuickBooksErrorKind
{
    /// <summary>401: the access token was refused.</summary>
    Unauthorized,

    /// <summary>429: too many calls; wait and try again.</summary>
    Throttled,

    /// <summary>400: QuickBooks understood the request and said no (a business rule or a bad field).</summary>
    Validation,

    /// <summary>5xx: QuickBooks itself is having trouble.</summary>
    Server,

    /// <summary>No answer at all: timeout, DNS, connection reset.</summary>
    Network,

    Other
}

/// <summary>One entry of the Fault a failed Accounting API call carries.</summary>
public sealed record QuickBooksFaultError(string? Code, string? Message, string? Detail, string? Element);

/// <summary>An Accounting API call that did not succeed.</summary>
public class QuickBooksApiException : Exception
{
    public QuickBooksApiException(
        QuickBooksErrorKind kind,
        string message,
        int? statusCode,
        IReadOnlyList<QuickBooksFaultError> errors,
        string? intuitTid,
        Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        Errors = errors;
        IntuitTid = intuitTid;
    }

    public QuickBooksErrorKind Kind { get; }
    public int? StatusCode { get; }
    public IReadOnlyList<QuickBooksFaultError> Errors { get; }
    public string? IntuitTid { get; }

    /// <summary>The first fault's code, e.g. 6140 for a duplicate document number.</summary>
    public string? Code => Errors.Count > 0 ? Errors[0].Code : null;

    /// <summary>Worth trying again later without anyone changing anything.</summary>
    public bool IsTransient => Kind is QuickBooksErrorKind.Throttled or QuickBooksErrorKind.Server or QuickBooksErrorKind.Network;

    /// <summary>Builds the exception for a non-2xx answer. QuickBooks sends its Fault with either
    /// capitalisation (<c>Fault.Error[].Message</c> and <c>fault.error[].message</c>).</summary>
    public static QuickBooksApiException FromResponse(int statusCode, string? body, string? intuitTid)
    {
        var errors = ParseErrors(body);
        var kind = statusCode switch
        {
            401 => QuickBooksErrorKind.Unauthorized,
            429 => QuickBooksErrorKind.Throttled,
            400 => QuickBooksErrorKind.Validation,
            >= 500 => QuickBooksErrorKind.Server,
            _ => QuickBooksErrorKind.Other
        };

        string message;
        if (errors.Count > 0)
        {
            var first = errors[0];
            var text = string.IsNullOrWhiteSpace(first.Detail) || string.Equals(first.Detail, first.Message, StringComparison.Ordinal)
                ? first.Message
                : string.IsNullOrWhiteSpace(first.Message) ? first.Detail : $"{first.Message}: {first.Detail}";
            message = string.IsNullOrWhiteSpace(text) ? $"QuickBooks answered {statusCode}." : text!;
        }
        else
        {
            message = $"QuickBooks answered {statusCode}.";
        }

        return new QuickBooksApiException(kind, message, statusCode, errors, intuitTid);
    }

    public static QuickBooksApiException Network(string message, Exception inner) =>
        new(QuickBooksErrorKind.Network, message, null, [], null, inner);

    private static IReadOnlyList<QuickBooksFaultError> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!TryGet(document.RootElement, "Fault", out var fault) || !TryGet(fault, "Error", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var errors = new List<QuickBooksFaultError>();
            foreach (var item in list.EnumerateArray())
            {
                errors.Add(new QuickBooksFaultError(
                    Text(item, "code"), Text(item, "Message"), Text(item, "Detail"), Text(item, "element")));
            }

            return errors;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? Text(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>A call to Intuit's OAuth endpoints that did not succeed.</summary>
public class QuickBooksAuthException : Exception
{
    public QuickBooksAuthException(string message, string? error, int? statusCode, string? intuitTid, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        StatusCode = statusCode;
        IntuitTid = intuitTid;
    }

    /// <summary>OAuth's error word, e.g. <c>invalid_grant</c> or <c>invalid_client</c>.</summary>
    public string? Error { get; }
    public int? StatusCode { get; }
    public string? IntuitTid { get; }

    /// <summary>The refresh token or authorisation code was refused. Intuit also answers this way
    /// during some outages, so one refusal is not proof the connection is gone.</summary>
    public bool IsInvalidGrant => string.Equals(Error, "invalid_grant", StringComparison.OrdinalIgnoreCase);

    /// <summary>The client ID or secret is wrong.</summary>
    public bool IsInvalidClient => string.Equals(Error, "invalid_client", StringComparison.OrdinalIgnoreCase);

    public bool IsTransient => StatusCode is null or 429 or >= 500;

    /// <summary>Builds the exception from the token endpoint's error JSON
    /// (<c>{"error":"invalid_grant","error_description":"…"}</c>).</summary>
    public static QuickBooksAuthException FromResponse(int statusCode, string? body, string? intuitTid)
    {
        string? error = null;
        string? description = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    description = root.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                }
            }
            catch (JsonException)
            {
                // Not JSON (an HTML error page from a proxy); the status code is all there is.
            }
        }

        var message = (error, description) switch
        {
            (null, null) => $"QuickBooks sign-in answered {statusCode}.",
            (var word, null) => $"QuickBooks sign-in refused: {word}.",
            (null, var text) => $"QuickBooks sign-in refused: {text}.",
            var (word, text) => $"QuickBooks sign-in refused: {word} ({text})."
        };
        return new QuickBooksAuthException(message, error, statusCode, intuitTid);
    }
}

/// <summary>Raised when something needs QuickBooks and no company is connected (or the saved
/// sign-in stopped working). The message is written for the person at the screen.</summary>
public class QuickBooksNotConnectedException(string message) : Exception(message);
