using System.Text.Json;

namespace JobsFinder.Infrastructure.Sourcing;

/// <summary>Checks the configured actor input: a JSON object without user credentials or cookies (F0.2-BR4).</summary>
internal static class ApifyInput
{
    private const string Empty = "{}";

    private static readonly string[] ForbiddenFragments = ["cookie", "password", "passwd", "credential"];

    // Whole names only: "sessionPoolName" or "boardToken" are normal scraper settings.
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.Ordinal)
    {
        "session", "sessionid", "sessiontoken", "username", "login", "loginemail", "authorization", "accesstoken", "liat",
    };

    // Headers and cookies are often given as pairs, e.g. {"name": "Cookie", "value": "..."}.
    private static readonly HashSet<string> PairNameProperties = new(StringComparer.Ordinal) { "name", "key" };

    private static readonly HashSet<string> PairValueProperties = new(StringComparer.Ordinal) { "value", "values" };

    /// <summary>Returns the JSON to send, or null when the input may not be used.</summary>
    public static string? Validate(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && !HasForbiddenName(document.RootElement)
                ? inputJson
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasForbiddenName(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => IsForbiddenPair(element)
            || element.EnumerateObject().Any(p => IsForbidden(p.Name) || HasForbiddenName(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(HasForbiddenName),
        _ => false,
    };

    /// <summary>
    /// Only the name half of a pair counts, so values may mention these words (e.g. a search keyword).
    /// </summary>
    private static bool IsForbiddenPair(JsonElement element) =>
        element.EnumerateObject().Any(p => PairValueProperties.Contains(Letters(p.Name)))
        && element.EnumerateObject().Any(p => PairNameProperties.Contains(Letters(p.Name)) && IsForbiddenPairName(p.Value));

    private static bool IsForbiddenPairName(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            // Header and cookie names have no spaces; "Cookie Engineer" is ordinary text.
            var name = value.GetString()!.Trim();
            return !name.Any(char.IsWhiteSpace) && IsForbidden(name);
        }
        catch (InvalidOperationException)
        {
            // Undecodable text (an escaped lone surrogate) cannot be checked, so it is not sent.
            return true;
        }
    }

    private static bool IsForbidden(string name)
    {
        var letters = Letters(name);
        return ForbiddenNames.Contains(letters)
            || ForbiddenFragments.Any(f => letters.Contains(f, StringComparison.Ordinal));
    }

    // "li_at", "session-id" and "userName" compare as "liat", "sessionid" and "username".
    private static string Letters(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
}
