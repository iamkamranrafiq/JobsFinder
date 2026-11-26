using JobsFinder.Application.Applying;
using JobsFinder.Domain.Applying;

namespace JobsFinder.Infrastructure.Persistence.Repositories;

internal sealed class ApplyRunRepository(AppDbContext db) : IApplyRunRepository
{
    public void Add(ApplyRun run, IEnumerable<ApplyScreenshot> screenshots)
    {
        db.ApplyRuns.Add(run);
        db.ApplyScreenshots.AddRange(screenshots);
    }
}
