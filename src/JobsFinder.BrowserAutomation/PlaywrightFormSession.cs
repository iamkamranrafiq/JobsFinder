using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Applying;
using Microsoft.Playwright;

namespace JobsFinder.BrowserAutomation;

/// <summary>
/// An open form whose browser is already cut off the network. F0.3-BR1: there are two clicks in this class, both on
/// controls that qualified first (a list option and "next"), and no key presses, navigation or submit.
/// </summary>
internal sealed class PlaywrightFormSession : IFormSession
{
    private const int MaxPageHeight = 16_000;
    private const int JpegQuality = 80;
    private const int MaxListOptions = 100;

    // An option that is, or is in, something that can submit or navigate is not an option.
    private const string ListOption = "[role=option]:not(button, input, a, button *, a *)";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IPage _page;
    private readonly BrowserResources _resources;
    private readonly BrowserSettings _settings;
    private GreenhouseForm? _form;

    public PlaywrightFormSession(IPage page, BrowserResources resources, BrowserSettings settings)
    {
        if (!resources.IsSealed)
        {
            throw new InvalidOperationException("A form may only be filled in a browser that is cut off the network.");
        }

        _page = page;
        _resources = resources;
        _settings = settings;
    }

    /// <summary>For tests, which read the filled page back.</summary>
    internal IPage Page => _page;

    public async Task<FormStep> ReadStepAsync(CancellationToken cancellationToken)
    {
        _form = await GuardAsync(() => GreenhouseForm.ReadAsync(_page), cancellationToken);
        return new FormStep(_form.Fields, _form.Next is not null);
    }

