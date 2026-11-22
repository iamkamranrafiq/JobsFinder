using JobsFinder.Application.Cv;
using JobsFinder.Domain.Common;
using JobsFinder.Domain.Cv;

namespace JobsFinder.UnitTests.Domain;

public class CvDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidPdf_SetsMetadataAndHash_F0_1_BR1()
    {
        var content = "%PDF-1.7 test"u8.ToArray();

        var document = CvDocument.Create("  cv.pdf ", CvFormat.Pdf, content, Now);

        Assert.Equal("cv.pdf", document.FileName);
        Assert.Equal(CvDocument.PdfContentType, document.ContentType);
        Assert.Equal(content.Length, document.SizeBytes);
        Assert.Equal(Now, document.UploadedAt);
        Assert.Matches("^[0-9a-f]{64}$", document.Sha256);
        Assert.NotEqual(Guid.Empty, document.Id);
    }

    [Fact]
    public void Create_Over5Mb_Throws_F0_1_BR1()
    {
        var content = new byte[CvDocument.MaxSizeBytes + 1];

        Assert.Throws<DomainException>(() => CvDocument.Create("cv.pdf", CvFormat.Pdf, content, Now));
    }

    [Fact]
    public void Create_Exactly5Mb_Succeeds_F0_1_BR1()
    {
        var content = new byte[CvDocument.MaxSizeBytes];

        var document = CvDocument.Create("cv.pdf", CvFormat.Pdf, content, Now);

        Assert.Equal(CvDocument.MaxSizeBytes, document.SizeBytes);
    }

    [Fact]
    public void Create_EmptyContent_Throws_F0_1_BR1()
    {
        Assert.Throws<DomainException>(() => CvDocument.Create("cv.pdf", CvFormat.Pdf, [], Now));
    }

    [Fact]
    public void Create_BlankFileName_Throws_F0_1_BR1()
    {
        Assert.Throws<DomainException>(() => CvDocument.Create(" ", CvFormat.Docx, [1, 2, 3], Now));
    }

    [Fact]
    public void CandidateProfile_CreateFromParse_IsFirstVersion_F0_1_AC6()
    {
        var data = CandidateProfileDataDto.Empty.ToDomain();
        var documentId = Guid.CreateVersion7();

        var profile = CandidateProfile.CreateFromParse(documentId, data, Now);

        Assert.Equal(CandidateProfile.FirstVersion, profile.Version);
        Assert.Equal(documentId, profile.CvDocumentId);
        Assert.Same(data, profile.Data);
    }

    [Fact]
    public void CandidateProfile_CreateFromParse_EmptyDocumentId_Throws_F0_1_AC6()
    {
        var data = CandidateProfileDataDto.Empty.ToDomain();

        Assert.Throws<DomainException>(() => CandidateProfile.CreateFromParse(Guid.Empty, data, Now));
    }

    [Fact]
    public void Create_TimestampWithSubMicrosecondTicks_IsTruncatedToMicroseconds_F0_1_AC1()
    {
        var now = Now.AddTicks(1234567); // 123 456.7 microseconds: the trailing 7 ticks are below PostgreSQL precision

        var document = CvDocument.Create("cv.pdf", CvFormat.Pdf, [1, 2, 3], now);
        var profile = CandidateProfile.CreateFromParse(document.Id, CandidateProfileDataDto.Empty.ToDomain(), now);

        var expected = Now.AddTicks(1234560);
        Assert.Equal(expected, document.UploadedAt);
        Assert.Equal(expected, profile.CreatedAt);
        Assert.Equal(0, document.UploadedAt.Ticks % TimeSpan.TicksPerMicrosecond);
    }
}
