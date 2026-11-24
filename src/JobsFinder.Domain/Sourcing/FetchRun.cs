using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Sourcing;

/// <summary>Run log entry: the outcome of one fetch from a job source.</summary>
public sealed class FetchRun
{
    // Required by EF Core; real instances go through Succeeded or Failed.
    private FetchRun()
    {
    }

    public Guid Id { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public FetchRunStatus Status { get; private set; }
    public FetchFailureReason? FailureReason { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public int FetchedCount { get; private set; }
    public int InsertedCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public int MalformedCount { get; private set; }

    /// <summary>An id for a fetch that is about to start, so it can be followed until its run is stored.</summary>
    public static Guid NewId() => Guid.CreateVersion7();

    public static FetchRun Succeeded(
        string source,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int insertedCount,
        int duplicateCount,
        int malformedCount,
        Guid? id = null)
    {
        if (insertedCount < 0 || duplicateCount < 0 || malformedCount < 0)
        {
            throw new DomainException("Fetch run counts cannot be negative.");
        }

        var run = Create(source, startedAt, completedAt, id);
        run.Status = FetchRunStatus.Succeeded;
        run.FetchedCount = insertedCount + duplicateCount + malformedCount;
        run.InsertedCount = insertedCount;
        run.DuplicateCount = duplicateCount;
        run.MalformedCount = malformedCount;
        return run;
    }

    public static FetchRun Failed(
        string source,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        FetchFailureReason reason,
        Guid? id = null)
    {
        var run = Create(source, startedAt, completedAt, id);
        run.Status = FetchRunStatus.Failed;
        run.FailureReason = reason;
        return run;
    }

    private static FetchRun Create(string source, DateTimeOffset startedAt, DateTimeOffset completedAt, Guid? id)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > JobPosting.SourceMaxLength)
        {
            throw new DomainException($"Fetch run source is required, at most {JobPosting.SourceMaxLength} characters.");
        }

        return new FetchRun
        {
            Id = id ?? NewId(),
            Source = source,
            StartedAt = StoragePrecision.ToMicroseconds(startedAt.ToUniversalTime()),
            CompletedAt = StoragePrecision.ToMicroseconds(completedAt.ToUniversalTime()),
        };
    }
}
