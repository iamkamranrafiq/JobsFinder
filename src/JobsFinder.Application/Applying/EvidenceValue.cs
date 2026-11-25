using System.Text.RegularExpressions;
using JobsFinder.Application.Cv;

namespace JobsFinder.Application.Applying;

/// <summary>What an answer is about; decides which field labels it may answer.</summary>
public enum FactKind
{
    FullName,
    FirstName,
    LastName,
    Email,
    Phone,
    Location,
    LinkedIn,
    GitHub,
    Website,
    CurrentCompany,
    CurrentTitle,
    NoticePeriod,
}

/// <summary>
/// An answer taken from the stored profile. F0.3-BR2 is enforced by this type: the form session accepts nothing else,
/// and <see cref="FromProfile"/> is the only way to get one, so no other text can ever be typed into a page.
/// </summary>
public sealed partial class EvidenceValue
{
    private const int MaxNameLength = 100;

    private EvidenceValue(FactKind kind, string reference, string value)
    {
        Kind = kind;
        Reference = reference;
        Value = value;
    }

    public FactKind Kind { get; }

    /// <summary>Where the value is in the profile, e.g. <c>contact.email</c>. Safe to store and log; the value is not.</summary>
    public string Reference { get; }

    public string Value { get; }

    /// <summary>The reference, so the value cannot end up in a log or message by accident.</summary>
    public override string ToString() => Reference;

    /// <summary>
    /// The usable facts of a profile, at most one per kind. A fact that is missing, implausible or not unique is left
    /// out: it is then no evidence, and the field goes to the candidate.
    /// </summary>
    public static IReadOnlyList<EvidenceValue> FromProfile(CandidateProfileDataDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var facts = new List<EvidenceValue>();
        AddNames(facts, profile.Identity.FullName);
        Add(facts, FactKind.Email, "contact.email", profile.Contact.Email, EmailPattern());
        Add(facts, FactKind.Phone, "contact.phone", profile.Contact.Phone, PhonePattern());
        Add(facts, FactKind.Location, "contact.location", profile.Contact.Location, PlainTextPattern());
        Add(facts, FactKind.CurrentCompany, "currentEmployer", profile.CurrentEmployer, PlainTextPattern());
        AddCurrentTitle(facts, profile.Experience);
        Add(facts, FactKind.NoticePeriod, "preferences.noticePeriod", profile.Preferences.NoticePeriod, PlainTextPattern());
        AddLink(facts, FactKind.LinkedIn, profile.Links, (_, url) => IsHost(url, "linkedin.com"));
        AddLink(facts, FactKind.GitHub, profile.Links, (_, url) => IsHost(url, "github.com"));
        AddLink(facts, FactKind.Website, profile.Links, (label, url) =>
            FormText.LabelKey(label) is "website" or "personal website" or "portfolio"
            && !IsHost(url, "linkedin.com")
            && !IsHost(url, "github.com"));
        return facts;
    }

    private static void AddNames(List<EvidenceValue> facts, string? fullName)
    {
        const string reference = "identity.fullName";

        var name = fullName?.Trim();
        if (name is null || name.Length > MaxNameLength || !NamePattern().IsMatch(name))
        {
            return;
        }

        facts.Add(new EvidenceValue(FactKind.FullName, reference, name));

        // Only a two-word name is split; for any other shape the parts would be a guess (JF-9 decision D7).
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2)
        {
            facts.Add(new EvidenceValue(FactKind.FirstName, reference, words[0]));
            facts.Add(new EvidenceValue(FactKind.LastName, reference, words[1]));
        }
    }

    private static void AddCurrentTitle(List<EvidenceValue> facts, IReadOnlyList<ProfileExperienceDto> experience)
    {
        var current = experience.Select((e, i) => (Experience: e, Index: i)).Where(x => x.Experience.IsCurrent).ToList();
        if (current.Count == 1)
        {
            Add(
                facts,
                FactKind.CurrentTitle,
                $"experience[{current[0].Index}].role",
                current[0].Experience.Role,
                PlainTextPattern());
        }
    }

    private static void AddLink(
        List<EvidenceValue> facts,
        FactKind kind,
        IReadOnlyList<ProfileLinkDto> links,
        Func<string?, Uri, bool> isKind)
    {
        var found = links
            .Select((link, index) => (link.Label, link.Url, Index: index, Uri: ParseLink(link.Url)))
            .Where(x => x.Uri is not null && isKind(x.Label, x.Uri))
            .ToList();

        if (found.Count == 1)
        {
            facts.Add(new EvidenceValue(kind, $"links[{found[0].Index}].url", found[0].Url.Trim()));
        }
    }

    private static Uri? ParseLink(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.UserInfo.Length == 0
        && PlainTextPattern().IsMatch(url.Trim())
            ? uri
            : null;

    private static bool IsHost(Uri url, string domain) =>
        url.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || url.IdnHost.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private static void Add(List<EvidenceValue> facts, FactKind kind, string reference, string? value, Regex plausible)
    {
        var text = value?.Trim();
        if (!string.IsNullOrEmpty(text) && plausible.IsMatch(text))
        {
            facts.Add(new EvidenceValue(kind, reference, text));
        }
    }

    // Letters and name punctuation only: text extracted from a PDF can run a name into an email address.
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}'’. \-]*\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,24}\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?(?:[ ().\-]*[0-9]){7,15}\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PhonePattern();

    // One line without "@": an address in here means two values ran together.
    [GeneratedRegex(@"^[^@\p{C}\u2028\u2029]+\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlainTextPattern();
}
