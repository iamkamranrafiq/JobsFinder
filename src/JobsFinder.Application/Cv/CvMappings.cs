using System.Linq.Expressions;
using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <summary>Manual mapping only (no AutoMapper). Keep the projections and the in-memory forms in sync.</summary>
public static class CvMappings
{
    /// <summary>For EF Core queries: selects metadata columns only, never the file bytes.</summary>
    public static readonly Expression<Func<CvDocument, CvDocumentDto>> DocumentProjection = d =>
        new CvDocumentDto(d.Id, d.FileName, d.ContentType, d.Format, d.SizeBytes, d.UploadedAt);

    /// <summary>For EF Core queries. <c>Data</c> is a JSON column mapped in memory by <see cref="ToDto(CandidateProfileData)"/>.</summary>
    public static readonly Expression<Func<CandidateProfile, CandidateProfileDto>> ProfileProjection = p =>
        new CandidateProfileDto(p.Id, p.CvDocumentId, p.Version, p.CreatedAt, p.Data.ToDto());

    private static readonly Func<CvDocument, CvDocumentDto> CompiledDocument = DocumentProjection.Compile();
    private static readonly Func<CandidateProfile, CandidateProfileDto> CompiledProfile = ProfileProjection.Compile();

    /// <summary>For in-memory entities (e.g. right after upload).</summary>
    public static CvDocumentDto ToDto(this CvDocument document) => CompiledDocument(document);

    /// <summary>For in-memory entities (e.g. right after upload).</summary>
    public static CandidateProfileDto ToDto(this CandidateProfile profile) => CompiledProfile(profile);

    public static CandidateProfileDataDto ToDto(this CandidateProfileData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new CandidateProfileDataDto(
            new ProfileIdentityDto(data.Identity.FullName, data.Identity.Headline),
            new ProfileContactDto(data.Contact.Email, data.Contact.Phone, data.Contact.Location),
            data.Summary,
            [.. data.Skills.Select(s => new ProfileSkillDto(s.Name, s.Proficiency, s.Years))],
            [.. data.Experience.Select(e => new ProfileExperienceDto(
                e.Role, e.Company, e.StartDate, e.EndDate, e.IsCurrent, [.. e.Achievements]))],
            [.. data.Education.Select(e => new ProfileEducationDto(
                e.Institution, e.Qualification, e.Field, e.StartDate, e.EndDate))],
            [.. data.Certifications.Select(c => new ProfileCertificationDto(c.Name, c.Issuer, c.Year))],
            [.. data.Links.Select(l => new ProfileLinkDto(l.Label, l.Url))],
            data.WorkAuthorisation,
            data.CurrentEmployer,
            new ProfilePreferencesDto(
                [.. data.Preferences.TargetRoles],
                [.. data.Preferences.Locations],
                data.Preferences.Remote,
                data.Preferences.SalaryMin,
                data.Preferences.SalaryMax,
                data.Preferences.SalaryCurrency,
                data.Preferences.NoticePeriod));
    }

    /// <summary>Converts validated model output into the Domain value. Trims text and drops blank list entries.</summary>
    public static CandidateProfileData ToDomain(this CandidateProfileDataDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CandidateProfileData(
            new ProfileIdentity(Clean(dto.Identity.FullName), Clean(dto.Identity.Headline)),
            new ProfileContact(Clean(dto.Contact.Email), Clean(dto.Contact.Phone), Clean(dto.Contact.Location)),
            Clean(dto.Summary),
            [.. dto.Skills.Select(s => new ProfileSkill(s.Name.Trim(), s.Proficiency, s.Years))],
            [.. dto.Experience.Select(e => new ProfileExperience(
                e.Role.Trim(), e.Company.Trim(), Clean(e.StartDate), Clean(e.EndDate), e.IsCurrent, CleanList(e.Achievements)))],
            [.. dto.Education.Select(e => new ProfileEducation(
                e.Institution.Trim(), Clean(e.Qualification), Clean(e.Field), Clean(e.StartDate), Clean(e.EndDate)))],
            [.. dto.Certifications.Select(c => new ProfileCertification(c.Name.Trim(), Clean(c.Issuer), c.Year))],
            [.. dto.Links.Select(l => new ProfileLink(Clean(l.Label), l.Url.Trim()))],
            Clean(dto.WorkAuthorisation),
            Clean(dto.CurrentEmployer),
            new ProfilePreferences(
                CleanList(dto.Preferences.TargetRoles),
                CleanList(dto.Preferences.Locations),
                dto.Preferences.Remote,
                dto.Preferences.SalaryMin,
                dto.Preferences.SalaryMax,
                Clean(dto.Preferences.SalaryCurrency),
                Clean(dto.Preferences.NoticePeriod)));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] CleanList(IReadOnlyList<string> values) =>
        [.. values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())];
}
