using JobsFinder.Domain.Applying;
using JobsFinder.Domain.Common;

namespace JobsFinder.UnitTests.Domain;

[Trait("REQ", "F0.3-AC1")]
public sealed class ApplyRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ApplyFieldResult Filled = ApplyFieldResult.Filled(1, "Email", true, "contact.email");
    private static readonly ApplyFieldResult Open = ApplyFieldResult.NeedsHuman(1, "Colour", true, NeedsHumanReason.UnknownQuestion);

    [Fact]
    public void Completed_NothingOpenAndCvAttached_IsPrepared_F0_3_AC1()
    {
        var run = ApplyRun.Completed(Facts(Filled, Open with { Required = false }));

        Assert.Equal((ApplyRunStatus.Prepared, (ApplyReason?)null), (run.Status, run.Reason));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public void Completed_RequiredFieldOpen_IsNeverPrepared_F0_3_BR2()
    {
        var run = ApplyRun.Completed(Facts(Filled, Open));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public void Completed_OptionalFieldHoldsWhatNobodyChose_IsNeverPrepared_F0_3_BR2()
    {
        var run = ApplyRun.Completed(Facts(Filled, ApplyFieldResult.NeedsHuman(1, "Colour", false, NeedsHumanReason.NotCleared)));

        Assert.Equal(ApplyReason.UnknownField, run.Reason);
    }

    [Fact]
    public void Completed_CvMissing_IsNeverPrepared_F0_3_AC1() =>
        Assert.Equal(ApplyReason.CvNotAttached, ApplyRun.Completed(Facts(Filled) with { CvAttached = false }).Reason);

    [Fact]
    public void Factories_ImpossibleFacts_Throw_F0_3_AC1()
    {
        Assert.Throws<DomainException>(() => ApplyRun.Completed(Facts(Filled) with { StepCount = 0 }));
        Assert.Throws<DomainException>(() => ApplyRun.Completed(Facts(Filled) with { CandidateProfileId = Guid.Empty }));
        Assert.Throws<DomainException>(() => ApplyRun.StoppedAtUnknownField(Facts(Filled)));
        Assert.Throws<DomainException>(() => ApplyRun.Failed(Facts(Filled), ApplyReason.UnknownField));
        Assert.Throws<DomainException>(() => ApplyRun.Failed(Facts(Filled) with { StepCount = -1 }, ApplyReason.PageLoad));
    }

    [Fact]
    public void Screenshot_EmptyTooLargeOrNowhere_Throws_F0_3_AC1()
    {
        Assert.Throws<DomainException>(() => ApplyScreenshot.Create(Guid.NewGuid(), 1, []));
        Assert.Throws<DomainException>(() => ApplyScreenshot.Create(Guid.NewGuid(), 1, new byte[ApplyScreenshot.MaxSizeBytes + 1]));
        Assert.Throws<DomainException>(() => ApplyScreenshot.Create(Guid.NewGuid(), 0, [1]));
        Assert.Throws<DomainException>(() => ApplyScreenshot.Create(Guid.Empty, 1, [1]));
    }

    [Fact]
    public void Status_HasNoSubmittedState_F0_3_AC1() =>
        Assert.Equal(["Prepared", "NeedsYou", "Failed"], Enum.GetNames<ApplyRunStatus>());

    [Theory]
    [InlineData("https://boards.greenhouse.io/acme/jobs/1", true)]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/1?gh_src=x#app", true)]
    [InlineData("https://boards.eu.greenhouse.io/acme/jobs/1", true)]
    [InlineData("HTTPS://JOB-BOARDS.EU.GREENHOUSE.IO:443/acme", true)]
    [InlineData("http://boards.greenhouse.io/acme/jobs/1", false)]
    [InlineData("https://boards.greenhouse.io:444/acme/jobs/1", false)]
    [InlineData("https://user:pass@boards.greenhouse.io/acme", false)]
    [InlineData("https://user@boards.greenhouse.io/acme", false)]
    [InlineData("https://boards.greenhouse.io@evil.com/acme", false)]
    [InlineData("https://boards.greenhouse.io.evil.com/", false)]
    [InlineData("https://boards.greenhouse.io./acme", false)]
    [InlineData("https://greenhouse.io/", false)]
    [InlineData("https://app.greenhouse.io/", false)]
    [InlineData("https://boards.greenhouse.io%2eevil.com/", false)]
    [InlineData("https://bоards.greenhouse.io/", false)]
    [InlineData("https://[::1]/", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("/relative", false)]
    public void GreenhouseApplyUrl_IsAllowed_F0_3_AC1(string url, bool allowed) =>
        Assert.Equal(allowed, Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) && GreenhouseApplyUrl.IsAllowed(uri));

    private static ApplyRunFacts Facts(params ApplyFieldResult[] fields) =>
        new(ApplyRun.NewId(), Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddSeconds(20), 1, true, fields);
}
