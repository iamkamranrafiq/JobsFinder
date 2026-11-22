using JobsFinder.Domain.Cv;
using JobsFinder.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> builder)
    {
        builder.ToTable("candidate_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Data)
            .HasConversion(CandidateProfileDataJson.Converter, CandidateProfileDataJson.Comparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasOne<CvDocument>()
            .WithMany()
            .HasForeignKey(p => p.CvDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.CvDocumentId, p.Version }).IsUnique();
    }
}
