using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;

namespace JobsFinder.UnitTests.Application.Cv;

public class UploadCvHandlerTests
{
    private const string CvText = "Ada Lovelace\nSenior .NET Engineer\nada@example.com\nExperience: Analytical Engines Ltd, 2019 - present";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeCvRepository _cvs = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAIProvider _ai = new();

    private UploadCvHandler CreateSut(CvTextExtraction extraction, CvParsingSettings? settings = null) =>
        CreateSut(new FakeCvTextExtractor(extraction), settings);

    private UploadCvHandler CreateSut(FakeCvTextExtractor extractor, CvParsingSettings? settings = null) =>
        new(_cvs, _unitOfWork, new UploadCvValidator(), extractor, _ai, settings ?? new CvParsingSettings(), new FixedTimeProvider(Now));

    private static UploadCvCommand Command(string fileName = "ada-cv.pdf", int size = 2048) =>
        new(fileName, CvSamples.Bytes(size));

    private void AssertNothingStored()
    {
        Assert.Empty(_cvs.Documents);
        Assert.Empty(_cvs.Profiles);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ValidPdfText_ReturnsDtoWithProfile_F0_1_AC1()
    {
        _ai.Returns(CvSamples.ProfileJson());
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, CvText));

        var result = await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var cv = result.Value!;
        Assert.Equal(CvFormat.Pdf, cv.Document.Format);
        Assert.Equal(CvDocument.PdfContentType, cv.Document.ContentType);
        Assert.Equal("ada-cv.pdf", cv.Document.FileName);
        Assert.Equal(CandidateProfile.FirstVersion, cv.Profile.Version);
        Assert.Equal("Ada Lovelace", cv.Profile.Data.Identity.FullName);
        Assert.Equal("Analytical Engines Ltd", cv.Profile.Data.CurrentEmployer);
        Assert.Single(_ai.Requests);
        Assert.Equal(CvText, _ai.Requests[0].UserContent);
        Assert.Equal(CandidateProfileSchema.Json, _ai.Requests[0].JsonSchema);
    }

    [Fact]
    public async Task HandleAsync_ParseExceedsBudget_ReturnsParseTimeoutAndStoresNothing_F0_1_AC1()
    {
        _ai.Hangs();
        var sut = CreateSut(
            CvTextExtraction.Success(CvFormat.Pdf, CvText),
            new CvParsingSettings { ParseBudget = TimeSpan.FromMilliseconds(50) });

        var result = await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(CvErrors.ParseTimeout, result.Error);
        AssertNothingStored();
    }

    [Fact]
    public async Task HandleAsync_SystemPrompt_RequiresEnglishLabels_F0_1_AC1()
    {
        _ai.Returns(CvSamples.ProfileJson());
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, "Ada Lovelace\nIngénieure logicielle senior chez Analytical Engines"));

        await sut.HandleAsync(Command(), CancellationToken.None);

        var prompt = _ai.Requests[0].SystemPrompt;
        Assert.Contains("not in English", prompt, StringComparison.Ordinal);
        Assert.Contains("English fields", prompt, StringComparison.Ordinal);
        Assert.Contains("Never invent", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_DocxText_ReturnsDtoWithProfile_F0_1_AC2()
    {
        _ai.Returns(CvSamples.ProfileJson());
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Docx, CvText));

        var result = await sut.HandleAsync(Command("ada-cv.docx"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CvFormat.Docx, result.Value!.Document.Format);
        Assert.Equal(CvDocument.DocxContentType, result.Value.Document.ContentType);
        Assert.Equal("Ada Lovelace", result.Value.Profile.Data.Identity.FullName);
    }

    [Theory]
    [InlineData(CvRejection.UnsupportedType, "cv_unsupported_type")]
    [InlineData(CvRejection.PasswordProtected, "cv_password_protected")]
    [InlineData(CvRejection.NoText, "cv_no_text")]
    [InlineData(CvRejection.Corrupt, "cv_corrupt")]
    public async Task HandleAsync_ExtractorRejects_ReturnsSpecificCodeAndStoresNothing_F0_1_AC3(CvRejection rejection, string code)
    {
        var sut = CreateSut(CvTextExtraction.Rejected(rejection));

        var result = await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(ErrorType.Unprocessable, result.Error!.Type);
        Assert.Equal(code, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Empty(_ai.Requests);
        AssertNothingStored();
    }

    [Fact]
    public async Task HandleAsync_Over5Mb_ReturnsTooLargeWithoutCallingAi_F0_1_AC3()
    {
        var extractor = new FakeCvTextExtractor(CvTextExtraction.Success(CvFormat.Pdf, CvText));
        var sut = CreateSut(extractor);

        var result = await sut.HandleAsync(Command(size: CvDocument.MaxSizeBytes + 1), CancellationToken.None);

        Assert.Equal(CvErrors.TooLarge, result.Error);
        Assert.Equal(0, extractor.Calls);
        Assert.Empty(_ai.Requests);
        AssertNothingStored();
    }

    [Fact]
    public async Task HandleAsync_EmptyFile_ReturnsValidationError_F0_1_AC3()
    {
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, CvText));

        var result = await sut.HandleAsync(new UploadCvCommand(string.Empty, []), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains(nameof(UploadCvCommand.FileName), result.Error.Details!.Keys);
        Assert.Contains(nameof(UploadCvCommand.Content), result.Error.Details.Keys);
        AssertNothingStored();
    }

    [Fact]
    public async Task HandleAsync_Success_AddsDocumentAndProfileAndSavesOnce_F0_1_AC6()
    {
        _ai.Returns(CvSamples.ProfileJson());
        var command = Command();
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, CvText));

        var result = await sut.HandleAsync(command, CancellationToken.None);

        var document = Assert.Single(_cvs.Documents);
        var profile = Assert.Single(_cvs.Profiles);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(command.Content, document.Content);
        Assert.Equal(document.Id, profile.CvDocumentId);
        Assert.Equal(Now, document.UploadedAt);
        Assert.Equal(64, document.Sha256.Length);
        Assert.Equal(document.Id, result.Value!.Document.Id);
        Assert.Equal(profile.Id, result.Value.Profile.Id);
    }

    [Fact]
    public async Task HandleAsync_InvalidThenValid_SucceedsAfterRetry_F0_1_BR4()
    {
        _ai.Returns("not json", CvSamples.ProfileJson());
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, CvText));

        var result = await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _ai.Requests.Count);
        Assert.Contains("previous answer was rejected", _ai.Requests[1].UserContent, StringComparison.Ordinal);
        Assert.Contains(CvText, _ai.Requests[1].UserContent, StringComparison.Ordinal);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidThreeTimes_ReturnsUnreadableAndStoresNothing_F0_1_BR4()
    {
        var invalidSalary = CvSamples.Profile() with
        {
            Preferences = CvSamples.Profile().Preferences with { SalaryMin = 200, SalaryMax = 100 },
        };
        _ai.Returns("{\"identity\":", "{}", CvSamples.ProfileJson(invalidSalary), CvSamples.ProfileJson());
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, CvText));

        var result = await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(CvErrors.Unreadable, result.Error);
        Assert.Equal("Could not read this CV; try another format", result.Error!.Message);
        Assert.Equal(3, _ai.Requests.Count);
        AssertNothingStored();
    }

    [Fact]
    public async Task HandleAsync_ProviderUnavailable_ReturnsAiUnavailableAndStoresNothing_F0_1_AC1()
    {
        _ai.Throws(new AIProviderException("down"));
        var sut = CreateSut(CvTextExtraction.Success(CvFormat.Pdf, CvText));

        var result = await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(CvErrors.AIUnavailable, result.Error);
        AssertNothingStored();
    }

    [Fact]
    public async Task HandleAsync_LongText_IsTruncatedBeforeSendingToModel_F0_1_AC1()
    {
        _ai.Returns(CvSamples.ProfileJson());
        var sut = CreateSut(
            CvTextExtraction.Success(CvFormat.Pdf, new string('a', 500)),
            new CvParsingSettings { MaxTextLength = 100 });

        await sut.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(100, _ai.Requests[0].UserContent.Length);
    }
}
