using JobsFinder.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.Title).HasMaxLength(Job.TitleMaxLength).IsRequired();
        builder.Property(j => j.Company).HasMaxLength(Job.CompanyMaxLength).IsRequired();
        builder.Property(j => j.Location).HasMaxLength(Job.LocationMaxLength);
        builder.Property(j => j.RemotePolicy).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Url).HasMaxLength(Job.UrlMaxLength).IsRequired();

        builder.HasIndex(j => j.Url).IsUnique();
        builder.HasIndex(j => j.PostedAt);
    }
}
