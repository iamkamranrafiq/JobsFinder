using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace JobsFinder.Infrastructure.Persistence.Repositories;

internal sealed class FetchRunRepository(AppDbContext db) : IFetchRunRepository
{
    public Task<FetchRunDto?> GetLatestAsync(CancellationToken cancellationToken) =>
        db.FetchRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Select(SourcingMappings.RunProjection)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(FetchRun run) => db.FetchRuns.Add(run);

    public void DiscardPending(FetchRun run) => db.Entry(run).State = EntityState.Detached;
}
