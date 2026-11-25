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
    private readonly List<Answer> _answers = [];
    private GreenhouseForm? _form;
    private int _step = 1;

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

        var (field, element) = Get(fieldId);
        return ApplyAsync(
            field,
            element,
            async () =>
            {
                await element.FillAsync(value.Value);
                await BlurAsync(element);
                return FieldApplyResult.Applied;
            },
            () => ClearTextAsync(element),
            holds: async e => await e.InputValueAsync() == value.Value,
            cancellationToken);
    }

    public Task<FieldApplyResult> SelectOptionAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        var (field, element) = Get(fieldId);
        return field.Kind == FormFieldKind.Select
            ? SelectNativeAsync(field, element, value, cancellationToken)
            : SelectFromListAsync(field, element, value, cancellationToken);
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

        return ApplyAsync(
            field,
            element,
            async () =>
            {
                // From memory: no copy of the CV is written to disk.
                await element.SetInputFilesAsync(new FilePayload { Name = name, MimeType = cv.ContentType, Buffer = cv.Content });
                return FieldApplyResult.Applied;
            },
            () => element.SetInputFilesAsync(Array.Empty<FilePayload>()),
            holds: async e => await e.EvaluateAsync<string>(
                "el => el.files && el.files.length === 1 ? `${el.files[0].size}|${el.files[0].name}` : ''") == $"{cv.Content.Length}|{name}",
            cancellationToken);
    }

    public Task<IReadOnlyList<int>> FindLostAnswersAsync(CancellationToken cancellationToken) =>
        GuardAsync<IReadOnlyList<int>>(
            async () =>
            {
                var lost = new List<int>();
                for (var i = 0; i < _answers.Count; i++)
                {
                    if (!await IsHeldAsync(_answers[i]))
                    {
                        lost.Add(i);
                    }
                }

                return lost;
            },
            cancellationToken);

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
        if (form.Next is not { } next || form.Signature != _form?.Signature)
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

        if (!await GuardAsync(() => HasMovedAsync(form.Signature, cancellationToken), cancellationToken))
        {
            return false;
        }

        _step++;
        return true;
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

    private Task<FieldApplyResult> SelectNativeAsync(
        FormField field, IElementHandle element, EvidenceValue value, CancellationToken cancellationToken)
    {
        if (OptionMatcher.FindOnly(field.Options, value) is not { } index)
        {
            return Task.FromResult(FieldApplyResult.NoMatchingOption);
        }

        var before = -1;
        return ApplyAsync(
            field,
            element,
            async () =>
            {
                before = await element.EvaluateAsync<int>("el => el.selectedIndex");
                await element.SelectOptionAsync(new SelectOptionValue { Index = index });
                await BlurAsync(element);
                return FieldApplyResult.Applied;
            },
            () => before < 0 ? Task.CompletedTask : element.SelectOptionAsync(new SelectOptionValue { Index = before }),
            holds: async e => OptionMatcher.FindOnly(
                [await e.EvaluateAsync<string>("el => el.selectedOptions && el.selectedOptions.length === 1 ? el.selectedOptions[0].text : ''")],
                value) is not null,
            cancellationToken);
    }

    private Task<FieldApplyResult> SelectFromListAsync(
        FormField field, IElementHandle element, EvidenceValue value, CancellationToken cancellationToken)
    {
        // Only the start of the value is typed, to search for it. The full value can then only come from the list.
        var search = value.Value[..^1];
        if (search.Trim().Length == 0)
        {
            return Task.FromResult(FieldApplyResult.NoMatchingOption);
        }

        return ApplyAsync(
            field,
            element,
            async () =>
            {
                if (ShowsValue(await ShownAroundAsync(element), value))
                {
                    // It already says so, whoever chose it; a choice could then not be told from no choice.
                    return FieldApplyResult.NotApplied;
                }

                await element.FillAsync(search);
                if (await WaitForOptionAsync(element, value, cancellationToken) is not { } option)
                {
                    return FieldApplyResult.NoMatchingOption;
                }

                await option.ClickAsync();
                await BlurAsync(element);

                // An open list still shows its options, so it says nothing about what was chosen.
                return await IsVisibleAsync(option) ? FieldApplyResult.NotApplied : FieldApplyResult.Applied;
            },
            () => ClearTextAsync(element),
            holds: async e =>
            {
                var input = await e.InputValueAsync();
                return input.Length == 0
                    ? ShowsValue(await ShownAroundAsync(e), value)
                    : OptionMatcher.FindOnly([input], value) is not null;
            },
            cancellationToken);
    }

    private static bool ShowsValue(string[] texts, EvidenceValue value) =>
        texts.Any(text => OptionMatcher.FindOnly([text], value) is not null);

    /// <summary>The texts of the elements around a searchable list that hold no other control: there it shows its choice.</summary>
    private static async Task<string[]> ShownAroundAsync(IElementHandle element) =>
        await element.EvaluateAsync<string[]>(
            """
            el => {
              const shown = [];
              for (let e = el.parentElement, i = 0; e && e.tagName !== 'FORM' && i < 3; e = e.parentElement, i++) {
                if (e.querySelectorAll('input:not([type=hidden]), select, textarea').length > 1) break;
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

    private static async Task ClearTextAsync(IElementHandle element)
    {
        await element.FillAsync(string.Empty);
        await BlurAsync(element);
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

    /// <summary>
    /// The only way anything on the page is changed. Whatever happens once an action has started, it ends in one of three
    /// ways: the value is read back (Applied), the field is checked to be as it was (NotApplied, NoMatchingOption), or neither
    /// is certain (NotCleared). A browser that is gone ends the run.
    /// </summary>
    private async Task<FieldApplyResult> ApplyAsync(
        FormField field,
        IElementHandle element,
        Func<Task<FieldApplyResult>> act,
        Func<Task> putBack,
        Func<IElementHandle, Task<bool>> holds,
        CancellationToken cancellationToken)
    {
        // Nothing has started yet, so a field that cannot even be read is as it was.
        if (await TryAsync(() => StateAsync(element), cancellationToken) is not { } before)
        {
            return FieldApplyResult.NotApplied;
        }

        var result = FieldApplyResult.NotApplied;
        await TryAsync(
            async () =>
            {
                result = await act();
                if (result == FieldApplyResult.Applied && !await holds(element))
                {
                    result = FieldApplyResult.NotApplied;
                }

                return string.Empty;
            },
            cancellationToken,
            onError: () => result = FieldApplyResult.NotApplied);

        if (result == FieldApplyResult.Applied)
        {
            _answers.Add(new Answer(_step, field.Key, element, holds));
            return result;
        }

        await TryAsync(
            async () =>
            {
                await putBack();
                return string.Empty;
            },
            cancellationToken);
        return await TryAsync(() => StateAsync(element), cancellationToken) == before ? result : FieldApplyResult.NotCleared;
    }

    /// <summary>What a field holds and, for a searchable list, shows. Null when it is no longer on the page.</summary>
    private static async Task<string?> StateAsync(IElementHandle element)
    {
        var own = await element.EvaluateAsync<string?>(
            """
            el => !el.isConnected ? null : JSON.stringify([
              el.value ?? '', el.selectedIndex ?? '', [...(el.files || [])].map(f => [f.name, f.size]),
            ])
            """);
        var isList = await element.GetAttributeAsync("role") == "combobox";
        return own is null ? null : own + (isList ? string.Join('\n', await ShownAroundAsync(element)) : string.Empty);
    }

    private async Task<bool> IsHeldAsync(Answer answer)
    {
        if (await answer.Element.EvaluateAsync<bool>("el => el.isConnected"))
        {
            return await answer.Holds(answer.Element);
        }

        // Drawn again: the field of the same key in the latest reading. An earlier step may simply be gone.
        return answer.Step == _step
            ? _form?.FindByKey(answer.Key) is { } again && await answer.Holds(again)
            : true;
    }

    /// <summary>Null on a Playwright error, whose text stays in here; throws only when the browser is gone.</summary>
    private async Task<string?> TryAsync(Func<Task<string?>> action, CancellationToken cancellationToken, Action? onError = null)
    {
        try
        {
            return await action().WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            if (_page.IsClosed || _resources.Browser is not { IsConnected: true })
            {
                throw new BrowserAutomationException(ApplyReason.BrowserError);
            }

            onError?.Invoke();
            return null;
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

    /// <summary>An answer the page took, and how to see whether it still holds it.</summary>
    private sealed record Answer(int Step, string Key, IElementHandle Element, Func<IElementHandle, Task<bool>> Holds);
}
