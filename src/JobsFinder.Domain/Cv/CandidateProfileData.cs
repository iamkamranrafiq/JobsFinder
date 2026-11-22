namespace JobsFinder.Domain.Cv;

/// <summary>
/// The structured profile extracted from a CV (F0.1-BR2). Immutable value; every field the CV does not
/// state is null or an empty list. Field labels are always English, whatever the CV language.
/// </summary>
public sealed record CandidateProfileData(
    ProfileIdentity Identity,
    ProfileContact Contact,
    string? Summary,
    IReadOnlyList<ProfileSkill> Skills,
    IReadOnlyList<ProfileExperience> Experience,
    IReadOnlyList<ProfileEducation> Education,
    IReadOnlyList<ProfileCertification> Certifications,
    IReadOnlyList<ProfileLink> Links,
    string? WorkAuthorisation,
    string? CurrentEmployer,
    ProfilePreferences Preferences)
{
    public const int ShortTextMaxLength = 200;
    public const int LongTextMaxLength = 4000;
    public const int UrlMaxLength = 2048;
    public const int MaxListItems = 100;
}

public sealed record ProfileIdentity(string? FullName, string? Headline);

public sealed record ProfileContact(string? Email, string? Phone, string? Location);

public sealed record ProfileSkill(string Name, SkillProficiency? Proficiency, decimal? Years);

/// <summary>Dates are "YYYY-MM" or "YYYY"; CVs rarely state a day.</summary>
public sealed record ProfileExperience(
    string Role,
    string Company,
    string? StartDate,
    string? EndDate,
    bool IsCurrent,
    IReadOnlyList<string> Achievements);

public sealed record ProfileEducation(
    string Institution,
    string? Qualification,
    string? Field,
    string? StartDate,
    string? EndDate);

public sealed record ProfileCertification(string Name, string? Issuer, int? Year);

public sealed record ProfileLink(string? Label, string Url);

public sealed record ProfilePreferences(
    IReadOnlyList<string> TargetRoles,
    IReadOnlyList<string> Locations,
    RemotePreference? Remote,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string? NoticePeriod);

public enum SkillProficiency
{
    Beginner,
    Intermediate,
    Advanced,
    Expert,
}

public enum RemotePreference
{
    Remote,
    Hybrid,
    OnSite,
    Any,
}
