using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;
using JobsFinder.Infrastructure.Cv;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>Exercises the real PdfPig/OpenXml extractor. No Docker needed.</summary>
public class CvTextExtractorTests
{
    private readonly CvTextExtractor _sut = new();

    [Fact]
    public async Task ExtractAsync_TextPdf_ReturnsPdfText_F0_1_AC1()
    {
        var result = await _sut.ExtractAsync(CvFixtures.TextPdf(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Rejection?.ToString());
        Assert.Equal(CvFormat.Pdf, result.Format);
        Assert.Contains("Ada Lovelace", result.Text, StringComparison.Ordinal);
        Assert.Contains("Analytical Engines Ltd", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_Docx_ReturnsDocxText_F0_1_AC2()
    {
        var result = await _sut.ExtractAsync(CvFixtures.Docx(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Rejection?.ToString());
        Assert.Equal(CvFormat.Docx, result.Format);
        Assert.Contains("Ada Lovelace", result.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_ImageOnlyPdf_ReturnsNoText_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync(CvFixtures.ImageOnlyPdf(), CancellationToken.None);

        Assert.Equal(CvRejection.NoText, result.Rejection);
    }

    [Fact]
    public async Task ExtractAsync_EncryptedPdf_ReturnsPasswordProtected_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync(CvFixtures.PasswordProtectedPdf(), CancellationToken.None);

        Assert.Equal(CvRejection.PasswordProtected, result.Rejection);
    }

    [Fact]
    public async Task ExtractAsync_OleCompoundFile_ReturnsPasswordProtected_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync(CvFixtures.EncryptedDocx(), CancellationToken.None);

        Assert.Equal(CvRejection.PasswordProtected, result.Rejection);
    }

    [Fact]
    public async Task ExtractAsync_PngRenamedPdf_ReturnsUnsupportedType_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync(CvFixtures.PngImage(), CancellationToken.None);

        Assert.Equal(CvRejection.UnsupportedType, result.Rejection);
    }

    [Fact]
    public async Task ExtractAsync_PlainText_ReturnsUnsupportedType_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync(CvFixtures.PlainText(), CancellationToken.None);

        Assert.Equal(CvRejection.UnsupportedType, result.Rejection);
    }

    [Fact]
    public async Task ExtractAsync_ZipWithoutWordDocument_ReturnsUnsupportedType_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync(CvFixtures.PlainZip(), CancellationToken.None);

        Assert.Equal(CvRejection.UnsupportedType, result.Rejection);
    }

    [Fact]
    public async Task ExtractAsync_TruncatedPdf_IsRejectedNotThrown_F0_1_AC3()
    {
        var result = await _sut.ExtractAsync("%PDF-1.7\n1 0 obj << /Type"u8.ToArray(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Rejection, new CvRejection?[] { CvRejection.Corrupt, CvRejection.NoText });
    }
}
