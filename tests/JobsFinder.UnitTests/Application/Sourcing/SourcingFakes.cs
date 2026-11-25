using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using Microsoft.Extensions.Logging;

namespace JobsFinder.UnitTests.Application.Sourcing;

internal sealed class FakeJobSource : IJobSource
{
    public string Name => "fake";

    public JobSourceResult Result { get; set; } = new([], 0);

    public Exception? Error { get; set; }

    public Task<JobSourceResult> FetchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Error is null ? Task.FromResult(Result) : Task.FromException<JobSourceResult>(Error);
    }
}

/// <summary>In-memory store for postings and runs: Add is pending until a save succeeds, as in EF Core.</summary>
internal sealed class FakeSourcingStore : IJobPostingRepository, IFetchRunRepository, IUnitOfWork
{
    private readonly List<JobPosting> _pendingPostings = [];
    private readonly List<FetchRun> _pendingRuns = [];

    public List<JobPosting> Postings { get; } = [];

    public List<FetchRun> Runs { get; } = [];

    public int SaveCount { get; private set; }

    public int LookupCount { get; private set; }

    /// <summary>Runs before each save; e.g. stores a competing posting and throws, like a lost race.</summary>
    public Action? BeforeSave { get; set; }

    public Task<IReadOnlyList<PostingKey>> FindExistingAsync(
        string source,
        IReadOnlyCollection<string> externalIds,
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken)
    {
        LookupCount++;
        return Task.FromResult<IReadOnlyList<PostingKey>>(
        [
            .. Postings
                .Where(p => contentHashes.Contains(p.ContentHash) || (p.Source == source && externalIds.Contains(p.ExternalId)))
                .Select(p => new PostingKey(p.Source, p.ExternalId, p.ContentHash)),
        ]);
    }

    public Task<PagedResult<JobPostingDto>> SearchAsync(SearchPostingsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<JobPostingDto>(
            [.. Postings.Select(p => p.ToDto())], query.Page, query.PageSize, Postings.Count));

    public Task<PostingApplyTarget?> GetApplyTargetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Postings
            .Where(p => p.Id == id)
            .Select(p => new PostingApplyTarget(p.Id, p.ApplyUrl, p.ApplyPath))
            .SingleOrDefault());

    public void AddRange(IEnumerable<JobPosting> postings) => _pendingPostings.AddRange(postings);

    public void DiscardPending(IEnumerable<JobPosting> postings) => _pendingPostings.RemoveAll(postings.Contains);

    /// <summary>Runs before the latest run is read.</summary>
    public Action? BeforeGetLatest { get; set; }

    public Task<FetchRunDto?> GetLatestAsync(CancellationToken cancellationToken)
    {
        BeforeGetLatest?.Invoke();
        return Task.FromResult(Runs.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefault()?.ToDto());
    }

    public void Add(FetchRun run) => _pendingRuns.Add(run);

    public void DiscardPending(FetchRun run) => _pendingRuns.Remove(run);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        BeforeSave?.Invoke();
        SaveCount++;
        Postings.AddRange(_pendingPostings);
        Runs.AddRange(_pendingRuns);
        _pendingPostings.Clear();
        _pendingRuns.Clear();
        return Task.FromResult(1);
    }
}

internal sealed class FakeFetchRunner : IFetchRunner
{
    public Guid? CurrentFetchId { get; set; }

    public bool TryStart(Guid fetchId)
    {
        if (CurrentFetchId is not null)
        {
            return false;
        }

        CurrentFetchId = fetchId;
        return true;
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
