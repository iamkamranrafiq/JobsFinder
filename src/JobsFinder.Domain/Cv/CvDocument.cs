using System.Security.Cryptography;
using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Cv;

/// <summary>The raw uploaded CV file (F0.1-AC6). Stored only after the CV parsed successfully.</summary>
public sealed class CvDocument
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const int Sha256Length = 64;

    public const string PdfContentType = "application/pdf";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // Required by EF Core; real instances go through Create.
    private CvDocument()
    {
    }

    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public CvFormat Format { get; private set; }
    public long SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = [];
    public string Sha256 { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    public static CvDocument Create(string fileName, CvFormat format, byte[] content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainException("CV file name is required.");
        }

        if (content.Length == 0)
        {
            throw new DomainException("CV file is empty.");
        }

        if (content.Length > MaxSizeBytes)
        {
            throw new DomainException("CV file is larger than 5 MB.");
        }

        var name = fileName.Trim();
        if (name.Length > FileNameMaxLength)
        {
            throw new DomainException($"CV file name must be at most {FileNameMaxLength} characters.");
        }

        return new CvDocument
        {
            Id = Guid.CreateVersion7(),
            FileName = name,
            ContentType = format == CvFormat.Pdf ? PdfContentType : DocxContentType,
            Format = format,
            SizeBytes = content.Length,
            Content = content,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            UploadedAt = StoragePrecision.ToMicroseconds(now),
        };
    }
}
