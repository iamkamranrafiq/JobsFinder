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

    // What a click could set off other than choosing. An option that is, is in, or contains one of these is not an option.
    private const string Interactive =
        "button, input, select, textarea, a, area, label, summary, [role=button], [role=link], [contenteditable]";

    private const string ListOption =
        $"[role=option]:not({Interactive}):not(:is({Interactive}) *):not(:has({Interactive}))";

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
        return new FormStep(_form.Fields, _form.End);
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

                return await ClearTextAsync(element);
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

        var before = await element.EvaluateAsync<int>("el => el.selectedIndex");
        await element.SelectOptionAsync(new SelectOptionValue { Index = index });
        await BlurAsync(element);
        var selected = await element.EvaluateAsync<string>("el => el.selectedOptions.length === 1 ? el.selectedOptions[0].text : ''");
        if (OptionMatcher.FindOnly([selected], value) is not null)
        {
            return FieldApplyResult.Applied;
        }

        await element.SelectOptionAsync(new SelectOptionValue { Index = before });
        return await element.EvaluateAsync<int>("el => el.selectedIndex") == before
            ? FieldApplyResult.NotApplied
            : FieldApplyResult.NotCleared;
    }

    private async Task<FieldApplyResult> SelectFromListAsync(
        IElementHandle element, EvidenceValue value, CancellationToken cancellationToken)
    {
        // Only the start of the value is typed, to search for it. The full value can then only come from the list.
        var search = value.Value[..^1];
        if (search.Trim().Length == 0)
        {
            return FieldApplyResult.NoMatchingOption;
        }

        var shownBefore = await ShownAroundAsync(element);
        await element.FillAsync(search);
        if (await WaitForOptionAsync(element, value, cancellationToken) is not { } option)
        {
            return await ClearTextAsync(element) == FieldApplyResult.NotCleared
                ? FieldApplyResult.NotCleared
                : FieldApplyResult.NoMatchingOption;
        }

        await option.ClickAsync();
        await BlurAsync(element);

        // An open list still shows its options, so it says nothing about what was chosen.
        var input = await element.InputValueAsync();
        if (!await IsVisibleAsync(option))
        {
            var shownNow = await ShownAroundAsync(element);
            if (OptionMatcher.FindOnly([input], value) is not null
                || (input.Length == 0 && !ShowsValue(shownBefore, value) && ShowsValue(shownNow, value)))
            {
                return FieldApplyResult.Applied;
            }
        }

        var cleared = await ClearTextAsync(element);
        return cleared == FieldApplyResult.NotApplied && (await ShownAroundAsync(element)).SequenceEqual(shownBefore)
            ? FieldApplyResult.NotApplied
            : FieldApplyResult.NotCleared;
    }

    private static bool ShowsValue(string[] texts, EvidenceValue value) =>
        texts.Any(text => OptionMatcher.FindOnly([text], value) is not null);

    /// <summary>The texts of the elements around a searchable list, where it shows what is chosen.</summary>
    private static async Task<string[]> ShownAroundAsync(IElementHandle element) =>
        await element.EvaluateAsync<string[]>(
            """
            el => {
              const shown = [];
              for (let e = el.parentElement, i = 0; e && e.tagName !== 'FORM' && i < 3; e = e.parentElement, i++) {
                shown.push((e.innerText || '').trim());
              }
              return shown;
            }
            """);

    private static async Task<bool> IsVisibleAsync(IElementHandle option)
    {
        try
        {
            return await option.IsVisibleAsync();
        }
        catch (PlaywrightException)
        {
            // Removed from the page together with its list.
            return false;
        }
    }

    private static async Task<FieldApplyResult> ClearTextAsync(IElementHandle element)
    {
        await element.FillAsync(string.Empty);
        await BlurAsync(element);
        return (await element.InputValueAsync()).Length == 0 ? FieldApplyResult.NotApplied : FieldApplyResult.NotCleared;
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
