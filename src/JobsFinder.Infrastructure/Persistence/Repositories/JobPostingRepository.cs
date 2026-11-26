using JobsFinder.Application.Common;
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

    public async Task<PagedResult<JobPostingDto>> SearchAsync(SearchPostingsQuery query, CancellationToken cancellationToken)
    {
        var postings = db.JobPostings.AsNoTracking();

        var total = await postings.CountAsync(cancellationToken);
        var items = await postings
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(SourcingMappings.PostingProjection)
            .ToListAsync(cancellationToken);

        return new PagedResult<JobPostingDto>(items, query.Page, query.PageSize, total);
    }

    public Task<PostingApplyTarget?> GetApplyTargetAsync(Guid id, CancellationToken cancellationToken) =>
        db.JobPostings
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PostingApplyTarget(p.Id, p.ApplyUrl, p.ApplyPath))
            .SingleOrDefaultAsync(cancellationToken);

    public void AddRange(IEnumerable<JobPosting> postings) => db.JobPostings.AddRange(postings);

    public void DiscardPending(IEnumerable<JobPosting> postings)
    {
        foreach (var posting in postings)
        {
            db.Entry(posting).State = EntityState.Detached;
        }
    }
}
