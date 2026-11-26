using JobsFinder.Domain.Applying;
using JobsFinder.Domain.Cv;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class ApplyRunConfiguration : IEntityTypeConfiguration<ApplyRun>
{
    public void Configure(EntityTypeBuilder<ApplyRun> builder)
    {
        builder.ToTable("apply_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Fields)
            .HasConversion(ApplyFieldResultsJson.Converter, ApplyFieldResultsJson.Comparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasOne<JobPosting>()
            .WithMany()
            .HasForeignKey(r => r.JobPostingId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting a CV deletes its profiles, and with them every run and screenshot that shows its data.
        builder.HasOne<CandidateProfile>()
            .WithMany()
            .HasForeignKey(r => r.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.JobPostingId, r.StartedAt });
    }
}
