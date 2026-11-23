using JobsFinder.Application.Common;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;

namespace JobsFinder.UnitTests.Application.Cv;

public class SaveProfileVersionHandlerTests
{
    private static readonly DateTimeOffset UploadedAt = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeCvRepository _cvs = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private SaveProfileVersionHandler CreateSut() =>
        new(_cvs, _unitOfWork, new SaveProfileVersionValidator(), new FixedTimeProvider(Now));

    /// <summary>An uploaded CV with its parsed version 1, as JF-5 stores it.</summary>
    private CvDocument SeedParsedCv(CandidateProfileDataDto? parsed = null)
    {
        var document = CvDocument.Create("ada-cv.pdf", CvFormat.Pdf, CvSamples.Bytes(), UploadedAt);
        _cvs.Add(document);
        _cvs.Add(CandidateProfile.CreateFromParse(document.Id, (parsed ?? CvSamples.Profile()).ToDomain(), UploadedAt));
        return document;
    }

    [Fact]
    public async Task HandleAsync_EditedField_AddsVersion2AndSavesOnce_F0_1_AC4()
    {
        var document = SeedParsedCv();
        var edited = CvSamples.Profile() with { Identity = new ProfileIdentityDto("Ada Lovelace", "Staff .NET Engineer") };

        var result = await CreateSut().HandleAsync(new SaveProfileVersionCommand(document.Id, 1, edited), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Profile.Version);
        Assert.Equal("Staff .NET Engineer", result.Value.Profile.Data.Identity.Headline);
        Assert.Equal(document.Id, result.Value.Document.Id);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(2, _cvs.Profiles.Count);

        var active = await _cvs.GetActiveAsync(CancellationToken.None);
        Assert.Equal(result.Value.Profile.Id, active!.Profile.Id);
    }

    [Fact]
    public async Task HandleAsync_EditedField_LeavesVersion1Unchanged_F0_1_BR3()
    {
        var document = SeedParsedCv();
        var edited = CvSamples.Profile() with { Summary = "Rewritten summary." };

        await CreateSut().HandleAsync(new SaveProfileVersionCommand(document.Id, 1, edited), CancellationToken.None);

        var first = _cvs.Profiles.Single(p => p.Version == 1);
        Assert.Equal("Backend engineer with 10 years of .NET.", first.Data.Summary);
    }

    [Fact]
    public async Task HandleAsync_CurrentEmployerCompleted_IsStoredInNewVersion_F0_1_AC5()
    {
        var document = SeedParsedCv(CvSamples.Profile() with { CurrentEmployer = null });
        var completed = CvSamples.Profile() with { CurrentEmployer = "  Difference Engines plc " };

        var result = await CreateSut().HandleAsync(new SaveProfileVersionCommand(document.Id, 1, completed), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Difference Engines plc", result.Value!.Profile.Data.CurrentEmployer);
        Assert.Null(_cvs.Profiles.Single(p => p.Version == 1).Data.CurrentEmployer);
    }

    [Fact]
    public async Task HandleAsync_UnchangedData_StillCreatesNewVersion_F0_1_BR3()
    {
        var document = SeedParsedCv();

        var result = await CreateSut().HandleAsync(
            new SaveProfileVersionCommand(document.Id, 1, CvSamples.Profile()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Profile.Version);
        Assert.Equal(2, _cvs.Profiles.Count);
    }

    [Fact]
    public async Task HandleAsync_StaleBaseVersion_ReturnsConflictAndSavesNothing_F0_1_BR3()
    {
        var document = SeedParsedCv();
        var sut = CreateSut();
        await sut.HandleAsync(new SaveProfileVersionCommand(document.Id, 1, CvSamples.Profile()), CancellationToken.None);

        var result = await sut.HandleAsync(new SaveProfileVersionCommand(document.Id, 1, CvSamples.Profile()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("profile_version_conflict", result.Error.Code);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(2, _cvs.Profiles.Count);
    }

    [Fact]
    public async Task HandleAsync_UniqueViolationOnSave_ReturnsConflict_F0_1_BR3()
    {
        var document = SeedParsedCv();
        _unitOfWork.ThrowUniqueViolation = true;

        var result = await CreateSut().HandleAsync(
            new SaveProfileVersionCommand(document.Id, 1, CvSamples.Profile()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("profile_version_conflict", result.Error!.Code);
    }

    [Fact]
    public async Task HandleAsync_UnknownCv_ReturnsNotFound_F0_1_AC4()
    {
        var result = await CreateSut().HandleAsync(
            new SaveProfileVersionCommand(Guid.CreateVersion7(), 1, CvSamples.Profile()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("cv_not_found", result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    public static TheoryData<string, CandidateProfileDataDto> InvalidProfiles => new()
    {
        { "non-http link", CvSamples.Profile() with { Links = [new ProfileLinkDto("Site", "javascript:alert(1)")] } },
        { "blank skill name", CvSamples.Profile() with { Skills = [new ProfileSkillDto(" ", null, null)] } },
        {
            "free-text date",
            CvSamples.Profile() with { Experience = [new ProfileExperienceDto("Engineer", "Acme", "March 2019", null, true, [])] }
        },
        {
            "employer too long",
            CvSamples.Profile() with { CurrentEmployer = new string('x', CandidateProfileData.ShortTextMaxLength + 1) }
        },
    };

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public async Task HandleAsync_InvalidData_ReturnsValidationAndSavesNothing_F0_1_BR2(string scenario, CandidateProfileDataDto data)
    {
        var document = SeedParsedCv();

        var result = await CreateSut().HandleAsync(new SaveProfileVersionCommand(document.Id, 1, data), CancellationToken.None);

        Assert.False(result.IsSuccess, scenario);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, _unitOfWork.SaveCount);
        Assert.Single(_cvs.Profiles);
    }

    [Fact]
    public async Task HandleAsync_BaseVersionZero_ReturnsValidation_F0_1_BR3()
    {
        var document = SeedParsedCv();

        var result = await CreateSut().HandleAsync(
            new SaveProfileVersionCommand(document.Id, 0, CvSamples.Profile()), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains(nameof(SaveProfileVersionCommand.BaseVersion), result.Error.Details!.Keys);
    }
}
