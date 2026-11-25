namespace JobsFinder.Domain.Applying;

public enum ApplyFieldOutcome
{
    Filled = 0,
    NeedsHuman = 1,
}

/// <summary>Why a field was left for the candidate (F0.3-BR2).</summary>
public enum NeedsHumanReason
{
    /// <summary>The label is not one the applier knows how to answer.</summary>
    UnknownQuestion = 0,

    /// <summary>The question is known, but the profile has no usable answer.</summary>
    NoEvidence = 1,

    /// <summary>More than one reading is possible.</summary>
    Ambiguous = 2,

    /// <summary>A kind of question the applier never answers (consent, diversity, salary, motivation, ...).</summary>
    NeverAnswered = 3,

    /// <summary>The answer does not fit the field; it is never shortened or reformatted to fit.</summary>
    DoesNotFit = 4,

    NoMatchingOption = 5,

    /// <summary>The page did not keep the value; the field is as it was before.</summary>
    NotApplied = 6,

    /// <summary>The page put something else in the field and would not give it up. Open even if the field is optional.</summary>
    NotCleared = 7,

    /// <summary>It was not there when the step was filled; the page asks it because of what was filled in.</summary>
    AppearedLater = 8,

    /// <summary>It was answered, but the answer was gone or different in the end.</summary>
    NotKept = 9,
}

/// <summary>
/// What happened to one form field. Holds a reference into the profile, never the value: the run also stores the
/// profile version, so the value can be looked up by whoever may see the profile.
/// </summary>
/// <param name="Step">1 for the first step.</param>
/// <param name="Label">Cleaned page text. Untrusted: show it as plain text only.</param>
/// <param name="Required">As the page marks it.</param>
/// <param name="Outcome">Filled, or left for the candidate.</param>
/// <param name="Evidence">Where the answer came from, e.g. <c>contact.email</c>; set only when filled.</param>
/// <param name="Reason">Why it was left; set only when not filled.</param>
public sealed record ApplyFieldResult(
    int Step,
    string Label,
    bool Required,
    ApplyFieldOutcome Outcome,
    string? Evidence,
    NeedsHumanReason? Reason)
{
    public const int LabelMaxLength = 300;

    public static ApplyFieldResult Filled(int step, string label, bool required, string evidence) =>
        new(step, label, required, ApplyFieldOutcome.Filled, evidence, null);

    public static ApplyFieldResult NeedsHuman(int step, string label, bool required, NeedsHumanReason reason) =>
        new(step, label, required, ApplyFieldOutcome.NeedsHuman, null, reason);
}
