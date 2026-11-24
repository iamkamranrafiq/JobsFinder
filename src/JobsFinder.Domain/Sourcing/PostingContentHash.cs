using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobsFinder.Domain.Sourcing;

/// <summary>
/// Exact-duplicate key (F0.2-BR3): SHA-256 over company, title, location and the whole description, ignoring
/// case, whitespace and Unicode compatibility differences. F2.2 refines the description part.
/// </summary>
public static partial class PostingContentHash
{
    public const int Length = 64;

    // Removed from the values first, so text cannot shift from one field into the next.
    private const char Separator = '\u001F';

    public static string Compute(string company, string title, string? location, string description)
    {
        var content = string.Join(
            Separator,
            Normalise(company),
            Normalise(title),
            Normalise(location),
            Normalise(description));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static string Normalise(string? value) =>
        WhitespacePattern()
            .Replace(ReplaceNonCharacters(PostingText.Clean(value)).Normalize(NormalizationForm.FormKC).Replace(Separator, ' '), " ")
            .Trim()
            .ToUpperInvariant();

    // string.Normalize rejects Unicode non-characters (e.g. U+FFFE), which PostgreSQL can store.
    private static string ReplaceNonCharacters(string value) =>
        NonCharacterPattern().Replace(value, "�");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[﷐-﷯￾￿]")]
    private static partial Regex NonCharacterPattern();
}
