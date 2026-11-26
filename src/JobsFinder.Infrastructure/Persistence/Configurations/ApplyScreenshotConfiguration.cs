using JobsFinder.Domain.Applying;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class ApplyScreenshotConfiguration : IEntityTypeConfiguration<ApplyScreenshot>
{
    public void Configure(EntityTypeBuilder<ApplyScreenshot> builder)
    {
        builder.ToTable("apply_screenshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ContentType).HasMaxLength(ApplyScreenshot.ContentTypeMaxLength).IsRequired();
        builder.Property(s => s.Content).HasColumnType("bytea").IsRequired();

        builder.HasOne<ApplyRun>()
            .WithMany()
            .HasForeignKey(s => s.ApplyRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.ApplyRunId, s.Step }).IsUnique();
    }
}
