using JobsFinder.Application.Common;
using JobsFinder.Application.Jobs;
using JobsFinder.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobsFinder.Infrastructure.Persistence.Repositories;

internal sealed class JobRepository(AppDbContext db) : IJobRepository
{
    public Task<JobDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(JobMappings.Projection)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<JobDto>> SearchAsync(SearchJobsQuery query, CancellationToken cancellationToken)
    {
        var jobs = db.Jobs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var pattern = $"%{query.Text.Trim()}%";
            jobs = jobs.Where(j => EF.Functions.ILike(j.Title, pattern) || EF.Functions.ILike(j.Company, pattern));
        }

        if (query.RemotePolicy is { } policy)
        {
            jobs = jobs.Where(j => j.RemotePolicy == policy);
        }

        var total = await jobs.CountAsync(cancellationToken);
        var items = await jobs
            .OrderByDescending(j => j.PostedAt)
            .ThenBy(j => j.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(JobMappings.Projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<JobDto>(items, query.Page, query.PageSize, total);
    }

    public Task<bool> ExistsByUrlAsync(Uri url, CancellationToken cancellationToken) =>
        db.Jobs.AsNoTracking().AnyAsync(j => j.Url == url, cancellationToken);

    public void Add(Job job) => db.Jobs.Add(job);
}
