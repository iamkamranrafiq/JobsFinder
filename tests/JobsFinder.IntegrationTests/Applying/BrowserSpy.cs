using System.Text.Json;
using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.BrowserAutomation;
using Microsoft.Playwright;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// Reads the filled page just before its browser closes (nothing on the page can report it: it is cut off), and counts
/// what reached the site between the moment the form was handed out and the end.
/// </summary>
internal sealed class BrowserSpy(FixtureSite site)
{
    private int _sentWhileOpen;

    /// <summary>Control id or name to its value; for a file input "name|size|sha256", "checked"/"" for choices.</summary>
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    public int OpenCalls { get; private set; }

    /// <summary>References of the values that were typed, whether or not the page kept them.</summary>
    public List<string> Typed { get; } = [];

    private FixtureSite Site => site;

    public IBrowserAutomationService Wrap(PlaywrightAutomationService inner) => new Service(this, inner);

    /// <summary>Over all sessions, from handing a form out until shortly after its browser has closed.</summary>
    public void AssertNothingWasSentOnceOpen() => Assert.Equal(0, _sentWhileOpen);

    private async Task ReadAsync(IPage page) =>
        Values = JsonSerializer.Deserialize<Dictionary<string, string>>(await page.EvaluateAsync<string>(
            """
            async () => {
              const values = {};
              for (const el of document.querySelectorAll('input, select, textarea')) {
                let value = el.value;
                if (el.type === 'checkbox' || el.type === 'radio') value = el.checked ? 'checked' : '';
                if (el.type === 'file') {
                  const file = el.files[0];
                  const hash = file && await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
                  value = file ? `${file.name}|${file.size}|${[...new Uint8Array(hash)].map((b) => b.toString(16).padStart(2, '0')).join('')}` : '';
                }
                values[el.id || el.name] = value;
              }
              const single = document.querySelector('.select__single-value');
              if (single) values['single-value'] = single.textContent;
              return JSON.stringify(values);
            }
            """))!;

    private sealed class Service(BrowserSpy spy, PlaywrightAutomationService inner) : IBrowserAutomationService
    {
        public async Task<IFormSession> OpenAsync(Uri applyUrl, CancellationToken cancellationToken)
        {
            spy.OpenCalls++;
            var session = (PlaywrightFormSession)await inner.OpenAsync(applyUrl, cancellationToken);

            // What was sent just before the cut may still be on its way to the server's counter.
            await Task.Delay(100, cancellationToken);
            return new Session(spy, session, spy.Site.Hits.Count);
        }
    }

    private sealed class Session(BrowserSpy spy, PlaywrightFormSession inner, int hitsWhenOpened) : IFormSession
    {
        public Task<FormStep> ReadStepAsync(CancellationToken cancellationToken) => inner.ReadStepAsync(cancellationToken);

        public Task<FieldApplyResult> FillAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken)
        {
            spy.Typed.Add(value.Reference);
            return inner.FillAsync(fieldId, value, cancellationToken);
        }

        public Task<FieldApplyResult> SelectOptionAsync(string fieldId, EvidenceValue value, CancellationToken cancellationToken) =>
            inner.SelectOptionAsync(fieldId, value, cancellationToken);

        public Task<FieldApplyResult> AttachCvAsync(string fieldId, CvFileDto cv, CancellationToken cancellationToken) =>
            inner.AttachCvAsync(fieldId, cv, cancellationToken);

        public Task<byte[]> CaptureAsync(CancellationToken cancellationToken) => inner.CaptureAsync(cancellationToken);

        public Task<bool> GoToNextStepAsync(CancellationToken cancellationToken) => inner.GoToNextStepAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await spy.ReadAsync(inner.Page).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception e) when (e is PlaywrightException or TimeoutException)
            {
                // The page is gone or hangs; a test that needs the values fails on them.
            }
            finally
            {
                await inner.DisposeAsync();
                await Task.Delay(300);
                spy._sentWhileOpen += spy.Site.Hits.Count - hitsWhenOpened;
            }
        }
    }
}
