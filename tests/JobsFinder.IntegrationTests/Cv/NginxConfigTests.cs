using System.Globalization;
using System.Text.RegularExpressions;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>
/// Guards the web container's reverse proxy (client/nginx.conf) against limits that would stop CV uploads
/// before they reach the API in the docker compose stack. No Docker needed: it reads the config file.
/// </summary>
public partial class NginxConfigTests
{
    private static string ApiLocationBlock()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobsFinder.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var config = File.ReadAllText(Path.Combine(dir.FullName, "client", "nginx.conf"));
        var block = ApiLocationPattern().Match(config);
        Assert.True(block.Success, "nginx.conf has no 'location /api/' block.");
        return block.Groups["body"].Value;
    }

    [Fact]
    public void NginxApiLocation_BodyLimit_AllowsMaxCvUploadPlusOverhead_F0_1_AC1()
    {
        var match = BodySizePattern().Match(ApiLocationBlock());
        Assert.True(match.Success, "client_max_body_size is not set for /api/ (nginx default is 1m).");

        var megabytes = long.Parse(match.Groups["mb"].Value, CultureInfo.InvariantCulture);
        Assert.True(
            megabytes * 1024 * 1024 > CvDocument.MaxSizeBytes,
            $"client_max_body_size {megabytes}m must exceed the 5 MB CV limit plus multipart overhead.");
    }

    [Fact]
    public void NginxApiLocation_ReadTimeout_ExceedsCvParseBudget_F0_1_AC1()
    {
        var match = ReadTimeoutPattern().Match(ApiLocationBlock());
        Assert.True(match.Success, "proxy_read_timeout is not set for /api/ (nginx default is 60s).");

        var seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        Assert.True(
            TimeSpan.FromSeconds(seconds) > new CvParsingSettings().ParseBudget + TimeSpan.FromSeconds(10),
            $"proxy_read_timeout {seconds}s must comfortably exceed the CV parse budget.");
    }

    [GeneratedRegex(@"location\s+/api/\s*\{(?<body>[^}]*)\}")]
    private static partial Regex ApiLocationPattern();

    [GeneratedRegex(@"client_max_body_size\s+(?<mb>\d+)m\s*;")]
    private static partial Regex BodySizePattern();

    [GeneratedRegex(@"proxy_read_timeout\s+(?<s>\d+)s\s*;")]
    private static partial Regex ReadTimeoutPattern();
}
