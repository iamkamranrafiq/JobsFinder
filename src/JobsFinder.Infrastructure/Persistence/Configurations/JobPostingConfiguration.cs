using JobsFinder.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.ToTable("job_postings");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Source).HasMaxLength(JobPosting.SourceMaxLength).IsRequired();
        builder.Property(p => p.ExternalId).HasMaxLength(JobPosting.ExternalIdMaxLength).IsRequired();
        builder.Property(p => p.Company).HasMaxLength(JobPosting.CompanyMaxLength).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(JobPosting.TitleMaxLength).IsRequired();
        builder.Property(p => p.Location).HasMaxLength(JobPosting.LocationMaxLength);
        builder.Property(p => p.Description).HasColumnType("text").IsRequired();

        // OriginalString, because Uri.ToString() unescapes and would change the link.
        builder.Property(p => p.ApplyUrl)
            .HasConversion(u => u!.OriginalString, s => new Uri(s))
            .HasMaxLength(JobPosting.ApplyUrlMaxLength);
        builder.Property(p => p.ApplyPath).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ContentHash).HasMaxLength(PostingContentHash.Length).IsFixedLength().IsRequired();

        builder.HasIndex(p => p.ContentHash).IsUnique();
        builder.HasIndex(p => new { p.Source, p.ExternalId }).IsUnique();
    }
}
