using JobsFinder.Domain.Sourcing;

namespace JobsFinder.IntegrationTests.QA.JF7;

/// <summary>Apply-path classification through the whole pipeline: fake Apify item -> handler -> database.</summary>
[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class ApplyPathQaTests(ApiFactory factory)
{
    private async Task<ApplyPath> StoredPathAsync(string? link, bool omitLink = false, bool nonWeb = false)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("ap-1", url: omitLink ? null : link));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess, run.Error?.Message);
        var posting = Assert.Single(await host.PostingsAsync());
        var kept = posting.ApplyUrl?.Scheme is "http" or "https" or "mailto";
        Assert.True(posting.ApplyUrl is null || kept, $"stored a non-web/mail URL: {posting.ApplyUrl}");
        if (nonWeb)
        {
            Assert.Null(posting.ApplyUrl);
        }

        return posting.ApplyPath;
    }

    [Theory]
    [Trait("REQ", "F0.2-AC3")]
    [InlineData("https://boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://boards.eu.greenhouse.io/acme/jobs/123")]
    [InlineData("HTTPS://BOARDS.GREENHOUSE.IO/acme/jobs/123")]
    [InlineData("http://boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://boards.greenhouse.io./acme/jobs/123")]
    [InlineData("  https://boards.greenhouse.io/acme/jobs/123  ")]
    public async Task TC08_GreenhouseLink_IsAtsUrl(string link) =>
        Assert.Equal(ApplyPath.AtsUrl, await StoredPathAsync(link));

    [Theory]
    [Trait("REQ", "F0.2-AC3")]
    [InlineData("https://jobs.lever.co/acme/abc")]
    [InlineData("https://jobs.ashbyhq.com/acme/abc")]
    [InlineData("https://apply.workable.com/acme/j/ABC/")]
    [InlineData("https://jobs.smartrecruiters.com/Acme/123")]
    public async Task TC09_OtherKnownAtsLinks_AreAtsUrl(string link) =>
        Assert.Equal(ApplyPath.AtsUrl, await StoredPathAsync(link));

    [Theory]
    [Trait("REQ", "F0.2-AC3")]
    [InlineData("https://evilgreenhouse.io/acme/jobs/1")]
    [InlineData("https://greenhouse.io.evil.example/acme/jobs/1")]
    [InlineData("https://evil.example/?u=https://boards.greenhouse.io/acme/jobs/1")]
    [InlineData("https://evil.example/boards.greenhouse.io/acme")]
    [InlineData("https://boards.greenhouse.io@evil.example/acme")]
    [InlineData("https://www.acme.com/careers/job?gh_jid=12345")]
    [InlineData("https://example.org/jobs/1")]
    public async Task TC10_LookalikeOrEmbeddedLinks_AreNotAtsUrl(string link) =>
        Assert.NotEqual(ApplyPath.AtsUrl, await StoredPathAsync(link));

    [Theory]
    [Trait("REQ", "F0.2-AC3")]
    [InlineData("https://www.acme.com/careers/job?gh_jid=12345")]
    [InlineData("https://acme.com/jobs/embed?for=acme&gh_jid=77")]
    public async Task TC10b_CompanyPageEmbeddingGreenhouse_IsUnknown_D7(string link) =>
        Assert.Equal(ApplyPath.Unknown, await StoredPathAsync(link));

    [Theory]
    [Trait("REQ", "F0.2-BR2")]
    [InlineData("mailto:jobs@acme.example", ApplyPath.Email)]
    [InlineData("jobs@acme.example", ApplyPath.Email)]
    [InlineData("https://www.linkedin.com/jobs/view/123", ApplyPath.NativeApply)]
    [InlineData("https://de.indeed.com/viewjob?jk=abc", ApplyPath.NativeApply)]
    public async Task TC11_EmailAndNativeLinks(string link, ApplyPath expected) =>
        Assert.Equal(expected, await StoredPathAsync(link));

    [Theory]
    [Trait("REQ", "F0.2-BR2")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://files.example/job.pdf")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    public async Task TC12_UnusableLink_IsUnknownWithoutUrl_AndPostingStillStored(string link) =>
        Assert.Equal(ApplyPath.Unknown, await StoredPathAsync(link, nonWeb: true));

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC12b_MissingLink_IsUnknownWithoutUrl() =>
        Assert.Equal(ApplyPath.Unknown, await StoredPathAsync(null, omitLink: true, nonWeb: true));
}
