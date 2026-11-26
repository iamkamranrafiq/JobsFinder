using FluentValidation;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Application.Cv;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Applying;
using JobsFinder.Domain.Sourcing;
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

    /// <summary>A filled step must stay unchanged for this long before it is judged.</summary>
    public TimeSpan QuietTime { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait for that. The budget applies as well.</summary>
    public TimeSpan MaxSettleTime { get; init; } = TimeSpan.FromSeconds(8);
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
    private const string CvEvidence = "cv";
    private const int MaxCaptures = 3;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

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
        if (posting.ApplyPath != ApplyPath.AtsUrl || !GreenhouseApplyUrl.IsAllowed(posting.ApplyUrl))
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

            var first = progress.Fields.Count;
            foreach (var plan in FieldMapper.Map(form.Fields, facts, asked))
            {
                progress.Fields.Add(await ApplyAsync(session, plan, cv, progress, cancellationToken));
            }

            // Answers can change a page: the step is judged as it is now, not as it was before it was filled.
            var now = await CaptureAtRestAsync(session, progress, cancellationToken);
            NoteChanges(form, now, first, progress);
            foreach (var lost in await session.FindLostAnswersAsync(cancellationToken))
            {
                progress.MarkNotKept(progress.Answers[lost]);
            }

            asked.UnionWith(form.Fields.Concat(now.Fields).Select(FieldMapper.QuestionOf).OfType<string>());
            if (now.End == FormStepEnd.Final)
            {
                return true;
            }

            if (now.End != FormStepEnd.Next || !await session.GoToNextStepAsync(cancellationToken))
            {
                // A page usually refuses to move on because of a field that was left open.
                return ApplyRun.HasOpenField(progress.Fields)
                    ? false
                    : throw new BrowserAutomationException(ApplyReason.StepNotReached);
            }
        }

        throw new BrowserAutomationException(ApplyReason.StepNotReached);
    }

    /// <summary>
    /// Waits until the step has stayed the same for a while, takes its screenshot, and returns the step as it is after that.
    /// Only reads; a page that does not come to rest ends the run.
    /// </summary>
    private async Task<FormStep> CaptureAtRestAsync(IFormSession session, Progress progress, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCaptures; attempt++)
        {
            var settled = await ReadAtRestAsync(session, cancellationToken);
            var jpeg = await session.CaptureAsync(cancellationToken);
            var now = await session.ReadStepAsync(cancellationToken);
            if (IsSame(settled, now))
            {
                progress.Screenshots.Add(ApplyScreenshot.Create(progress.RunId, progress.StepCount, jpeg));
                return now;
            }
        }

        throw new BrowserAutomationException(ApplyReason.StepNotReached);
    }

    private async Task<FormStep> ReadAtRestAsync(IFormSession session, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var quietSince = started;
        var last = await session.ReadStepAsync(cancellationToken);
        while (timeProvider.GetElapsedTime(quietSince) < settings.QuietTime)
        {
            if (timeProvider.GetElapsedTime(started) > settings.MaxSettleTime)
            {
                throw new BrowserAutomationException(ApplyReason.StepNotReached);
            }

            await Task.Delay(PollInterval, cancellationToken);
            var now = await session.ReadStepAsync(cancellationToken);
            if (!IsSame(last, now))
            {
                quietSince = timeProvider.GetTimestamp();
            }

            last = now;
        }

        return last;
    }

    private static bool IsSame(FormStep a, FormStep b) =>
        a.End == b.End && a.Fields.Select(f => (f.Key, f.Required, f.State)).SequenceEqual(b.Fields.Select(f => (f.Key, f.Required, f.State)));

    /// <summary>Compares the step with how it was before it was filled. What is different needs the candidate.</summary>
    private static void NoteChanges(FormStep before, FormStep now, int first, Progress progress)
    {
        var current = now.Fields.ToDictionary(f => f.Key);
        for (var i = 0; i < before.Fields.Count; i++)
        {
            var position = first + i;
            var result = progress.Fields[position];
            if (!current.Remove(before.Fields[i].Key, out var field))
            {
                if (result.Outcome == ApplyFieldOutcome.Filled)
                {
                    progress.MarkNotKept(position);
                }

                continue;
            }

            // Left to the candidate, but the page has put something in it.
            if (result.Outcome == ApplyFieldOutcome.NeedsHuman && field.State != before.Fields[i].State)
            {
                result = result with { Reason = NeedsHumanReason.NotCleared };
            }

            progress.Fields[position] = result with { Required = result.Required || field.Required };
        }

        // In page order, and not filled even if the profile has the answer: that could go on without end.
        foreach (var field in now.Fields.Where(f => current.ContainsKey(f.Key)))
        {
            progress.Fields.Add(ApplyFieldResult.NeedsHuman(
                progress.StepCount, field.Label, field.Required, NeedsHumanReason.AppearedLater));
        }
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
            { AttachCv: true } => (await session.AttachCvAsync(field.Id, cv, cancellationToken), CvEvidence),
            { Evidence: { } value, Field.Kind: FormFieldKind.Select or FormFieldKind.Combobox } =>
                (await session.SelectOptionAsync(field.Id, value, cancellationToken), value.Reference),
            { Evidence: { } value } => (await session.FillAsync(field.Id, value, cancellationToken), value.Reference),
            _ => throw new InvalidOperationException("A field plan has an answer or a reason."),
        };

        if (result == FieldApplyResult.Applied)
        {
            progress.Answers.Add(progress.Fields.Count);
        }

        return result switch
        {
            FieldApplyResult.Applied => ApplyFieldResult.Filled(progress.StepCount, field.Label, field.Required, reference),
            FieldApplyResult.NoMatchingOption => ApplyFieldResult.NeedsHuman(
                progress.StepCount, field.Label, field.Required, NeedsHumanReason.NoMatchingOption),
            FieldApplyResult.NotCleared => ApplyFieldResult.NeedsHuman(
                progress.StepCount, field.Label, field.Required, NeedsHumanReason.NotCleared),
            _ => ApplyFieldResult.NeedsHuman(progress.StepCount, field.Label, field.Required, NeedsHumanReason.NotApplied),
        };
    }

    /// <summary>What a run has done so far; kept outside the session so that a failure does not lose it.</summary>
    private sealed class Progress(Guid runId, Guid postingId, Guid profileId, DateTimeOffset startedAt)
    {
        public Guid RunId => runId;

        public int StepCount { get; set; }

        public bool CvAttached => Fields.Any(f => f is { Outcome: ApplyFieldOutcome.Filled, Evidence: CvEvidence });

        /// <summary>For every answer that was given, where its field is in <see cref="Fields"/>.</summary>
        public List<int> Answers { get; } = [];

        public List<ApplyFieldResult> Fields { get; } = [];

        public List<ApplyScreenshot> Screenshots { get; } = [];

        public void MarkNotKept(int position)
        {
            var field = Fields[position];
            Fields[position] = ApplyFieldResult.NeedsHuman(field.Step, field.Label, field.Required, NeedsHumanReason.NotKept);
        }

        public ApplyRunFacts ToFacts(DateTimeOffset completedAt) =>
            new(runId, postingId, profileId, startedAt, completedAt, StepCount, CvAttached, Fields);
    }

    // Counts only: labels are page content and everything else is personal data.
    [LoggerMessage(Level = LogLevel.Information, Message = "Apply run {RunId} for posting {PostingId} ended as {Status} ({Reason}) after {StepCount} steps and {FieldCount} fields")]
    private static partial void LogRun(
        ILogger logger, Guid runId, Guid postingId, ApplyRunStatus status, ApplyReason? reason, int stepCount, int fieldCount);
}
