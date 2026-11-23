using JobsFinder.Application.Common;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;

namespace JobsFinder.UnitTests.Application.Cv;

public class GetActiveCvHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeCvRepository _cvs = new();

    [Fact]
    public async Task HandleAsync_NoProfiles_ReturnsNotFound_F0_1_BR5()
    {
        var result = await new GetActiveCvHandler(_cvs).HandleAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("profile_not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_TwoUploads_ReturnsNewestProfile_F0_1_BR3()
    {
        var older = CvDocument.Create("old.pdf", CvFormat.Pdf, CvSamples.Bytes(), Now);
        var newer = CvDocument.Create("new.docx", CvFormat.Docx, CvSamples.Bytes(), Now.AddHours(1));
        _cvs.Add(older);
        _cvs.Add(newer);
        _cvs.Add(CandidateProfile.CreateFromParse(older.Id, CvSamples.Profile().ToDomain(), Now));
        _cvs.Add(CandidateProfile.CreateFromParse(newer.Id, CvSamples.Profile().ToDomain(), Now.AddHours(1)));

        var result = await new GetActiveCvHandler(_cvs).HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newer.Id, result.Value!.Document.Id);
        Assert.Equal("new.docx", result.Value.Document.FileName);
    }
}