    public Task<FieldApplyResult> FillAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        var element = Get(fieldId).Element;
        return TryApplyAsync(
            async () =>
            {
                await element.FillAsync(value.Value);
                await BlurAsync(element);
                if (await element.InputValueAsync() == value.Value)
                {
                    return FieldApplyResult.Applied;
                }

                await element.FillAsync(string.Empty);
                return FieldApplyResult.NotApplied;
            },
            cancellationToken);
    }

    public Task<FieldApplyResult> SelectOptionAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        var (field, element) = Get(fieldId);
        return TryApplyAsync(
            () => field.Kind == FormFieldKind.Select
                ? SelectNativeAsync(field, element, value)
                : SelectFromListAsync(element, value, cancellationToken),
            cancellationToken);
    }

    public Task<FieldApplyResult> AttachCvAsync(string fieldId, CvFileDto cv, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cv);

        var (field, element) = Get(fieldId);
        var name = CvFileName.Clean(cv.FileName);
        if (!CvFileName.IsAccepted(field.Accept, name, cv.ContentType))
        {
            return Task.FromResult(FieldApplyResult.NotApplied);
        }

        return TryApplyAsync(
            async () =>
            {
                // From memory: no copy of the CV is written to disk.
                await element.SetInputFilesAsync(new FilePayload { Name = name, MimeType = cv.ContentType, Buffer = cv.Content });
                var attached = await element.EvaluateAsync<string>(
                    "el => el.files.length === 1 ? `${el.files[0].size}|${el.files[0].name}` : ''");
                return attached == $"{cv.Content.Length}|{name}" ? FieldApplyResult.Applied : FieldApplyResult.NotApplied;
            },
            cancellationToken);
    }

    public async Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
    {
        byte[] jpeg;
        try
        {
            var height = await _page.EvaluateAsync<double>("() => document.documentElement.scrollHeight").WaitAsync(cancellationToken);
            if (height > MaxPageHeight)
            {
                throw new BrowserAutomationException(ApplyReason.Screenshot);
            }

            jpeg = await _page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
                Type = ScreenshotType.Jpeg,
                Quality = JpegQuality,
                Scale = ScreenshotScale.Css,
                Animations = ScreenshotAnimations.Disabled,
                Timeout = (float)_settings.LoadTimeout.TotalMilliseconds,
            }).WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            throw new BrowserAutomationException(ApplyReason.Screenshot);
        }

        return jpeg.Length is > 0 and <= ApplyScreenshot.MaxSizeBytes
            ? jpeg
            : throw new BrowserAutomationException(ApplyReason.Screenshot);
    }

    public async Task<bool> GoToNextStepAsync(CancellationToken cancellationToken)
    {
        // Read again: the control must still qualify, with the fields filled in.
        var form = await GuardAsync(() => GreenhouseForm.ReadAsync(_page), cancellationToken);
        if (form.Next is not { } next)
        {
            return false;
        }

        _form = null;
        try
        {
            await next.ClickAsync().WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            return false;
        }

        return await GuardAsync(() => HasMovedAsync(form.Signature, cancellationToken), cancellationToken);
    }

    public ValueTask DisposeAsync() => _resources.DisposeAsync();

    private async Task<bool> HasMovedAsync(string signature, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _settings.ReactionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await GreenhouseForm.ReadSignatureAsync(_page) is { } now && now != signature)
            {
                return true;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return false;
    }

    private static async Task<FieldApplyResult> SelectNativeAsync(FormField field, IElementHandle element, EvidenceValue value)
    {
        if (OptionMatcher.FindOnly(field.Options, value) is not { } index)
        {
            return FieldApplyResult.NoMatchingOption;
        }

        await element.SelectOptionAsync(new SelectOptionValue { Index = index });
        var selected = await element.EvaluateAsync<string>("el => el.selectedOptions.length === 1 ? el.selectedOptions[0].text : ''");
        return OptionMatcher.FindOnly([selected], value) is null ? FieldApplyResult.NotApplied : FieldApplyResult.Applied;
    }

    private async Task<FieldApplyResult> SelectFromListAsync(
        IElementHandle element, EvidenceValue value, CancellationToken cancellationToken)
    {
        await element.FillAsync(value.Value);
        if (await WaitForOptionAsync(element, value, cancellationToken) is not { } option)
        {
            await element.FillAsync(string.Empty);
            await BlurAsync(element);
            return FieldApplyResult.NoMatchingOption;
        }

        await option.ClickAsync();
        await BlurAsync(element);

        // Either the input itself shows the choice, or an element around it does.
        var chosen = await element.EvaluateAsync<string[]>(
            """
            el => {
              const shown = [el.value];
              for (let e = el.parentElement, i = 0; e && e.tagName !== 'FORM' && i < 4; e = e.parentElement, i++) {
                shown.push((e.innerText || '').trim());
              }
              return shown;
            }
            """);
        return chosen.Any(text => OptionMatcher.FindOnly([text], value) is not null)
            ? FieldApplyResult.Applied
            : FieldApplyResult.NotApplied;
    }

    /// <summary>The only matching option of the list that belongs to the combobox.</summary>
    private async Task<IElementHandle?> WaitForOptionAsync(
        IElementHandle element, EvidenceValue value, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _settings.ReactionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var listId = await element.GetAttributeAsync("aria-controls") ?? await element.GetAttributeAsync("aria-owns");
            if (!string.IsNullOrEmpty(listId))
            {
                var list = await _page.EvaluateHandleAsync("id => document.getElementById(id)", listId);
                var options = list.AsElement() is { } listElement ? await listElement.QuerySelectorAllAsync(ListOption) : [];
                if (options.Count is > 0 and <= MaxListOptions)
                {
                    var texts = new List<string>();
                    foreach (var option in options)
                    {
                        texts.Add(await option.IsVisibleAsync() ? await option.InnerTextAsync() : string.Empty);
                    }

                    if (OptionMatcher.FindOnly(texts, value) is { } index)
                    {
                        return options[index];
                    }
                }
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return null;
    }

    private static async Task BlurAsync(IElementHandle element) => await element.EvaluateAsync("el => el.blur()");

    private (FormField Field, IElementHandle Element) Get(string fieldId) =>
        (_form ?? throw new InvalidOperationException("Read the step first.")).Get(fieldId);

    /// <summary>A control that does not cooperate is left for the candidate; a browser that is gone ends the run.</summary>
    private async Task<FieldApplyResult> TryApplyAsync(Func<Task<FieldApplyResult>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action().WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            return _page.IsClosed || _resources.Browser is not { IsConnected: true }
                ? throw new BrowserAutomationException(ApplyReason.BrowserError)
                : FieldApplyResult.NotApplied;
        }
    }

    /// <summary>Playwright's exceptions stay in here: their text can contain values that were filled in.</summary>
    private static async Task<T> GuardAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action().WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            throw new BrowserAutomationException(ApplyReason.BrowserError);
        }
    }
}
