using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Applying;

namespace JobsFinder.UnitTests.Application.Applying;

internal sealed class FakeApplyRunRepository : IApplyRunRepository
{
    public List<ApplyRun> Runs { get; } = [];

    public List<ApplyScreenshot> Screenshots { get; } = [];

    public void Add(ApplyRun run, IEnumerable<ApplyScreenshot> screenshots)
    {
        Runs.Add(run);
        Screenshots.AddRange(screenshots);
    }
}

/// <summary>Opens the session, after throwing the queued failures.</summary>
internal sealed class FakeBrowser(FakeFormSession session) : IBrowserAutomationService
{
    public Queue<Exception> Failures { get; } = new();

    public List<Uri> Opened { get; } = [];

    public Task<IFormSession> OpenAsync(Uri applyUrl, CancellationToken cancellationToken)
    {
        Opened.Add(applyUrl);
        return Failures.TryDequeue(out var failure)
            ? Task.FromException<IFormSession>(failure)
            : Task.FromResult<IFormSession>(session);
    }
}

internal sealed class FakeFormSession(params FormStep[] steps) : IFormSession
{
    private int _step;

    public Dictionary<string, string> Filled { get; } = [];

    public Dictionary<string, FieldApplyResult> Results { get; } = [];

    public CvFileDto? Attached { get; private set; }

    public bool Moves { get; set; } = true;

    public bool Hangs { get; set; }

    public bool Disposed { get; private set; }

    public Exception? CaptureError { get; set; }

    public async Task<FormStep> ReadStepAsync(CancellationToken cancellationToken)
    {
        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return steps[_step];
    }

    public Task<FieldApplyResult> FillAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken)
    {
        Filled[fieldId] = value.Value;
        return Task.FromResult(Results.GetValueOrDefault(fieldId, FieldApplyResult.Applied));
    }

    public Task<FieldApplyResult> SelectOptionAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken) =>
        FillAsync(fieldId, value, cancellationToken);

    public Task<FieldApplyResult> AttachCvAsync(string fieldId, CvFileDto cv, CancellationToken cancellationToken)
    {
        Attached = cv;
        return Task.FromResult(Results.GetValueOrDefault(fieldId, FieldApplyResult.Applied));
    }

    public Task<byte[]> CaptureAsync(CancellationToken cancellationToken) =>
        CaptureError is null ? Task.FromResult<byte[]>([1, 2, 3]) : Task.FromException<byte[]>(CaptureError);

    public Task<bool> GoToNextStepAsync(CancellationToken cancellationToken)
    {
        if (Moves && _step < steps.Length - 1)
        {
            _step++;
        }

        return Task.FromResult(Moves);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class Form
{
    public static FormField Field(
        string label,
        FormFieldKind kind = FormFieldKind.Text,
        bool required = false,
        string? group = null,
        string inputType = "",
        string autocomplete = "",
        int? maxLength = null,
        string? pattern = null,
        params string[] options) =>
        new(label, kind, label, group is null ? [] : [group], required, inputType, autocomplete, maxLength, pattern, string.Empty, options);

    public static FormStep Final(params FormField[] fields) => new(fields, FormStepEnd.Final);

    public static FormStep WithNext(params FormField[] fields) => new(fields, FormStepEnd.Next);
}
