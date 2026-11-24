using JobsFinder.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class FetchRunConfiguration : IEntityTypeConfiguration<FetchRun>
{
    public void Configure(EntityTypeBuilder<FetchRun> builder)
    {
        builder.ToTable("job_fetch_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Source).HasMaxLength(JobPosting.SourceMaxLength).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FailureReason).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => r.StartedAt);
    }
}
