using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using JobsFinder.UnitTests.Application.Cv;
using Microsoft.Extensions.Logging;

namespace JobsFinder.UnitTests.Application.Sourcing;

public class FetchJobPostingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeJobSource _source = new();
    private readonly FakeSourcingStore _store = new();
    private readonly RecordingLogger<FetchJobPostingsHandler> _logger = new();

    private FetchJobPostingsHandler CreateSut() =>
        new(_source, _store, _store, _store, new SourcedPostingValidator(), new FixedTimeProvider(Now), _logger);

    private static SourcedPosting Item(string id, string? description = null) => new(
        id,
        "Acme",
        "Engineer",
        "London",
        false,
        description ?? $"Description of {id}",
        $"https://boards.greenhouse.io/acme/jobs/{id}",
        Now.AddDays(-1));

    private void SourceReturns(params SourcedPosting[] items) => _source.Result = new JobSourceResult(items, 0);

    [Fact]
    public async Task HandleAsync_NewItems_StoresPostingsAndRunInOneSave_F0_2_BR2()
    {
        SourceReturns(Item("1"), Item("2"));

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((2, 2, 0, 0), (result.Value!.FetchedCount, result.Value.InsertedCount, result.Value.DuplicateCount, result.Value.MalformedCount));
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _store.LookupCount);
        Assert.Equal(["1", "2"], _store.Postings.Select(p => p.ExternalId));
        Assert.All(_store.Postings, p => Assert.Equal("fake", p.Source));
        Assert.Equal(FetchRunStatus.Succeeded, Assert.Single(_store.Runs).Status);
    }

    [Fact]
    public async Task HandleAsync_GreenhouseLink_StoredAsAtsUrl_F0_2_AC3()
    {
        SourceReturns(Item("1"));

        await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal(ApplyPath.AtsUrl, Assert.Single(_store.Postings).ApplyPath);
    }

    [Fact]
    public async Task HandleAsync_SameFetchTwice_InsertsOnce_F0_2_AC2()
    {
        SourceReturns(Item("1"), Item("2"));

        await CreateSut().HandleAsync(CancellationToken.None);
        var second = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal((0, 2), (second.Value!.InsertedCount, second.Value.DuplicateCount));
        Assert.Equal(2, _store.Postings.Count);
        Assert.Equal(2, _store.Runs.Count);
    }

    [Fact]
    public async Task HandleAsync_DuplicateWithinBatch_InsertsOnce_F0_2_BR3()
    {
        SourceReturns(Item("1", "Same text"), Item("2", "same  TEXT"), Item("1", "Other text"));

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal((1, 2), (result.Value!.InsertedCount, result.Value.DuplicateCount));
        Assert.Equal("1", Assert.Single(_store.Postings).ExternalId);
    }

    [Fact]
    public async Task HandleAsync_SameContentOtherExternalId_NotInserted_F0_2_BR3()
    {
        SourceReturns(Item("1", "Same text"));
        await CreateSut().HandleAsync(CancellationToken.None);
        SourceReturns(Item("2", "Same text"));

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal((0, 1), (result.Value!.InsertedCount, result.Value.DuplicateCount));
    }

    [Fact]
    public async Task HandleAsync_SameExternalIdChangedText_NotInserted_F0_2_AC2()
    {
        SourceReturns(Item("1", "Posted 1 day ago"));
        await CreateSut().HandleAsync(CancellationToken.None);
        SourceReturns(Item("1", "Posted 2 days ago"));

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal((0, 1), (result.Value!.InsertedCount, result.Value.DuplicateCount));
        Assert.Equal("Posted 1 day ago", Assert.Single(_store.Postings).Description);
    }

    [Fact]
    public async Task HandleAsync_MalformedItems_SkippedAndCounted_RunSucceeds_F0_2_AC2()
    {
        _source.Result = new JobSourceResult(
            [
                Item("1"),
                Item(" "),
                Item("2", "\0"),
                Item("3") with { Title = new string('x', JobPosting.TitleMaxLength + 1) },
                Item("4") with { ApplyLink = $"https://acme.com/{new string('x', JobPosting.ApplyUrlMaxLength)}" },
            ],
            MalformedCount: 2);

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((7, 1, 0, 6), (result.Value!.FetchedCount, result.Value.InsertedCount, result.Value.DuplicateCount, result.Value.MalformedCount));
        Assert.Equal("1", Assert.Single(_store.Postings).ExternalId);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("6 malformed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FetchFailureReason.NotConfigured)]
    [InlineData(FetchFailureReason.Unauthorized)]
    [InlineData(FetchFailureReason.Unreachable)]
    [InlineData(FetchFailureReason.ActorFailed)]
    [InlineData(FetchFailureReason.TimedOut)]
    public async Task HandleAsync_SourceFails_RecordsFailedRun_LogsError_KeepsPostings_F0_2_AC4(FetchFailureReason reason)
    {
        SourceReturns(Item("1"));
        await CreateSut().HandleAsync(CancellationToken.None);
        _source.Error = new JobSourceException(reason, "nope");

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal(ErrorType.Unavailable, result.Error!.Type);
        Assert.Equal("source_unavailable", result.Error.Code);
        Assert.Equal("Source unavailable", result.Error.Message);
        var run = _store.Runs[^1];
        Assert.Equal((FetchRunStatus.Failed, reason), (run.Status, run.FailureReason));
        Assert.Single(_store.Postings);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(reason.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_LostRace_LooksAgainAndStoresOnlyTheRest_F0_2_BR3()
    {
        SourceReturns(Item("1"), Item("2"));
        _store.BeforeSave = () =>
        {
            _store.BeforeSave = null;
            _store.Postings.Add(JobPosting.Create(
                "fake", "1", "Acme", "Engineer", "London", false, "Description of 1", null, null, Now));
            throw new UniqueConstraintViolationException("duplicate key");
        };

        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Value!.InsertedCount, result.Value.DuplicateCount));
        Assert.Equal(["1", "2"], _store.Postings.Select(p => p.ExternalId));
        Assert.Single(_store.Runs);
    }

    [Fact]
    public async Task HandleAsync_Cancelled_PropagatesAndRecordsNothing_F0_2_AC4()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().HandleAsync(cancellation.Token));

        Assert.Empty(_store.Runs);
    }

    [Fact]
    public async Task HandleAsync_NoItems_Succeeds_F0_2_AC2()
    {
        var result = await CreateSut().HandleAsync(CancellationToken.None);

        Assert.Equal(0, result.Value!.FetchedCount);
        Assert.Single(_store.Runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_WithRunId_StoresRunWithThatId_F0_2_AC1(bool fails)
    {
        var id = FetchRun.NewId();
        SourceReturns(Item("1"));
        _source.Error = fails ? new JobSourceException(FetchFailureReason.TimedOut, "nope") : null;

        await CreateSut().HandleAsync(id, CancellationToken.None);

        Assert.Equal(id, Assert.Single(_store.Runs).Id);
    }
}

public class GetSourceStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSourcingStore _store = new();
    private readonly FakeFetchRunner _runner = new();

    private async Task<SourceStatusDto> GetAsync() =>
        (await new GetSourceStatusHandler(_store, _runner).HandleAsync(CancellationToken.None)).Value!;

    [Fact]
    public async Task HandleAsync_NoRuns_ReturnsNeverRun_F0_2_AC4()
    {
        var status = await GetAsync();

        Assert.Equal(SourceState.NeverRun, status.State);
        Assert.Null(status.LastRun);
    }

    [Fact]
    public async Task HandleAsync_LatestRunFailed_ReturnsUnavailable_F0_2_AC4()
    {
        _store.Runs.Add(FetchRun.Succeeded("fake", Now.AddHours(-1), Now.AddHours(-1), 1, 0, 0));
        _store.Runs.Add(FetchRun.Failed("fake", Now, Now, FetchFailureReason.Unauthorized));

        var status = await GetAsync();

        Assert.Equal(SourceState.Unavailable, status.State);
        Assert.Equal(FetchFailureReason.Unauthorized, status.LastRun!.FailureReason);
    }

    [Fact]
    public async Task HandleAsync_LatestRunSucceeded_ReturnsAvailable_F0_2_AC4()
    {
        _store.Runs.Add(FetchRun.Failed("fake", Now.AddHours(-1), Now.AddHours(-1), FetchFailureReason.Unauthorized));
        _store.Runs.Add(FetchRun.Succeeded("fake", Now, Now, 1, 0, 0));

        Assert.Equal(SourceState.Available, (await GetAsync()).State);
    }

    [Fact]
    public async Task HandleAsync_FetchRunning_ReportsItsIdAndLastFinishedRun_F0_2_AC1()
    {
        _store.Runs.Add(FetchRun.Failed("fake", Now, Now, FetchFailureReason.Unauthorized));
        _runner.CurrentFetchId = FetchRun.NewId();

        var status = await GetAsync();

        Assert.Equal(_runner.CurrentFetchId, status.CurrentFetchId);
        Assert.Equal(SourceState.Unavailable, status.State);
        Assert.Equal(_store.Runs[0].Id, status.LastRun!.Id);
    }

    /// <summary>Otherwise a fetch that ends between the two reads would look finished without a run.</summary>
    [Fact]
    public async Task HandleAsync_FetchEndsMeanwhile_StillReportedAsRunning_F0_2_AC1()
    {
        var id = FetchRun.NewId();
        _runner.CurrentFetchId = id;
        _store.BeforeGetLatest = () => _runner.CurrentFetchId = null;

        Assert.Equal(id, (await GetAsync()).CurrentFetchId);
    }
}
