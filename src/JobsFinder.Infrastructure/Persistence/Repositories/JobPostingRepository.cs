using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace JobsFinder.Infrastructure.Persistence.Repositories;

internal sealed class JobPostingRepository(AppDbContext db) : IJobPostingRepository
{
    public async Task<IReadOnlyList<PostingKey>> FindExistingAsync(
        string source,
        IReadOnlyCollection<string> externalIds,
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken) =>
        await db.JobPostings
            .AsNoTracking()
            .Where(p => contentHashes.Contains(p.ContentHash)
                || (p.Source == source && externalIds.Contains(p.ExternalId)))
            .Select(p => new PostingKey(p.Source, p.ExternalId, p.ContentHash))
            .ToListAsync(cancellationToken);

    public void AddRange(IEnumerable<JobPosting> postings) => db.JobPostings.AddRange(postings);

    public void DiscardPending(IEnumerable<JobPosting> postings)
    {
        foreach (var posting in postings)
        {
            db.Entry(posting).State = EntityState.Detached;
        }
    }
}
