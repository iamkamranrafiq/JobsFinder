using JobsFinder.Application.Common;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

public interface IJobPostingRepository
{
    /// <summary>Keys of stored postings with one of these hashes, or one of these external ids from this source.</summary>
    Task<IReadOnlyList<PostingKey>> FindExistingAsync(
        string source,
        IReadOnlyCollection<string> externalIds,
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken);

    /// <summary>Stored postings, the most recently fetched first.</summary>
    Task<PagedResult<JobPostingDto>> SearchAsync(SearchPostingsQuery query, CancellationToken cancellationToken);

    /// <summary>Where and how to apply to a posting, or null when it does not exist.</summary>
    Task<PostingApplyTarget?> GetApplyTargetAsync(Guid id, CancellationToken cancellationToken);

    void AddRange(IEnumerable<JobPosting> postings);

    /// <summary>Forgets postings that were added but not saved.</summary>
    void DiscardPending(IEnumerable<JobPosting> postings);
}

/// <summary>Untrusted: the URL is as scraped.</summary>
public sealed record PostingApplyTarget(Guid Id, Uri? ApplyUrl, ApplyPath ApplyPath);

/// <summary>What makes a fetched posting a duplicate of a stored one.</summary>
public sealed record PostingKey(string Source, string ExternalId, string ContentHash);
