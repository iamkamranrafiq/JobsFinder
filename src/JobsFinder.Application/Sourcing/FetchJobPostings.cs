using FluentValidation;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Domain.Sourcing;
using Microsoft.Extensions.Logging;

namespace JobsFinder.Application.Sourcing;

/// <summary>Items that fail these rules are malformed: skipped and counted, never fatal.</summary>
public sealed class SourcedPostingValidator : AbstractValidator<SourcedPosting>
{
    // A bare email address gains this prefix when it becomes a URL.
    private const int MailtoPrefixLength = 7;

    public SourcedPostingValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(JobPosting.ExternalIdMaxLength);
        RuleFor(x => x.Company).NotEmpty().MaximumLength(JobPosting.CompanyMaxLength);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(JobPosting.TitleMaxLength);
        RuleFor(x => x.Location).MaximumLength(JobPosting.LocationMaxLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(JobPosting.DescriptionMaxLength);
        RuleFor(x => x.ApplyLink).MaximumLength(JobPosting.ApplyUrlMaxLength - MailtoPrefixLength);
    }
}

/// <summary>
/// Runs one fetch: normalises, de-duplicates and stores the source's postings, and records the run (F0.2).
/// Nothing is stored until the source has returned everything, so a failed or timed-out run leaves no partial data.
/// </summary>
public sealed partial class FetchJobPostingsHandler(
    IJobSource source,
    IJobPostingRepository postings,
    IFetchRunRepository runs,
    IUnitOfWork unitOfWork,
    IValidator<SourcedPosting> validator,
    TimeProvider timeProvider,
    ILogger<FetchJobPostingsHandler> logger)
{
    private const int MaxSaveAttempts = 2;

    // Two fetches storing the same postings in different orders could deadlock on the unique indexes, so the
    // short "look up, then save" part runs one at a time. v1 is a single API instance (spec D14); across
    // instances the indexes still prevent duplicates.
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    public async Task<Result<FetchRunDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();

        JobSourceResult fetched;
        try
        {
            fetched = await source.FetchAsync(cancellationToken);
        }
        catch (JobSourceException e)
        {
            LogFetchFailed(logger, e, source.Name, e.Reason);
            runs.Add(FetchRun.Failed(source.Name, startedAt, timeProvider.GetUtcNow(), e.Reason));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return SourcingErrors.SourceUnavailable;
        }

        var (candidates, invalidCount) = await BuildPostingsAsync(fetched.Postings, cancellationToken);
        var malformedCount = fetched.MalformedCount + invalidCount;

        FetchRun run;
        await SaveGate.WaitAsync(cancellationToken);
        try
        {
            run = await StoreNewAsync(candidates, malformedCount, startedAt, cancellationToken);
        }
        finally
        {
            SaveGate.Release();
        }

        if (malformedCount > 0)
        {
            LogMalformed(logger, source.Name, malformedCount);
        }

        LogFetchSucceeded(logger, source.Name, run.InsertedCount, run.DuplicateCount, run.MalformedCount);
        return run.ToDto();
    }

    private async Task<(List<JobPosting> Postings, int InvalidCount)> BuildPostingsAsync(
        IReadOnlyList<SourcedPosting> items,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var valid = new List<JobPosting>(items.Count);
        foreach (var item in items)
        {
            var clean = Clean(item);
            if (!(await validator.ValidateAsync(clean, cancellationToken)).IsValid)
            {
                continue;
            }

            valid.Add(JobPosting.Create(
                source.Name,
                clean.ExternalId,
                clean.Company,
                clean.Title,
                clean.Location,
                clean.IsRemote,
                clean.Description,
                clean.ApplyLink,
                clean.PostedAt,
                now));
        }

        return (valid, items.Count - valid.Count);
    }

    private async Task<FetchRun> StoreNewAsync(
        List<JobPosting> candidates,
        int malformedCount,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var fresh = await SelectNewAsync(candidates, cancellationToken);
            var run = FetchRun.Succeeded(
                source.Name,
                startedAt,
                timeProvider.GetUtcNow(),
                fresh.Count,
                candidates.Count - fresh.Count,
                malformedCount);

            postings.AddRange(fresh);
            runs.Add(run);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return run;
            }
            catch (UniqueConstraintViolationException) when (attempt < MaxSaveAttempts)
            {
                // Another instance stored some of these first; the whole save was rolled back. Look again.
                postings.DiscardPending(fresh);
                runs.DiscardPending(run);
            }
        }
    }

    /// <summary>Drops postings that repeat a hash (BR3) or an external id, in this fetch or in the database.</summary>
    private async Task<List<JobPosting>> SelectNewAsync(List<JobPosting> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var existing = await postings.FindExistingAsync(
            source.Name,
            [.. candidates.Select(p => p.ExternalId).Distinct()],
            [.. candidates.Select(p => p.ContentHash).Distinct()],
            cancellationToken);

        var hashes = existing.Select(k => k.ContentHash).ToHashSet(StringComparer.Ordinal);
        var externalIds = existing
            .Where(k => k.Source == source.Name)
            .Select(k => k.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        var fresh = new List<JobPosting>(candidates.Count);
        foreach (var posting in candidates)
        {
            if (!hashes.Contains(posting.ContentHash) && !externalIds.Contains(posting.ExternalId))
            {
                hashes.Add(posting.ContentHash);
                externalIds.Add(posting.ExternalId);
                fresh.Add(posting);
            }
        }

        return fresh;
    }

    private static SourcedPosting Clean(SourcedPosting item) => item with
    {
        ExternalId = PostingText.Clean(item.ExternalId),
        Company = PostingText.Clean(item.Company),
        Title = PostingText.Clean(item.Title),
        Location = PostingText.CleanOrNull(item.Location),
        Description = PostingText.Clean(item.Description),
        ApplyLink = PostingText.CleanOrNull(item.ApplyLink),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Job fetch from {Source} failed: {Reason}")]
    private static partial void LogFetchFailed(ILogger logger, Exception exception, string source, FetchFailureReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job fetch from {Source} skipped {MalformedCount} malformed items")]
    private static partial void LogMalformed(ILogger logger, string source, int malformedCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Job fetch from {Source} finished: {InsertedCount} new, {DuplicateCount} duplicates, {MalformedCount} malformed")]
    private static partial void LogFetchSucceeded(
        ILogger logger,
        string source,
        int insertedCount,
        int duplicateCount,
        int malformedCount);
}
