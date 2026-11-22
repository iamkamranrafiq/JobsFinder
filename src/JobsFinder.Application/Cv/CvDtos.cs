using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <summary>An uploaded CV with the profile parsed from it (returned for review, F0.1-AC1).</summary>
public sealed record CvDto(CvDocumentDto Document, CandidateProfileDto Profile);

/// <summary>CV file metadata. Never carries the file bytes.</summary>
public sealed record CvDocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    CvFormat Format,
    long SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record CandidateProfileDto(
    Guid Id,
    Guid CvDocumentId,
    int Version,
    DateTimeOffset CreatedAt,
    CandidateProfileDataDto Data);

/// <summary>The raw file for download (F0.1-AC6).</summary>
public sealed record CvFileDto(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Wire shape of the profile (F0.1-BR2). It is also the type the JSON schema sent to the model is built from
/// (see <see cref="CandidateProfileSchema"/>), so its constructor parameters define the schema's required fields.
/// </summary>
public sealed record CandidateProfileDataDto(
    ProfileIdentityDto Identity,
    ProfileContactDto Contact,
    string? Summary,
    IReadOnlyList<ProfileSkillDto> Skills,
    IReadOnlyList<ProfileExperienceDto> Experience,
    IReadOnlyList<ProfileEducationDto> Education,
    IReadOnlyList<ProfileCertificationDto> Certifications,
    IReadOnlyList<ProfileLinkDto> Links,
    string? WorkAuthorisation,
    string? CurrentEmployer,
    ProfilePreferencesDto Preferences)
{
    /// <summary>A profile with nothing filled in.</summary>
    public static CandidateProfileDataDto Empty { get; } = new(
        new ProfileIdentityDto(null, null),
        new ProfileContactDto(null, null, null),
        null,
        [],
        [],
        [],
        [],
        [],
        null,
        null,
        new ProfilePreferencesDto([], [], null, null, null, null, null));
}

public sealed record ProfileIdentityDto(string? FullName, string? Headline);

public sealed record ProfileContactDto(string? Email, string? Phone, string? Location);

public sealed record ProfileSkillDto(string Name, SkillProficiency? Proficiency, decimal? Years);

public sealed record ProfileExperienceDto(
    string Role,
    string Company,
    string? StartDate,
    string? EndDate,
    bool IsCurrent,
    IReadOnlyList<string> Achievements);

public sealed record ProfileEducationDto(
    string Institution,
    string? Qualification,
    string? Field,
    string? StartDate,
    string? EndDate);

public sealed record ProfileCertificationDto(string Name, string? Issuer, int? Year);

public sealed record ProfileLinkDto(string? Label, string Url);

public sealed record ProfilePreferencesDto(
    IReadOnlyList<string> TargetRoles,
    IReadOnlyList<string> Locations,
    RemotePreference? Remote,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string? NoticePeriod);
