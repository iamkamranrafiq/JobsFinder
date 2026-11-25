using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Application.Applying;
using JobsFinder.Domain.Applying;
using Microsoft.Playwright;

namespace JobsFinder.BrowserAutomation;

/// <summary>The Greenhouse adapter: finds the form of a public board page and reads one step of it.</summary>
internal sealed class GreenhouseForm
{
    private const int MaxFields = 100;
    private const int MaxOptions = 100;
    private const int MaxOptionLength = 100;

    private static readonly HashSet<string> FileButtonLabels =
        [string.Empty, "attach", "attach file", "upload", "upload file", "choose file", "select file", "browse"];

    private static readonly string ReadStep = Scripts.Load("read-step.js");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, (FormField Field, IElementHandle Element)> _fields;

    private GreenhouseForm(
        Dictionary<string, (FormField Field, IElementHandle Element)> fields,
        FormStepEnd end,
        IElementHandle? next,
        string signature)
    {
        _fields = fields;
        End = end;
        Next = next;
        Signature = signature;
    }

    public IReadOnlyList<FormField> Fields => [.. _fields.Values.Select(f => f.Field)];

    public FormStepEnd End { get; }

    /// <summary>The one control that plainly means "next"; set only when the step ends that way.</summary>
    public IElementHandle? Next { get; }

    /// <summary>Differs between steps; used to see whether the form has moved on.</summary>
    public string Signature { get; }

    public static async Task<bool> IsPresentAsync(IPage page) => (await EvaluateAsync(page)).Meta.Found;

    /// <summary>Throws <see cref="BrowserAutomationException"/> when there is no form, or no way on and no way to submit.</summary>
    public static async Task<GreenhouseForm> ReadAsync(IPage page)
    {
        var (meta, result) = await EvaluateAsync(page);
        if (!meta.Found || meta.End == FormStepEnd.None || meta.Fields.Count > MaxFields)
        {
            throw new BrowserAutomationException(ApplyReason.FormNotRecognised);
        }

        var elements = await (await result.GetPropertyAsync("elements")).GetPropertiesAsync();
        var fields = new Dictionary<string, (FormField, IElementHandle)>();
        for (var i = 0; i < meta.Fields.Count; i++)
        {
            var id = i.ToString(CultureInfo.InvariantCulture);
            if (elements.TryGetValue(id, out var handle) && handle.AsElement() is { } element)
            {
                fields[id] = (ToField(id, meta.Fields[i]), element);
            }
        }

        var next = meta.End == FormStepEnd.Next ? (await result.GetPropertyAsync("next")).AsElement() : null;
        return new GreenhouseForm(fields, meta.End, next, SignatureOf(meta));
    }

    /// <summary>The signature of the step now on the page, or null when there is no form.</summary>
    public static async Task<string?> ReadSignatureAsync(IPage page)
    {
        var (meta, _) = await EvaluateAsync(page);
        return meta.Found ? SignatureOf(meta) : null;
    }

    public (FormField Field, IElementHandle Element) Get(string fieldId) =>
        _fields.TryGetValue(fieldId, out var field)
            ? field
            : throw new ArgumentException("The field does not belong to the current step.", nameof(fieldId));

    private static async Task<(StepMeta Meta, IJSHandle Result)> EvaluateAsync(IPage page)
    {
        var result = await page.EvaluateHandleAsync(ReadStep);
        var json = await (await result.GetPropertyAsync("meta")).JsonValueAsync<JsonElement>();
        try
        {
            return (json.Deserialize<StepMeta>(Json) ?? StepMeta.None, result);
        }
        catch (JsonException)
        {
            // The page has tampered with what the script uses.
            return (StepMeta.None, result);
        }
    }

    private static string SignatureOf(StepMeta meta) =>
        string.Join('\n', meta.Fields.Select(f => $"{f.Kind}|{f.Name}|{f.Label}"));

    private static FormField ToField(string id, FieldMeta meta)
    {
        var label = FormText.Clean(meta.Label, ApplyFieldResult.LabelMaxLength);
        List<string> groups = [.. (meta.Groups ?? []).Select(g => FormText.Clean(g, ApplyFieldResult.LabelMaxLength))];

        // The boards label a file input "Attach"; what it is for is the name of the group around it.
        if (meta.Kind == FormFieldKind.File && groups.Count > 0 && FileButtonLabels.Contains(FormText.LabelKey(label)))
        {
            label = groups[0];
        }

        return new FormField(
            id,
            meta.Kind,
            label,
            groups,
            meta.Required,
            (meta.InputType ?? string.Empty).ToLowerInvariant(),
            meta.Autocomplete ?? string.Empty,
            meta.MaxLength,
            string.IsNullOrEmpty(meta.Pattern) ? null : meta.Pattern,
            meta.Accept ?? string.Empty,

            // A list that had to be cut cannot show that an option is the only match.
            meta.Options is null || meta.Options.Count > MaxOptions
                ? []
                : [.. meta.Options.Select(o => FormText.Clean(o, MaxOptionLength))]);
    }

    private sealed record StepMeta(bool Found, FormStepEnd End, IReadOnlyList<FieldMeta> Fields)
    {
        public static StepMeta None { get; } = new(false, FormStepEnd.None, []);

        public IReadOnlyList<FieldMeta> Fields { get; } = Fields ?? [];
    }

    private sealed record FieldMeta(
        FormFieldKind Kind,
        string? Label,
        IReadOnlyList<string>? Groups,
        bool Required,
        string? InputType,
        string? Autocomplete,
        int? MaxLength,
        string? Pattern,
        string? Accept,
        string? Name,
        IReadOnlyList<string>? Options);
}
