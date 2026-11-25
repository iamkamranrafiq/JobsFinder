using JobsFinder.Application.Cv;
using JobsFinder.Domain.Applying;

namespace JobsFinder.Application.Applying;

/// <summary>Opens public application forms in a browser. Implemented in JobsFinder.BrowserAutomation.</summary>
public interface IBrowserAutomationService
{
    /// <summary>
    /// Loads the page once (no retries) and finds its application form. Throws <see cref="BrowserAutomationException"/>.
    /// The returned session can no longer reach the network, so nothing that is filled in can leave the browser.
    /// </summary>
    Task<IFormSession> OpenAsync(Uri applyUrl, CancellationToken cancellationToken);
}

/// <summary>
/// One open form. F0.3-BR1: this interface has no way to submit, press a key, run script, navigate or click an element
/// of the caller's choice. An implementation clicks in exactly two places: an option of a list it opened itself
/// (<see cref="SelectOptionAsync"/>) and the single control that qualifies as "next" (<see cref="GoToNextStepAsync"/>).
/// Field ids are valid until the next <see cref="ReadStepAsync"/>.
/// </summary>
public interface IFormSession : IAsyncDisposable
{
    /// <summary>Reads the visible fields of the current step. Everything in the answer is untrusted page content.</summary>
    Task<FormStep> ReadStepAsync(CancellationToken cancellationToken);

    /// <summary>Types the value as it is and reads it back; a field that did not keep it is cleared.</summary>
    Task<FieldApplyResult> FillAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken);

    /// <summary>Chooses the one option that equals the value (see <see cref="OptionMatcher"/>), if there is exactly one.</summary>
    Task<FieldApplyResult> SelectOptionAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken);

    /// <summary>Puts the CV in a file field and reads back its name and size.</summary>
    Task<FieldApplyResult> AttachCvAsync(string fieldId, CvFileDto cv, CancellationToken cancellationToken);

    /// <summary>A full-page JPEG of the current step, at most <see cref="ApplyScreenshot.MaxSizeBytes"/>.</summary>
    Task<byte[]> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves a multi-step form on. Clicks only if the step has exactly one control that is plainly "next" and no visible
    /// submit control. False when the form did not move.
    /// </summary>
    Task<bool> GoToNextStepAsync(CancellationToken cancellationToken);
}

public enum FieldApplyResult
{
    Applied,
    NoMatchingOption,
    NotApplied,
}

public enum FormFieldKind
{
    Text,
    TextArea,

    /// <summary>A native select; its options are known.</summary>
    Select,

    /// <summary>An ARIA combobox; its options are known only after typing.</summary>
    Combobox,
    File,

    /// <summary>Checkboxes, radio buttons and anything else that is never filled.</summary>
    Choice,
}

/// <summary>A form field as the page describes it. All text is untrusted, cleaned and capped.</summary>
public sealed record FormField(
    string Id,
    FormFieldKind Kind,
    string Label,
    string Group,
    bool Required,
    string InputType,
    string Autocomplete,
    int? MaxLength,
    string? Pattern,
    string Accept,
    IReadOnlyList<string> Options);

/// <param name="Fields">The visible fields of the step.</param>
/// <param name="HasNext">False on the final step, where the form can only be submitted.</param>
public sealed record FormStep(IReadOnlyList<FormField> Fields, bool HasNext);

/// <summary>
/// The browser could not do its part. It carries no inner exception and no page or browser text: those can contain
/// values that were filled in.
/// </summary>
public sealed class BrowserAutomationException(ApplyReason reason, bool isTransient = false)
    : Exception($"Browser automation failed: {reason}.")
{
    public ApplyReason Reason { get; } = reason;

    /// <summary>Whether loading the page again may help.</summary>
    public bool IsTransient { get; } = isTransient;
}
