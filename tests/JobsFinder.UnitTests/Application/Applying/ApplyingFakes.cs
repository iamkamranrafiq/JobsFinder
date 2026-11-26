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

    /// <summary>How a step reads once something has been filled in.</summary>
    public Dictionary<int, Func<FormStep>> Later { get; } = [];

    public List<int> Lost { get; } = [];

    public bool Hangs { get; set; }

    public bool Disposed { get; private set; }

    public Exception? CaptureError { get; set; }

    public async Task<FormStep> ReadStepAsync(CancellationToken cancellationToken)
    {
        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        // After something was filled, the page may read differently.
        return Later.TryGetValue(_step, out var later) && (Filled.Count > 0 || Attached is not null) ? later() : steps[_step];
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

    public Task<IReadOnlyList<int>> FindLostAnswersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>>(Lost);

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
        new(label, label, kind, label, true, group is null ? [] : [group], required, inputType, autocomplete, maxLength, pattern, string.Empty, options, string.Empty);

    public static FormStep Final(params FormField[] fields) => new(fields, FormStepEnd.Final);

    public static FormStep WithNext(params FormField[] fields) => new(fields, FormStepEnd.Next);
}
