using System.Text.RegularExpressions;
using JobsFinder.Domain.Applying;

namespace JobsFinder.Application.Applying;

/// <summary>What to do with one field: exactly one of the three is set.</summary>
public sealed record FieldPlan(FormField Field, EvidenceValue? Evidence, bool AttachCv, NeedsHumanReason? Reason)
{
    public static FieldPlan Fill(FormField field, EvidenceValue evidence) => new(field, evidence, false, null);

    public static FieldPlan Cv(FormField field) => new(field, null, true, null);

    public static FieldPlan NeedsHuman(FormField field, NeedsHumanReason reason) => new(field, null, false, reason);
}

/// <summary>
/// Decides which profile fact answers which field, without a model (F0.3-AC1, BR2). A label must be exactly one of the
/// known phrases of a fact; everything else is left for the candidate.
/// </summary>
public static class FieldMapper
{
    private static readonly Dictionary<FactKind, string[]> Labels = new()
    {
        [FactKind.FullName] = ["full name", "your full name", "name", "your name"],
        [FactKind.FirstName] = ["first name", "given name"],
        [FactKind.LastName] = ["last name", "family name", "surname"],
        [FactKind.Email] = ["email", "email address", "e mail", "your email"],
        [FactKind.Phone] = ["phone", "phone number", "telephone", "telephone number", "mobile", "mobile number", "mobile phone"],
        [FactKind.Location] = ["location", "current location", "your location"],
        [FactKind.LinkedIn] = ["linkedin", "linkedin profile", "linkedin url", "linkedin profile url", "linkedin link"],
        [FactKind.GitHub] = ["github", "github profile", "github url", "github profile url", "github link"],
        [FactKind.Website] =
            ["website", "personal website", "website url", "portfolio", "portfolio url", "website portfolio", "portfolio website"],
        [FactKind.CurrentCompany] = ["current company", "current employer"],
        [FactKind.CurrentTitle] = ["current title", "current job title", "current role", "current position"],
        [FactKind.NoticePeriod] = ["notice period", "your notice period", "current notice period"],
    };

    private static readonly Dictionary<string, FactKind> KindByLabel =
        Labels.SelectMany(l => l.Value.Select(phrase => (phrase, l.Key))).ToDictionary(x => x.phrase, x => x.Key);

    private static readonly HashSet<string> CvLabels =
    [
        "resume", "cv", "resume cv", "cv resume", "resume or cv", "cv or resume", "curriculum vitae",
        "your resume", "your cv", "upload resume", "upload cv", "attach resume", "attach cv", "attach resume cv",
    ];

