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

    public static FetchRun Succeeded(
        string source,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int insertedCount,
        int duplicateCount,
        int malformedCount)
    {
        if (insertedCount < 0 || duplicateCount < 0 || malformedCount < 0)
        {
            throw new DomainException("Fetch run counts cannot be negative.");
        }

        var run = Create(source, startedAt, completedAt);
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
        FetchFailureReason reason)
    {
        var run = Create(source, startedAt, completedAt);
        run.Status = FetchRunStatus.Failed;
        run.FailureReason = reason;
        return run;
    }

    private static FetchRun Create(string source, DateTimeOffset startedAt, DateTimeOffset completedAt)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > JobPosting.SourceMaxLength)
        {
            throw new DomainException($"Fetch run source is required, at most {JobPosting.SourceMaxLength} characters.");
        }

        return new FetchRun
        {
            Id = Guid.CreateVersion7(),
            Source = source,
            StartedAt = StoragePrecision.ToMicroseconds(startedAt.ToUniversalTime()),
            CompletedAt = StoragePrecision.ToMicroseconds(completedAt.ToUniversalTime()),
        };
    }
}
