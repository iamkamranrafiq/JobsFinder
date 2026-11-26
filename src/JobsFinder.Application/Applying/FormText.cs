using System.Globalization;
using System.Text;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Applying;

/// <summary>Cleans and compares untrusted text from a form page.</summary>
public static class FormText
{
    /// <summary>One line without control or invisible formatting characters, at most <paramref name="maxLength"/> long.</summary>
    public static string Clean(string? value, int maxLength)
    {
        var text = new StringBuilder();
        var pendingSpace = false;
        foreach (var c in PostingText.Clean(value))
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = text.Length > 0;
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                if (pendingSpace)
                {
                    text.Append(' ');
                    pendingSpace = false;
                }

                text.Append(c);
            }

            if (text.Length >= maxLength)
            {
                break;
            }
        }

        // Cutting may have split a surrogate pair.
        return PostingText.Clean(text.ToString(0, Math.Min(text.Length, maxLength)));
    }

    /// <summary>Lower-case words without accents and punctuation, e.g. "Résumé/CV *" becomes "resume cv".</summary>
    public static string LabelKey(string? label)
    {
        var key = new StringBuilder();
        var pendingSpace = false;
        foreach (var c in WithoutAccents(label))
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && key.Length > 0)
                {
                    key.Append(' ');
                }

                pendingSpace = false;
                key.Append(char.ToLowerInvariant(c));
            }
            else if (c is not ('\'' or '’'))
            {
                pendingSpace = true;
            }
        }

        var words = key.ToString();
        foreach (var marker in (string[])[" required", " optional"])
        {
            if (words.EndsWith(marker, StringComparison.Ordinal))
            {
                words = words[..^marker.Length];
            }
        }

        return words;
    }

    /// <summary>For comparing an option with an answer: case, accents and spacing are ignored, nothing else.</summary>
    public static string OptionKey(string? option) =>
        string.Join(' ', WithoutAccents(option).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string WithoutAccents(string? value)
    {
        var text = new StringBuilder();
        foreach (var c in PostingText.Clean(value).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                text.Append(c);
            }
        }

        return text.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>F0.3-BR2: an option is chosen only when it says the same as the answer.</summary>
public static class OptionMatcher
{
    /// <summary>The index of the only option equal to the value, or null when there is none or more than one.</summary>
    public static int? FindOnly(IReadOnlyList<string> options, EvidenceValue value)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(value);

        var key = FormText.OptionKey(value.Value);
        int? found = null;
        for (var i = 0; i < options.Count; i++)
        {
            if (FormText.OptionKey(options[i]) != key)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = i;
        }

        return found;
    }
}
