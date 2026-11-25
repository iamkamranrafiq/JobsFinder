using JobsFinder.Application.Common;
using JobsFinder.Domain.Applying;

namespace JobsFinder.Application.Applying;

/// <summary>The outcome of a run, without its screenshots.</summary>
public sealed record ApplyRunDto(
    Guid Id,
    Guid JobPostingId,
    Guid CandidateProfileId,
    ApplyRunStatus Status,
    ApplyReason? Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int StepCount,
    bool CvAttached,
    IReadOnlyList<ApplyFieldResult> Fields);

public static class ApplyMappings
{
    public static ApplyRunDto ToDto(this ApplyRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ApplyRunDto(
            run.Id,
            run.JobPostingId,
            run.CandidateProfileId,
            run.Status,
            run.Reason,
            run.StartedAt,
            run.CompletedAt,
            run.StepCount,
            run.CvAttached,
            run.Fields);
    }
}

public static class ApplyErrors
{
    public static readonly Error PostingNotFound = Error.NotFound("posting_not_found", "The job posting does not exist.");

    public static readonly Error NoProfile = Error.Conflict("profile_missing", "Upload a CV before preparing an application.");
}

/// <summary>Implemented in Infrastructure.</summary>
public interface IApplyRunRepository
{
    void Add(ApplyRun run, IEnumerable<ApplyScreenshot> screenshots);
}
