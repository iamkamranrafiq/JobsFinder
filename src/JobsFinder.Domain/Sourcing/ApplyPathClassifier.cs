using System.Text.RegularExpressions;

namespace JobsFinder.Domain.Sourcing;

/// <summary>A posting's apply link and how it can be used. The URL is kept only for http(s) and mailto links.</summary>
public sealed record ApplyTarget(Uri? Url, ApplyPath Path);

/// <summary>Works out the apply path from a posting's apply link (F0.2-AC3).</summary>
public static partial class ApplyPathClassifier
{
    private const int EmailMaxLength = 320;

    private static readonly ApplyTarget None = new(null, ApplyPath.Unknown);

    // The ATS boards named in spec 3.11.
    private static readonly string[] AtsDomains =
        ["greenhouse.io", "lever.co", "ashbyhq.com", "workable.com", "smartrecruiters.com"];

    private static readonly string[] NativeDomains = ["linkedin.com", "indeed.com"];

    /// <summary>Accepts a URL, a mailto link or a bare email address; anything else is Unknown without a URL.</summary>
    public static ApplyTarget Classify(string? link)
    {
        var text = link?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        if (text.Length <= EmailMaxLength && BareEmailPattern().IsMatch(text))
        {
            text = $"mailto:{text}";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var url))
        {
            return None;
        }

        if (url.Scheme == Uri.UriSchemeMailto)
        {
            return url.UserInfo.Length > 0 && url.Host.Length > 0 ? new ApplyTarget(url, ApplyPath.Email) : None;
        }

        return url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps
            ? new ApplyTarget(url, ClassifyHost(url.Host.TrimEnd('.')))
            : None;
    }

    private static ApplyPath ClassifyHost(string host) =>
        AtsDomains.Any(d => IsDomainOrSubdomain(host, d)) ? ApplyPath.AtsUrl
        : NativeDomains.Any(d => IsDomainOrSubdomain(host, d)) ? ApplyPath.NativeApply
        : ApplyPath.Unknown;

    // Whole labels only: "evilgreenhouse.io" and "greenhouse.io.evil.com" must not match.
    private static bool IsDomainOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BareEmailPattern();
}
