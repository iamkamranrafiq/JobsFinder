namespace JobsFinder.BrowserAutomation;

/// <summary>The stored file name is whatever the client sent at upload, so it is cleaned before a page sees it.</summary>
internal static class CvFileName
{
    private const string Fallback = "cv";

    /// <summary>The last part of the name, without path and control characters.</summary>
    public static string Clean(string fileName)
    {
        var name = fileName[(fileName.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        var clean = new string([.. name.Where(c => !char.IsControl(c))]).Trim();
        return clean.Length == 0 ? Fallback : clean;
    }

    /// <summary>Whether a file input with this <c>accept</c> attribute takes the file. Empty takes everything.</summary>
    public static bool IsAccepted(string accept, string fileName, string contentType)
    {
        var kinds = accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return kinds.Length == 0
            || kinds.Any(kind =>
                kind is "*" or "*/*"
                || (kind.StartsWith('.') && fileName.EndsWith(kind, StringComparison.OrdinalIgnoreCase))
                || kind.Equals(contentType, StringComparison.OrdinalIgnoreCase));
    }
}
