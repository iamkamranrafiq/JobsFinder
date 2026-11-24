using JobsFinder.Application.Common;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.UnitTests.Application.Sourcing;

public class StartFetchHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSourcingStore _store = new();
    private readonly FakeFetchRunner _runner = new();

    private Task<Result<SourceStatusDto>> StartAsync() =>
        new StartFetchHandler(_store, _runner).HandleAsync(new StartFetchCommand(), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_Idle_StartsAndReturnsFetchId_F0_2_AC1()
    {
        _store.Runs.Add(FetchRun.Succeeded("fake", Now, Now, 1, 0, 0));

        var result = await StartAsync();

        Assert.NotNull(result.Value!.CurrentFetchId);
        Assert.Equal(_runner.CurrentFetchId, result.Value.CurrentFetchId);
        Assert.Equal((SourceState.Available, _store.Runs[0].Id), (result.Value.State, result.Value.LastRun!.Id));
    }

    [Fact]
    public async Task HandleAsync_AlreadyRunning_ReturnsConflictAndKeepsTheFirst_F0_2_AC1()
    {
        await StartAsync();
        var first = _runner.CurrentFetchId;

        var result = await StartAsync();

        Assert.Equal((ErrorType.Conflict, "fetch_in_progress"), (result.Error!.Type, result.Error.Code));
        Assert.Equal(first, _runner.CurrentFetchId);
    }

    [Fact]
    public async Task HandleAsync_LatestRunCannotBeRead_StartsNothing_F0_2_AC1()
    {
        _store.BeforeGetLatest = () => throw new InvalidOperationException("database down");

        await Assert.ThrowsAsync<InvalidOperationException>(StartAsync);

        Assert.Null(_runner.CurrentFetchId);
    }
}

public class SearchPostingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSourcingStore _store = new();

    private Task<Result<PagedResult<JobPostingDto>>> SearchAsync(SearchPostingsQuery query) =>
        new SearchPostingsHandler(_store, new SearchPostingsValidator()).HandleAsync(query, CancellationToken.None);

    [Theory]
    [InlineData(0, 20, "Page")]
    [InlineData(-1, 20, "Page")]
    [InlineData(SearchPostingsValidator.MaxPage + 1, 20, "Page")]
    [InlineData(int.MaxValue, 100, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, SearchPostingsValidator.MaxPageSize + 1, "PageSize")]
    public async Task HandleAsync_InvalidPaging_ReturnsValidation_F0_2_AC1(int page, int pageSize, string property)
    {
        var result = await SearchAsync(new SearchPostingsQuery(page, pageSize));

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal([property], result.Error.Details!.Keys);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(SearchPostingsValidator.MaxPage, SearchPostingsValidator.MaxPageSize)]
    public async Task HandleAsync_PagingAtTheLimits_IsAllowed_F0_2_AC1(int page, int pageSize) =>
        Assert.True((await SearchAsync(new SearchPostingsQuery(page, pageSize))).IsSuccess);

    [Fact]
    public void ToDto_MapsAllFields_F0_2_AC1()
    {
        var posting = JobPosting.Create(
            "apify", "42", "Acme", "Engineer", "London", true, "Build", "https://boards.greenhouse.io/acme/jobs/42", Now.AddDays(-1), Now);

        Assert.Equal(
            new JobPostingDto(
                posting.Id, "apify", "Acme", "Engineer", "London", true,
                new Uri("https://boards.greenhouse.io/acme/jobs/42"), ApplyPath.AtsUrl, Now.AddDays(-1), Now),
            posting.ToDto());
    }
}