    // The page may contradict a label ("Name" with autocomplete "email"); it can never replace one.
    private static readonly Dictionary<string, FactKind[]> KindsByAutocomplete = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = [FactKind.FullName],
        ["given-name"] = [FactKind.FirstName],
        ["family-name"] = [FactKind.LastName],
        ["email"] = [FactKind.Email],
        ["tel"] = [FactKind.Phone],
        ["url"] = [FactKind.LinkedIn, FactKind.GitHub, FactKind.Website],
        ["organization"] = [FactKind.CurrentCompany],
        ["organization-title"] = [FactKind.CurrentTitle],
    };

    private static readonly Dictionary<string, FactKind[]> KindsByInputType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = [FactKind.Email],
        ["tel"] = [FactKind.Phone],
        ["url"] = [FactKind.LinkedIn, FactKind.GitHub, FactKind.Website],
    };

    // Whole words or phrases, searched in the label and in the heading of the group the field is in.
    private static readonly string[] NeverAnswered =
    [
        // Diversity (F3.3) and other sensitive data
        "gender", "sex", "pronoun", "pronouns", "race", "racial", "ethnicity", "ethnic", "hispanic", "latino", "veteran",
        "disability", "disabled", "religion", "orientation", "transgender", "lgbtq", "age", "date of birth", "birthday",
        "marital", "nationality", "citizenship", "self identification", "eeo", "equal opportunity", "demographic",
        "ssn", "social security", "passport", "national id", "driver", "password", "bank", "criminal", "conviction",
        // Screening answers (F3.2)
        "salary", "compensation", "pay", "rate", "sponsorship", "sponsor", "visa", "authorized", "authorised",
        "authorization", "authorisation", "eligible", "relocate", "relocation", "clearance", "hear about", "heard about",
        "referral", "referred", "why", "motivation", "cover letter", "additional information", "tell us", "describe",
        // Consent
        "agree", "consent", "acknowledge", "certify", "confirm", "privacy", "terms", "signature",
        // Somebody else's data
        "reference", "references", "referee", "referees", "emergency", "manager", "supervisor", "recruiter", "spouse",
    ];

    /// <param name="fields">The fields of one step.</param>
    /// <param name="facts">From <see cref="EvidenceValue.FromProfile"/>.</param>
    /// <param name="alreadyAsked">Labels of earlier steps: the same question twice is ambiguous.</param>
    public static IReadOnlyList<FieldPlan> Map(
        IReadOnlyList<FormField> fields,
        IReadOnlyList<EvidenceValue> facts,
        IReadOnlySet<string> alreadyAsked)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(alreadyAsked);

        var questions = fields.Select(QuestionOf).ToList();
        var counts = questions.Where(q => q is not null).CountBy(q => q!).ToDictionary();

        return [.. fields.Select((field, i) => Plan(field, questions[i], counts, facts, alreadyAsked))];
    }

    /// <summary>The question a field asks, or null when it is not one that is ever answered.</summary>
    public static string? QuestionOf(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var label = FormText.LabelKey(field.Label);
        return field.Kind == FormFieldKind.File ? CvLabels.Contains(label) ? "cv" : null
            : KindByLabel.TryGetValue(label, out var kind) ? kind.ToString()
            : null;
    }

    private static FieldPlan Plan(
        FormField field,
        string? question,
        Dictionary<string, int> counts,
        IReadOnlyList<EvidenceValue> facts,
        IReadOnlySet<string> alreadyAsked)
    {
        var label = FormText.LabelKey(field.Label);
        if (field.Kind == FormFieldKind.Choice || IsNeverAnswered(label) || IsNeverAnswered(FormText.LabelKey(field.Group)))
        {
            return FieldPlan.NeedsHuman(field, NeedsHumanReason.NeverAnswered);
        }

        if (question is null)
        {
            return FieldPlan.NeedsHuman(field, NeedsHumanReason.UnknownQuestion);
        }

        // The same question twice: one of them may be about somebody else.
        if (counts[question] > 1 || alreadyAsked.Contains(question))
        {
            return FieldPlan.NeedsHuman(field, NeedsHumanReason.Ambiguous);
        }

        if (field.Kind == FormFieldKind.File)
        {
            return FieldPlan.Cv(field);
        }

        var kind = KindByLabel[label];
        if (Contradicts(KindsByInputType, field.InputType, kind)
            || Contradicts(KindsByAutocomplete, field.Autocomplete, kind)
            || (field.Kind == FormFieldKind.Text && !IsTextInput(field.InputType)))
        {
            return FieldPlan.NeedsHuman(field, NeedsHumanReason.Ambiguous);
        }

        var evidence = facts.FirstOrDefault(f => f.Kind == kind);
        if (evidence is null)
        {
            return FieldPlan.NeedsHuman(field, NeedsHumanReason.NoEvidence);
        }

        if (field.Kind == FormFieldKind.Select && OptionMatcher.FindOnly(field.Options, evidence) is null)
        {
            return FieldPlan.NeedsHuman(field, NeedsHumanReason.NoMatchingOption);
        }

        return Fits(field, evidence.Value)
            ? FieldPlan.Fill(field, evidence)
            : FieldPlan.NeedsHuman(field, NeedsHumanReason.DoesNotFit);
    }

    private static bool IsNeverAnswered(string key) =>
        key.Length > 0 && NeverAnswered.Any(phrase => $" {key} ".Contains($" {phrase} ", StringComparison.Ordinal));

    private static bool Contradicts(Dictionary<string, FactKind[]> kinds, string hint, FactKind kind) =>
        kinds.TryGetValue(hint, out var expected) && !expected.Contains(kind);

    private static bool IsTextInput(string inputType) =>
        inputType.Length == 0 || inputType is "text" or "email" or "tel" or "url" or "search";

    private static bool Fits(FormField field, string value)
    {
        if (field.MaxLength is { } max && max >= 0 && value.Length > max)
        {
            return false;
        }

        if (string.IsNullOrEmpty(field.Pattern))
        {
            return true;
        }

        try
        {
            // The pattern is untrusted; NonBacktracking runs in linear time. What it cannot run counts as "does not fit".
            return Regex.IsMatch(value, $@"\A(?:{field.Pattern})\z", RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
