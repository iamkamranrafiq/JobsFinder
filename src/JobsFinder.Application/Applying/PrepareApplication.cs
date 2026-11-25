using FluentValidation;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Application.Cv;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Applying;
using Microsoft.Extensions.Logging;

namespace JobsFinder.Application.Applying;

public sealed record PrepareApplicationCommand(Guid JobPostingId);

public sealed class PrepareApplicationValidator : AbstractValidator<PrepareApplicationCommand>
{
    public PrepareApplicationValidator() => RuleFor(x => x.JobPostingId).NotEmpty();
}

/// <summary>Limits of one run. Registered with defaults; tests shorten the delays.</summary>
public sealed class ApplySettings
{
    /// <summary>The whole run, kept under the 2 min NFR.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(110);

    /// <summary>Waits before the 3 retries of a page that does not load.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    public int MaxSteps { get; init; } = 10;
}

/// <summary>
/// Fills a Greenhouse form from the active profile and stores the outcome with a screenshot per step (F0.3-AC1, BR2).
/// DryRun only: it stops on the final step, before submit. Every outcome, also a failed one, is a stored run; an error
/// result means there was nothing to run. A cancelled call stores nothing.
/// </summary>
public sealed partial class PrepareApplicationHandler(
    IJobPostingRepository postings,
    ICvRepository cvs,
    IApplyRunRepository runs,
    IUnitOfWork unitOfWork,
    IBrowserAutomationService browser,
    IValidator<PrepareApplicationCommand> validator,
    ApplySettings settings,
    TimeProvider timeProvider,
    ILogger<PrepareApplicationHandler> logger)
{
    public async Task<Result<ApplyRunDto>> HandleAsync(PrepareApplicationCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var posting = await postings.GetApplyTargetAsync(command.JobPostingId, cancellationToken);
        if (posting is null)
        {
            return ApplyErrors.PostingNotFound;
        }

        var active = await cvs.GetActiveAsync(cancellationToken);
        var cv = active is null ? null : await cvs.GetFileAsync(active.Document.Id, cancellationToken);
        if (active is null || cv is null)
        {
            return ApplyErrors.NoProfile;
        }

        var progress = new Progress(ApplyRun.NewId(), posting.Id, active.Profile.Id, timeProvider.GetUtcNow());
        var run = await RunAsync(posting, EvidenceValue.FromProfile(active.Profile.Data), cv, progress, cancellationToken);

        runs.Add(run, progress.Screenshots);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogRun(logger, run.Id, posting.Id, run.Status, run.Reason, run.StepCount, run.Fields.Count);
        return run.ToDto();
    }

    private async Task<ApplyRun> RunAsync(
        PostingApplyTarget posting,
        IReadOnlyList<EvidenceValue> facts,
        CvFileDto cv,
        Progress progress,
        CancellationToken cancellationToken)
    {
        // Also refuses every other ATS: those belong to F3.5.
        if (!GreenhouseApplyUrl.IsAllowed(posting.ApplyUrl))
        {
            return ApplyRun.Failed(progress.ToFacts(timeProvider.GetUtcNow()), ApplyReason.UrlNotAllowed);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(settings.Budget);

        try
        {
            await using var session = await OpenWithRetriesAsync(posting.ApplyUrl, budget.Token);
            var reachedEnd = await FillStepsAsync(session, facts, cv, progress, budget.Token);
            var ended = progress.ToFacts(timeProvider.GetUtcNow());
            return reachedEnd ? ApplyRun.Completed(ended) : ApplyRun.StoppedAtUnknownField(ended);
        }
        catch (BrowserAutomationException e)
        {
            return ApplyRun.Failed(progress.ToFacts(timeProvider.GetUtcNow()), e.Reason);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ApplyRun.Failed(progress.ToFacts(timeProvider.GetUtcNow()), ApplyReason.TimedOut);
        }
    }

    /// <summary>One attempt plus a retry after each delay, for failures that may pass (F0.3 edge case "page load").</summary>
    private async Task<IFormSession> OpenWithRetriesAsync(Uri url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await browser.OpenAsync(url, cancellationToken);
            }
            catch (BrowserAutomationException e) when (e.IsTransient && attempt < settings.RetryDelays.Count)
            {
                await Task.Delay(settings.RetryDelays[attempt], cancellationToken);
            }
        }
    }

    /// <summary>True when the final step was filled; false when a required field without an answer blocks the way.</summary>
    private async Task<bool> FillStepsAsync(
        IFormSession session,
        IReadOnlyList<EvidenceValue> facts,
        CvFileDto cv,
        Progress progress,
        CancellationToken cancellationToken)
    {
        var asked = new HashSet<string>();
        for (var step = 1; step <= settings.MaxSteps; step++)
        {
            var form = await session.ReadStepAsync(cancellationToken);
            progress.StepCount = step;

            foreach (var plan in FieldMapper.Map(form.Fields, facts, asked))
            {
                progress.Fields.Add(await ApplyAsync(session, plan, cv, progress, cancellationToken));
            }

            asked.UnionWith(form.Fields.Select(FieldMapper.QuestionOf).OfType<string>());
            progress.Screenshots.Add(
                ApplyScreenshot.Create(progress.RunId, step, await session.CaptureAsync(cancellationToken)));

            if (!form.HasNext)
            {
                return true;
            }

            if (!await session.GoToNextStepAsync(cancellationToken))
            {
                // The page usually refuses to move on because of the field that was left open.
                return ApplyRun.HasOpenRequiredField(progress.Fields)
                    ? false
                    : throw new BrowserAutomationException(ApplyReason.StepNotReached);
            }
        }

        throw new BrowserAutomationException(ApplyReason.StepNotReached);
    }

    private static async Task<ApplyFieldResult> ApplyAsync(
        IFormSession session,
        FieldPlan plan,
        CvFileDto cv,
        Progress progress,
        CancellationToken cancellationToken)
    {
        var field = plan.Field;
        if (plan.Reason is { } reason)
        {
            return ApplyFieldResult.NeedsHuman(progress.StepCount, field.Label, field.Required, reason);
        }

        var (result, reference) = plan switch
        {
            { AttachCv: true } => (await session.AttachCvAsync(field.Id, cv, cancellationToken), "cv"),
            { Evidence: { } value, Field.Kind: FormFieldKind.Select or FormFieldKind.Combobox } =>
                (await session.SelectOptionAsync(field.Id, value, cancellationToken), value.Reference),
            { Evidence: { } value } => (await session.FillAsync(field.Id, value, cancellationToken), value.Reference),
            _ => throw new InvalidOperationException("A field plan has an answer or a reason."),
        };

        progress.CvAttached |= plan.AttachCv && result == FieldApplyResult.Applied;
        return result switch
        {
            FieldApplyResult.Applied => ApplyFieldResult.Filled(progress.StepCount, field.Label, field.Required, reference),
            FieldApplyResult.NoMatchingOption => ApplyFieldResult.NeedsHuman(
                progress.StepCount, field.Label, field.Required, NeedsHumanReason.NoMatchingOption),
            _ => ApplyFieldResult.NeedsHuman(progress.StepCount, field.Label, field.Required, NeedsHumanReason.NotApplied),
        };
    }

    /// <summary>What a run has done so far; kept outside the session so that a failure does not lose it.</summary>
    private sealed class Progress(Guid runId, Guid postingId, Guid profileId, DateTimeOffset startedAt)
    {
        public Guid RunId => runId;

        public int StepCount { get; set; }

        public bool CvAttached { get; set; }

        public List<ApplyFieldResult> Fields { get; } = [];

        public List<ApplyScreenshot> Screenshots { get; } = [];

        public ApplyRunFacts ToFacts(DateTimeOffset completedAt) =>
            new(runId, postingId, profileId, startedAt, completedAt, StepCount, CvAttached, Fields);
    }

    // Counts only: labels are page content and everything else is personal data.
    [LoggerMessage(Level = LogLevel.Information, Message = "Apply run {RunId} for posting {PostingId} ended as {Status} ({Reason}) after {StepCount} steps and {FieldCount} fields")]
    private static partial void LogRun(
        ILogger logger, Guid runId, Guid postingId, ApplyRunStatus status, ApplyReason? reason, int stepCount, int fieldCount);
}
