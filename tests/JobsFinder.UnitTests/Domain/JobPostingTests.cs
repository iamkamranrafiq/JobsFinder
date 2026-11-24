using JobsFinder.Domain.Common;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.UnitTests.Domain;

public class JobPostingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static JobPosting Create(
        string source = "apify",
        string externalId = "gh-1",
        string company = "Acme",
        string title = "Engineer",
        string? location = "London",
        string description = "Build things.",
        string? applyLink = "https://boards.greenhouse.io/acme/jobs/1",
        DateTimeOffset? postedAt = null,
        DateTimeOffset? now = null) =>
        JobPosting.Create(source, externalId, company, title, location, true, description, applyLink, postedAt, now ?? Now);

    [Fact]
    public void Create_MapsAllFields_F0_2_BR2()
    {
        var posting = Create(company: " Acme ", postedAt: Now.AddDays(-2));

        Assert.NotEqual(Guid.Empty, posting.Id);
        Assert.Equal("apify", posting.Source);
        Assert.Equal("gh-1", posting.ExternalId);
        Assert.Equal("Acme", posting.Company);
        Assert.Equal("Engineer", posting.Title);
        Assert.Equal("London", posting.Location);
        Assert.True(posting.IsRemote);
        Assert.Equal("Build things.", posting.Description);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/1", posting.ApplyUrl!.OriginalString);
        Assert.Equal(ApplyPath.AtsUrl, posting.ApplyPath);
        Assert.Equal(Now.AddDays(-2), posting.PostedAt);
        Assert.Equal(PostingContentHash.Compute("Acme", "Engineer", "London", "Build things."), posting.ContentHash);
        Assert.Equal(Now, posting.CreatedAt);
    }

    [Fact]
    public void Create_OptionalValuesMissing_StoresNulls_F0_2_BR2()
    {
        var posting = Create(location: " ", applyLink: null, postedAt: null);

        Assert.Null(posting.Location);
        Assert.Null(posting.ApplyUrl);
        Assert.Equal(ApplyPath.Unknown, posting.ApplyPath);
        Assert.Null(posting.PostedAt);
    }

    [Fact]
    public void Create_MissingRequired_Throws_F0_2_BR2()
    {
        Assert.Throws<DomainException>(() => Create(source: " "));
        Assert.Throws<DomainException>(() => Create(externalId: ""));
        Assert.Throws<DomainException>(() => Create(company: "\0"));
        Assert.Throws<DomainException>(() => Create(title: " "));
        Assert.Throws<DomainException>(() => Create(description: ""));
    }

    [Fact]
    public void Create_TooLong_Throws_F0_2_BR2()
    {
        Assert.Throws<DomainException>(() => Create(title: new string('x', JobPosting.TitleMaxLength + 1)));
        Assert.Throws<DomainException>(() => Create(location: new string('x', JobPosting.LocationMaxLength + 1)));
        Assert.Throws<DomainException>(() =>
            Create(applyLink: $"https://acme.com/{new string('x', JobPosting.ApplyUrlMaxLength)}"));
    }

    [Fact]
    public void Create_OffsetTimestamp_StoredAsUtcMicroseconds_F0_2_BR2()
    {
        var posted = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(5)).AddTicks(7);

        var posting = Create(postedAt: posted, now: Now.AddTicks(3));

        Assert.Equal(TimeSpan.Zero, posting.PostedAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero), posting.PostedAt);
        Assert.Equal(Now, posting.CreatedAt);
    }

    [Fact]
    public void Create_NullCharacterAndLoneSurrogate_AreStripped_F0_2_BR2()
    {
        var posting = Create(title: "Eng\0in\ud800eer", description: "Ship 🚀");

        Assert.Equal("Engineer", posting.Title);
        Assert.Equal("Ship 🚀", posting.Description);
    }
}

public class PostingContentHashTests
{
    private static string Hash(
        string company = "Acme",
        string title = "Engineer",
        string? location = "London",
        string description = "Build things.") =>
        PostingContentHash.Compute(company, title, location, description);

    [Fact]
    public void Compute_SameContentDifferentCaseAndSpacing_SameHash_F0_2_BR3()
    {
        Assert.Equal(Hash(), Hash(company: " ACME ", title: "engineer", description: "Build \r\n  things."));
        Assert.Equal(PostingContentHash.Length, Hash().Length);
    }

    [Fact]
    public void Compute_CompatibilityCharacters_SameHash_F0_2_BR3() =>
        Assert.Equal(Hash(title: "Office"), Hash(title: "Oﬃce"));

    [Fact]
    public void Compute_AnyFieldDiffers_DifferentHash_F0_2_BR3()
    {
        string[] hashes =
        [
            Hash(),
            Hash(company: "Globex"),
            Hash(title: "Manager"),
            Hash(location: "Paris"),
            Hash(location: null),
            Hash(description: "Break things."),
        ];

        Assert.Equal(hashes.Length, hashes.Distinct().Count());
    }

    [Theory]
    [InlineData("a", "b c", "a b", "c")]
    [InlineData("a", "b\u001Fc", "a\u001Fb", "c")]
    public void Compute_ShiftedFieldBoundary_DifferentHash_F0_2_BR3(string company1, string title1, string company2, string title2) =>
        Assert.NotEqual(Hash(company: company1, title: title1), Hash(company: company2, title: title2));

    [Fact]
    public void Compute_UnicodeNonCharacter_DoesNotThrow_F0_2_BR3() =>
        Assert.Equal(PostingContentHash.Length, Hash(description: "x￾y￿").Length);
}

public class FetchRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Succeeded_RecordsCounts_F0_2_AC2()
    {
        var run = FetchRun.Succeeded("apify", Now, Now.AddSeconds(9), insertedCount: 3, duplicateCount: 2, malformedCount: 1);

        Assert.Equal(FetchRunStatus.Succeeded, run.Status);
        Assert.Null(run.FailureReason);
        Assert.Equal((6, 3, 2, 1), (run.FetchedCount, run.InsertedCount, run.DuplicateCount, run.MalformedCount));
    }

    [Fact]
    public void Failed_RecordsReason_F0_2_AC4()
    {
        var run = FetchRun.Failed("apify", Now, Now.AddSeconds(9), FetchFailureReason.TimedOut);

        Assert.Equal(FetchRunStatus.Failed, run.Status);
        Assert.Equal(FetchFailureReason.TimedOut, run.FailureReason);
        Assert.Equal(0, run.FetchedCount);
    }

    [Fact]
    public void Create_InvalidValues_Throw_F0_2_AC4()
    {
        Assert.Throws<DomainException>(() => FetchRun.Failed(" ", Now, Now, FetchFailureReason.TimedOut));
        Assert.Throws<DomainException>(() => FetchRun.Succeeded("apify", Now, Now, -1, 0, 0));
    }
}
