using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

public sealed record FetchRunDto(
    Guid Id,
    string Source,
    FetchRunStatus Status,
    FetchFailureReason? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int FetchedCount,
    int InsertedCount,
    int DuplicateCount,
    int MalformedCount);

public enum SourceState
{
    NeverRun,
    Available,
    Unavailable,
}

/// <summary>Whether the job source worked the last time it was used (F0.2-AC4).</summary>
public sealed record SourceStatusDto(SourceState State, FetchRunDto? LastRun);
