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
/// <param name="State">Outcome of the latest finished fetch; a running one does not change it.</param>
/// <param name="LastRun">The latest finished fetch.</param>
/// <param name="CurrentFetchId">The running fetch, or null. Its run is stored under this id when it ends.</param>
public sealed record SourceStatusDto(SourceState State, FetchRunDto? LastRun, Guid? CurrentFetchId);

/// <summary>One row of the postings list (F0.2-AC1). The description is left out: large and untrusted.</summary>
public sealed record JobPostingDto(
    Guid Id,
    string Source,
    string Company,
    string Title,
    string? Location,
    bool IsRemote,
    Uri? ApplyUrl,
    ApplyPath ApplyPath,
    DateTimeOffset? PostedAt,
    DateTimeOffset CreatedAt);
