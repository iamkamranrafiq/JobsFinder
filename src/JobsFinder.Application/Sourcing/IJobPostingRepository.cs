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

    void AddRange(IEnumerable<JobPosting> postings);

    /// <summary>Forgets postings that were added but not saved.</summary>
    void DiscardPending(IEnumerable<JobPosting> postings);
}

/// <summary>What makes a fetched posting a duplicate of a stored one.</summary>
public sealed record PostingKey(string Source, string ExternalId, string ContentHash);
