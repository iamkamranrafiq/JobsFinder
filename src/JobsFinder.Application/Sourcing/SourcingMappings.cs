using System.Linq.Expressions;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

/// <summary>Manual mapping only (no AutoMapper). Keep both forms in sync.</summary>
public static class SourcingMappings
{
    /// <summary>For EF Core queries: translated to SQL so only the needed columns are selected.</summary>
    public static readonly Expression<Func<FetchRun, FetchRunDto>> RunProjection = r =>
        new FetchRunDto(
            r.Id,
            r.Source,
            r.Status,
            r.FailureReason,
            r.StartedAt,
            r.CompletedAt,
            r.FetchedCount,
            r.InsertedCount,
            r.DuplicateCount,
            r.MalformedCount);

    public static readonly Expression<Func<JobPosting, JobPostingDto>> PostingProjection = p =>
        new JobPostingDto(
            p.Id,
            p.Source,
            p.Company,
            p.Title,
            p.Location,
            p.IsRemote,
            p.ApplyUrl,
            p.ApplyPath,
            p.PostedAt,
            p.CreatedAt);

    private static readonly Func<FetchRun, FetchRunDto> CompiledRun = RunProjection.Compile();

    private static readonly Func<JobPosting, JobPostingDto> CompiledPosting = PostingProjection.Compile();

    /// <summary>For in-memory entities (e.g. right after a fetch).</summary>
    public static FetchRunDto ToDto(this FetchRun run) => CompiledRun(run);

    public static JobPostingDto ToDto(this JobPosting posting) => CompiledPosting(posting);

    public static SourceStatusDto ToStatus(FetchRunDto? lastRun, Guid? currentFetchId)
    {
        var state = lastRun switch
        {
            null => SourceState.NeverRun,
            { Status: FetchRunStatus.Failed } => SourceState.Unavailable,
            _ => SourceState.Available,
        };

        return new SourceStatusDto(state, lastRun, currentFetchId);
    }
}
