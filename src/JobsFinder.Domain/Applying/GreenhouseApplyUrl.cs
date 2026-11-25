using System.Diagnostics.CodeAnalysis;

namespace JobsFinder.Domain.Applying;

/// <summary>
/// Which apply URLs the applier may open. Fixed in code on purpose: the URL comes from a scraped posting, and no
/// configuration may widen what a browser on this machine can be pointed at.
/// </summary>
public static class GreenhouseApplyUrl
{
    /// <summary>Pages may load their assets from this domain and its subdomains.</summary>
    public const string AssetDomain = "greenhouse.io";

    private static readonly string[] BoardHosts =
    [
        "boards.greenhouse.io",
        "job-boards.greenhouse.io",
        "boards.eu.greenhouse.io",
        "job-boards.eu.greenhouse.io",
    ];

    /// <summary>True only for https on the default port, without credentials, on one of the public board hosts.</summary>
    public static bool IsAllowed([NotNullWhen(true)] Uri? url) =>
        url is { IsAbsoluteUri: true }
        && url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && url.UserInfo.Length == 0
        && BoardHosts.Contains(url.IdnHost, StringComparer.OrdinalIgnoreCase);
}
