using JobsFinder.Application.Abstractions;
using JobsFinder.Domain.Cv;
using JobsFinder.Domain.Jobs;
using JobsFinder.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobsFinder.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<CvDocument> CvDocuments => Set<CvDocument>();

    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();

    public DbSet<FetchRun> FetchRuns => Set<FetchRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    /// <summary>
    /// <see cref="IUnitOfWork.SaveChangesAsync"/>: a unique-key violation surfaces as the persistence-neutral
    /// <see cref="UniqueConstraintViolationException"/> so Application handlers can turn expected races into conflicts.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Detach the failed inserts so the context does not retry them on a later save in the same scope.
            foreach (var entry in e.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new UniqueConstraintViolationException("A unique constraint was violated.", e)
            {
                ConstraintName = pg.ConstraintName,
            };
        }
    }
}
