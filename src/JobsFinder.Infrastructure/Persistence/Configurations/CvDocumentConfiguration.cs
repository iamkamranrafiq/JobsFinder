using JobsFinder.Domain.Cv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobsFinder.Infrastructure.Persistence.Configurations;

internal sealed class CvDocumentConfiguration : IEntityTypeConfiguration<CvDocument>
{
    public void Configure(EntityTypeBuilder<CvDocument> builder)
    {
        builder.ToTable("cv_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.FileName).HasMaxLength(CvDocument.FileNameMaxLength).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(CvDocument.ContentTypeMaxLength).IsRequired();
        builder.Property(d => d.Format).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Content).HasColumnType("bytea").IsRequired();
        builder.Property(d => d.Sha256).HasMaxLength(CvDocument.Sha256Length).IsFixedLength().IsRequired();

        builder.HasIndex(d => d.UploadedAt);
    }
}
