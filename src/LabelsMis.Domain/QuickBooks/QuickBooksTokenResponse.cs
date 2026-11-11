using System.Text.Json;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>Intuit's answer to a code exchange or a refresh. Lifetimes are seconds from now.</summary>
public sealed record QuickBooksTokenResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    int RefreshTokenExpiresInSeconds,
    int? RefreshTokenHardExpiresInSeconds,
    string? IntuitTid)
{
    /// <summary>Reads the token endpoint's JSON. Throws <see cref="QuickBooksAuthException"/> when a
    /// token or its lifetime is missing, so a half answer never reaches storage.</summary>
    public static QuickBooksTokenResponse Parse(string json, string? intuitTid)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var access = Text(root, "access_token");
            var refresh = Text(root, "refresh_token");
            var expiresIn = Number(root, "expires_in");
            var refreshExpiresIn = Number(root, "x_refresh_token_expires_in");
            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh)
                || expiresIn is not > 0 || refreshExpiresIn is not > 0)
            {
                throw new QuickBooksAuthException(
                    "QuickBooks returned an incomplete sign-in answer.", null, null, intuitTid);
            }

            return new QuickBooksTokenResponse(
                access!,
                expiresIn.Value,
                refresh!,
                refreshExpiresIn.Value,
                Number(root, "x_refresh_token_hard_expires_in") is > 0 and var hard ? hard : null,
                intuitTid);
        }
        catch (JsonException ex)
        {
            throw new QuickBooksAuthException(
                "QuickBooks returned a sign-in answer that could not be read.", null, null, intuitTid, ex);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }
}
