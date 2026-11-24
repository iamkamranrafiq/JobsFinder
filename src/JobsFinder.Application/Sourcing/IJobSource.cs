using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

/// <summary>
/// Fetches normalised postings from one source (spec 3.9). Implemented in Infrastructure; tests use fakes.
/// </summary>
public interface IJobSource
{
    /// <summary>Stable short name stored with each posting, e.g. "apify".</summary>
    string Name { get; }

    /// <summary>
    /// Returns every item of one fetch, or throws <see cref="JobSourceException"/>. Never returns a partial result.
    /// </summary>
    Task<JobSourceResult> FetchAsync(CancellationToken cancellationToken);
}

/// <param name="Postings">Items the source could map. The handler validates them.</param>
/// <param name="MalformedCount">Items the source had to skip.</param>
public sealed record JobSourceResult(IReadOnlyList<SourcedPosting> Postings, int MalformedCount);

/// <summary>One item as read from a source, before validation.</summary>
public sealed record SourcedPosting(
    string ExternalId,
    string Company,
    string Title,
    string? Location,
    bool IsRemote,
    string Description,
    string? ApplyLink,
    DateTimeOffset? PostedAt);

/// <summary>The source is unavailable. Messages must never contain secrets: they are logged.</summary>
public sealed class JobSourceException : Exception
{
    public JobSourceException()
    {
    }

    public JobSourceException(string message)
        : base(message)
    {
    }

    public JobSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public JobSourceException(FetchFailureReason reason, string message, Exception? innerException = null)
        : base(message, innerException) => Reason = reason;

    public FetchFailureReason Reason { get; } = FetchFailureReason.Unreachable;
}
