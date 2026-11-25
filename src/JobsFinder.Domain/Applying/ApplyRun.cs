using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Applying;

/// <summary>One DryRun apply of a profile version to a posting (F0.3-AC1). Never changed after insert.</summary>
public sealed class ApplyRun
{
    // Required by EF Core; real instances go through the factories.
    private ApplyRun()
    {
    }

    public Guid Id { get; private set; }
    public Guid JobPostingId { get; private set; }

    /// <summary>The profile version the answers came from (spec D9).</summary>
    public Guid CandidateProfileId { get; private set; }

    public ApplyRunStatus Status { get; private set; }
    public ApplyReason? Reason { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Steps whose fields were worked through.</summary>
    public int StepCount { get; private set; }

    public bool CvAttached { get; private set; }
    public IReadOnlyList<ApplyFieldResult> Fields { get; private set; } = [];

    public static Guid NewId() => Guid.CreateVersion7();

    /// <summary>
    /// A run that reached the final step. It is Prepared only when nothing required is open and the CV is attached;
    /// otherwise the candidate has to continue (F0.3-BR2).
    /// </summary>
    public static ApplyRun Completed(ApplyRunFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.StepCount < 1)
        {
            throw new DomainException("A completed apply run has at least one step.");
        }

        var run = Create(facts);
        run.Reason = HasOpenField(facts.Fields) ? ApplyReason.UnknownField
            : facts.CvAttached ? null
            : ApplyReason.CvNotAttached;
        run.Status = run.Reason is null ? ApplyRunStatus.Prepared : ApplyRunStatus.NeedsYou;
        return run;
    }

    /// <summary>A run that could not go on because a required field has no answer.</summary>
    public static ApplyRun StoppedAtUnknownField(ApplyRunFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!HasOpenField(facts.Fields))
        {
            throw new DomainException("A run stops at an unknown field only if a required field is open.");
        }

        var run = Create(facts);
        run.Status = ApplyRunStatus.NeedsYou;
        run.Reason = ApplyReason.UnknownField;
        return run;
    }

    public static ApplyRun Failed(ApplyRunFacts facts, ApplyReason reason)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (reason is ApplyReason.UnknownField or ApplyReason.CvNotAttached)
        {
            throw new DomainException($"{reason} is not a failure.");
        }

        var run = Create(facts);
        run.Status = ApplyRunStatus.Failed;
        run.Reason = reason;
        return run;
    }

    /// <summary>A required field without an answer, or any field that holds what nobody chose.</summary>
    public static bool HasOpenField(IEnumerable<ApplyFieldResult> fields) =>
        fields.Any(f => f is { Required: true, Outcome: ApplyFieldOutcome.NeedsHuman } or { Reason: NeedsHumanReason.NotCleared });

    private static ApplyRun Create(ApplyRunFacts facts)
    {
        if (facts.Id == Guid.Empty || facts.JobPostingId == Guid.Empty || facts.CandidateProfileId == Guid.Empty)
        {
            throw new DomainException("An apply run needs an id, a posting and a profile.");
        }

        if (facts.StepCount < 0)
        {
            throw new DomainException("Apply run step count cannot be negative.");
        }

        return new ApplyRun
        {
            Id = facts.Id,
            JobPostingId = facts.JobPostingId,
            CandidateProfileId = facts.CandidateProfileId,
            StartedAt = StoragePrecision.ToMicroseconds(facts.StartedAt.ToUniversalTime()),
            CompletedAt = StoragePrecision.ToMicroseconds(facts.CompletedAt.ToUniversalTime()),
            StepCount = facts.StepCount,
            CvAttached = facts.CvAttached,
            Fields = [.. facts.Fields],
        };
    }
}

/// <summary>What is known about a run when it ends, whatever its outcome.</summary>
public sealed record ApplyRunFacts(
    Guid Id,
    Guid JobPostingId,
    Guid CandidateProfileId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int StepCount,
    bool CvAttached,
    IReadOnlyList<ApplyFieldResult> Fields);
