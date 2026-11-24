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
        JsonValueKind.Object => element.EnumerateObject().Any(p => IsForbidden(p.Name) || HasForbiddenName(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(HasForbiddenName),
        _ => false,
    };

    private static bool IsForbidden(string name)
    {
        // "li_at", "session-id" and "userName" compare as "liat", "sessionid" and "username".
        var letters = string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
        return ForbiddenNames.Contains(letters)
            || ForbiddenFragments.Any(f => letters.Contains(f, StringComparison.Ordinal));
    }
}
