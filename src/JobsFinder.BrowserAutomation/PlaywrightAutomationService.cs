using JobsFinder.Application.Applying;
using JobsFinder.Domain.Applying;
using Microsoft.Playwright;

namespace JobsFinder.BrowserAutomation;

/// <summary>
/// Opens a Greenhouse form in a fresh headless Chromium, one at a time, and cuts it off the network before handing it
/// out. Nothing a page or Playwright says is logged: both can contain values that were filled in.
/// </summary>
internal sealed class PlaywrightAutomationService(BrowserSettings settings) : IBrowserAutomationService, IDisposable
{
    private const int TooManyRequests = 429;
    private const int RequestTimeout = 408;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly string PageGuard = Scripts.Load("page-guard.js");

    private readonly SemaphoreSlim _slot = new(1, 1);

    public async Task<IFormSession> OpenAsync(Uri applyUrl, CancellationToken cancellationToken)
    {
        // The handler checks this too; the browser never trusts its caller with it.
        if (!GreenhouseApplyUrl.IsAllowed(applyUrl))
        {
            throw new BrowserAutomationException(ApplyReason.UrlNotAllowed);
        }

        await _slot.WaitAsync(cancellationToken);
        var resources = new BrowserResources(EgressGate.Start(settings.Dial), _slot);
        try
        {
            return await OpenAsync(applyUrl, resources, cancellationToken);
        }
        catch
        {
            await resources.DisposeAsync();
            throw;
        }
    }

    public void Dispose() => _slot.Dispose();

    private async Task<IFormSession> OpenAsync(Uri applyUrl, BrowserResources resources, CancellationToken cancellationToken)
    {
        var page = await StartBrowserAsync(resources, cancellationToken);

        try
        {
            await LoadAsync(page, applyUrl, cancellationToken);
            await WaitForFormAsync(page, cancellationToken);
            await SettleAsync(page, cancellationToken);
        }
        catch (PlaywrightException)
        {
            // Not passed on, and not logged: see the class comment.
            throw new BrowserAutomationException(ApplyReason.PageLoad, isTransient: true);
        }

        // Script may have moved the page on after it loaded.
        EnsureAllowed(page.Url);

        // From here on nothing can leave the browser. The gate is the guarantee; the rest keeps the page in place.
        resources.Gate.Seal();
        resources.IsSealed = true;
        try
        {
            await page.Context.SetOfflineAsync(true).WaitAsync(cancellationToken);
        }
        catch (PlaywrightException)
        {
            throw new BrowserAutomationException(ApplyReason.BrowserError);
        }

        return new PlaywrightFormSession(page, resources, settings);
    }

    private async Task<IPage> StartBrowserAsync(BrowserResources resources, CancellationToken cancellationToken)
    {
        try
        {
            resources.Playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
            resources.Browser = await resources.Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,

                // Playwright's default is off.
                ChromiumSandbox = true,
                Proxy = new Proxy { Server = resources.Gate.Endpoint.ToString() },
                Args =
                [
                    // Chromium sends loopback addresses past a proxy unless told otherwise.
                    "--proxy-bypass-list=<-loopback>",

                    // Nothing but the gate can be resolved, so nothing can be reached without it.
                    "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1",
                    "--force-webrtc-ip-handling-policy=disable_non_proxied_udp",
                    "--disable-quic",
                ],
            }).WaitAsync(cancellationToken);

            var context = await resources.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = false,
                IgnoreHTTPSErrors = settings.AcceptAnyCertificate,
                Locale = "en-US",
                ServiceWorkers = ServiceWorkerPolicy.Block,
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            }).WaitAsync(cancellationToken);
            context.SetDefaultTimeout((float)settings.ActionTimeout.TotalMilliseconds);
            await context.AddInitScriptAsync(PageGuard).WaitAsync(cancellationToken);
            await context.RouteAsync("**/*", route => HandleRequestAsync(route, resources)).WaitAsync(cancellationToken);

            var page = await context.NewPageAsync().WaitAsync(cancellationToken);
            context.Page += (_, popup) => _ = CloseQuietlyAsync(popup);
            return page;
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException or IOException or System.ComponentModel.Win32Exception)
        {
            throw new BrowserAutomationException(ApplyReason.BrowserUnavailable);
        }
    }

    /// <summary>While loading: reading only. Once sealed: nothing, and aborted quietly so that the page stays in place.</summary>
    private static async Task HandleRequestAsync(IRoute route, BrowserResources resources)
    {
        try
        {
            if (!resources.IsSealed && route.Request.Method is "GET" or "HEAD")
            {
                await route.ContinueAsync();
            }
            else
            {
                await route.AbortAsync("aborted");
            }
        }
        catch (PlaywrightException)
        {
            // The page is gone.
        }
    }

    private static async Task CloseQuietlyAsync(IPage popup)
    {
        try
        {
            await popup.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // Already closed.
        }
    }

    private async Task LoadAsync(IPage page, Uri applyUrl, CancellationToken cancellationToken)
    {
        var lastUrl = applyUrl.AbsoluteUri;
        page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest && request.Frame == page.MainFrame)
            {
                lastUrl = request.Url;
            }
        };

        IResponse? response;
        try
        {
            response = await page.GotoAsync(applyUrl.AbsoluteUri, new PageGotoOptions
            {
                Timeout = (float)settings.LoadTimeout.TotalMilliseconds,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            }).WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            // A redirect to somewhere else fails at the gate; trying again would not help.
            EnsureAllowed(lastUrl);
            throw new BrowserAutomationException(ApplyReason.PageLoad, isTransient: true);
        }

        EnsureAllowed(page.Url);
        if (response is null || response.Status >= 400)
        {
            var status = response?.Status ?? 0;
            throw new BrowserAutomationException(
                ApplyReason.PageLoad,
                isTransient: status is RequestTimeout or TooManyRequests or >= 500);
        }
    }

    private async Task WaitForFormAsync(IPage page, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.FormTimeout);
        try
        {
            while (!await GreenhouseForm.IsPresentAsync(page).WaitAsync(timeout.Token))
            {
                await Task.Delay(PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrowserAutomationException(ApplyReason.FormNotRecognised);
        }
    }

    private async Task SettleAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = (float)settings.SettleTimeout.TotalMilliseconds,
            }).WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            // Some pages never go quiet; what has arrived by now has to do.
        }
    }

    private static void EnsureAllowed(string url)
    {
        if (!GreenhouseApplyUrl.IsAllowed(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null))
        {
            throw new BrowserAutomationException(ApplyReason.UrlNotAllowed);
        }
    }
}
