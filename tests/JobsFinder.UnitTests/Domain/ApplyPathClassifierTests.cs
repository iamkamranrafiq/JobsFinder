using JobsFinder.Domain.Sourcing;

namespace JobsFinder.UnitTests.Domain;

public class ApplyPathClassifierTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/123?gh_src=x")]
    [InlineData("https://job-boards.eu.greenhouse.io/acme/jobs/123")]
    [InlineData("http://greenhouse.io/acme")]
    [InlineData("HTTPS://BOARDS.GREENHOUSE.IO/acme/jobs/123")]
    [InlineData("https://boards.greenhouse.io./acme/jobs/123")]
    [InlineData("  https://boards.greenhouse.io/acme/jobs/123  ")]
    public void Classify_GreenhouseHosts_ReturnsAtsUrl_F0_2_AC3(string link)
    {
        var target = ApplyPathClassifier.Classify(link);

        Assert.Equal(ApplyPath.AtsUrl, target.Path);
        Assert.NotNull(target.Url);
    }

    [Theory]
    [InlineData("https://evilgreenhouse.io/acme/jobs/123")]
    [InlineData("https://greenhouse.io.evil.com/acme/jobs/123")]
    [InlineData("https://greenhouse.io@evil.com/acme/jobs/123")]
    [InlineData("https://evil.com/boards.greenhouse.io/jobs/123")]
    [InlineData("https://evil.com\\@boards.greenhouse.io/jobs/123")]
    [InlineData("https://acme.com/careers?gh_jid=123")]
    public void Classify_LookalikeHosts_ReturnsUnknown_F0_2_AC3(string link) =>
        Assert.Equal(ApplyPath.Unknown, ApplyPathClassifier.Classify(link).Path);

    [Theory]
    [InlineData("https://jobs.lever.co/acme/1", ApplyPath.AtsUrl)]
    [InlineData("https://jobs.ashbyhq.com/acme/1", ApplyPath.AtsUrl)]
    [InlineData("https://apply.workable.com/acme/j/1", ApplyPath.AtsUrl)]
    [InlineData("https://jobs.smartrecruiters.com/acme/1", ApplyPath.AtsUrl)]
    [InlineData("mailto:jobs@acme.com?subject=Apply", ApplyPath.Email)]
    [InlineData("jobs@acme.com", ApplyPath.Email)]
    [InlineData("https://www.linkedin.com/jobs/view/1", ApplyPath.NativeApply)]
    [InlineData("https://uk.indeed.com/viewjob?jk=1", ApplyPath.NativeApply)]
    [InlineData("https://acme.com/careers/1", ApplyPath.Unknown)]
    public void Classify_OtherLinks_ReturnsPath_F0_2_BR2(string link, ApplyPath expected)
    {
        var target = ApplyPathClassifier.Classify(link);

        Assert.Equal(expected, target.Path);
        Assert.NotNull(target.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("not a link")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:")]
    public void Classify_UnusableLinks_ReturnsUnknownWithoutUrl_F0_2_BR2(string? link)
    {
        var target = ApplyPathClassifier.Classify(link);

        Assert.Equal(ApplyPath.Unknown, target.Path);
        Assert.Null(target.Url);
    }

    [Fact]
    public void Classify_BareEmail_BecomesMailtoUrl_F0_2_BR2() =>
        Assert.Equal("mailto:jobs@acme.com", ApplyPathClassifier.Classify("jobs@acme.com").Url!.OriginalString);
}
