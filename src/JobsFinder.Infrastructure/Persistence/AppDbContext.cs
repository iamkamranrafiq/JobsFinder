using JobsFinder.Application.Abstractions;
using JobsFinder.Domain.Cv;
using JobsFinder.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobsFinder.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<CvDocument> CvDocuments => Set<CvDocument>();

    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
