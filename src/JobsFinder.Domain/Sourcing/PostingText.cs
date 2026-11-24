using System.Text;

namespace JobsFinder.Domain.Sourcing;

/// <summary>Cleans scraped text so one bad character cannot fail a whole save.</summary>
public static class PostingText
{
    /// <summary>Trims and drops what PostgreSQL text cannot store: U+0000 and lone surrogates.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var clean = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                clean.Append(c).Append(value[++i]);
            }
            else if (c != '\0' && !char.IsSurrogate(c))
            {
                clean.Append(c);
            }
        }

        return clean.ToString().Trim();
    }

    /// <summary>As <see cref="Clean"/>, with blank text becoming null.</summary>
    public static string? CleanOrNull(string? value) => Clean(value) is { Length: > 0 } text ? text : null;
}
