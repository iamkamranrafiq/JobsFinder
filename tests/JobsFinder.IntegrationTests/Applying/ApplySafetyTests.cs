using System.Net;
using System.Net.Sockets;
using System.Text;
using JobsFinder.Application.Applying;
using JobsFinder.BrowserAutomation;
using JobsFinder.Domain.Applying;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// Nothing is submitted, nothing leaves, nothing else is reached. JF-11 owns the formal no-submit guarantee (F0.3-AC4);
/// these tests show that JF-9's code is contained without it. <see cref="ApplyHost"/> adds its checks to every test.
/// </summary>
[Collection(ApiTestGroup.Name)]
[Trait("REQ", "F0.3-AC1")]
public sealed class ApplySafetyTests(ApiFactory factory)
{
    private const string MinimalForm =
        """
        <form id="application-form" method="post" action="/submit">
          <label for="email">Email*</label><input id="email" name="email" required>
          <label for="cv">CV*</label><input id="cv" name="cv" type="file" required>
          <button type="submit">Submit application</button>
        </form>
        """;

    private const string OnEmail = "const email = document.getElementById('email'); const form = email.form; ";

    /// <summary>What hostile pages do with a value once they have it.</summary>
    public static TheoryData<string, string> Hostile => new()
    {
        { "form.submit on change", OnEmail + "email.addEventListener('change', () => form.submit());" },
        { "requestSubmit on input", OnEmail + "email.addEventListener('input', () => form.requestSubmit());" },
        { "click submit on blur", OnEmail + "email.addEventListener('blur', () => form.querySelector('button').click());" },
        { "get form", OnEmail + "email.addEventListener('input', () => { form.method = 'get'; form.action = '/leak'; form.submit(); });" },
        { "form in a new frame", OnEmail + "email.addEventListener('input', () => { const f = document.createElement('iframe'); document.body.append(f); const d = f.contentDocument; d.body.innerHTML = '<form method=post action=/leak><input name=v></form>'; d.forms[0].elements[0].value = email.value; f.contentWindow.HTMLFormElement.prototype.submit.call(d.forms[0]); });" },
        { "fetch", OnEmail + "email.addEventListener('input', () => fetch('/leak', { method: 'POST', body: email.value, keepalive: true }));" },
        { "timer", OnEmail + "setInterval(() => { if (email.value) fetch('/leak?v=' + email.value); }, 50);" },
        { "image", OnEmail + "email.addEventListener('input', () => { new Image().src = '/leak?v=' + email.value; });" },
        { "beacon on leaving", OnEmail + "for (const e of ['pagehide', 'beforeunload', 'unload', 'visibilitychange']) addEventListener(e, () => { if (email.value) navigator.sendBeacon('/leak', email.value); }); email.addEventListener('blur', () => { location.href = 'about:blank'; });" },
        { "beacon now", OnEmail + "email.addEventListener('input', () => navigator.sendBeacon('/leak', email.value));" },
        { "worker started before", OnEmail + "const w = new Worker('/worker.js'); email.addEventListener('input', () => w.postMessage(email.value));" },
        { "socket opened before", OnEmail + "let s; try { s = new WebSocket('wss://job-boards.greenhouse.io/socket'); } catch { } email.addEventListener('input', () => s?.send('leak ' + email.value));" },
        { "socket in a worker", OnEmail + "const w = new Worker('/socket-worker.js'); email.addEventListener('input', () => w.postMessage(email.value));" },
        { "popup", OnEmail + "email.addEventListener('input', () => open('/leak?v=' + email.value));" },
        { "navigation", OnEmail + "email.addEventListener('blur', () => { location.href = '/leak?v=' + email.value; });" },
        { "cv upload on change", "document.getElementById('cv').addEventListener('change', (e) => { const x = new XMLHttpRequest(); x.open('POST', '/leak'); x.send(e.target.files[0]); });" },
        { "retry when online", OnEmail + "addEventListener('online', () => fetch('/leak', { method: 'POST', body: email.value }));" },
        { "dialog on close", "addEventListener('beforeunload', (e) => { e.preventDefault(); e.returnValue = 'x'; });" },
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public async Task HandleAsync_HostilePage_SendsNothingAndSubmitsNothing_F0_3_AC1(string name, string script)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);
        AddWorkers(host.Site);

        var run = await host.RunAsync(FixturePages.Page(MinimalForm, script));

        // The value was really there to be taken. Whatever the page did with it, the checks in ApplyHost.DisposeAsync hold.
        Assert.Contains("contact.email", host.Spy.Typed);
        Assert.NotEqual(ApplyReason.PageLoad, run.Reason);
    }

    /// <summary>
    /// Negative control: the same pages do reach the server when a plain browser types the value. Without this, a fixture
    /// that never sends anything would make the tests above pass for nothing.
    /// </summary>
    [Fact]
    public async Task Fixtures_WithoutTheGuards_DoSubmitAndLeak_F0_3_AC1()
    {
        await using var site = await FixtureSite.StartAsync();
        AddWorkers(site);
        site.Pages["/submit"] = site.Pages["/leak"] = "ok";
        await using var gate = EgressGate.Start(site.DialAsync);
        using var playwright = await Playwright.CreateAsync();

        var silent = new List<string>();
        foreach (var row in Hostile)
        {
            var name = (string)row[0];
            site.ClearHits();
            var messages = site.WebSocketMessages.Count;
            site.Pages[ApplyHost.JobPath] = FixturePages.Page(MinimalForm, (string)row[1]);

            await using (var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Proxy = new Proxy { Server = gate.Endpoint.ToString() },
            }))
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
                var page = await context.NewPageAsync();
                await page.GotoAsync(FixtureSite.Host + ApplyHost.JobPath);
                await page.WaitForTimeoutAsync(300);
                page.SetDefaultTimeout(2000);
                await page.Locator("#cv").SetInputFilesAsync(new FilePayload { Name = "a.pdf", MimeType = "application/pdf", Buffer = [1] });
                try
                {
                    await page.Locator("#email").FillAsync("someone@example.com");
                    await page.Locator("#email").EvaluateAsync("el => el.blur()");
                }
                catch (Exception e) when (e is PlaywrightException or TimeoutException)
                {
                    // The form has been submitted and is gone, which is what this test is about.
                }

                await Task.Delay(600);
                await context.CloseAsync();
            }

            await Task.Delay(200);
            var sent = site.Hits.Any(h => h.Path is "/submit" or "/leak") || site.WebSocketMessages.Count > messages;
            if (!sent)
            {
                silent.Add(name);
            }
        }

        // These two only act on an event that a plain run does not raise; they are here for the run above.
        Assert.Equal(["retry when online", "dialog on close"], silent);
    }

    public static TheoryData<string> RefusedUrls => new(
    [
        "http://job-boards.greenhouse.io/acme/jobs/123",
        "https://job-boards.greenhouse.io:8443/acme/jobs/123",
        "https://user:secret@job-boards.greenhouse.io/acme/jobs/123",
        "https://www.greenhouse.io/acme/jobs/123",
        "https://greenhouse.io.evil.com/acme/jobs/123",
        "https://evilgreenhouse.io/acme/jobs/123",
        "https://jobs.lever.co/acme/123",
        "https://127.0.0.1/acme/jobs/123",
        "https://localhost/acme/jobs/123",
        "https://169.254.169.254/latest/meta-data",
        "mailto:jobs@greenhouse.io",
    ]);

    [Theory]
    [MemberData(nameof(RefusedUrls))]
    public async Task HandleAsync_UrlThatIsNotAPublicBoard_OpensNoBrowser_F0_3_AC1(string url)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunUrlAsync(url);

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.UrlNotAllowed), (run.Status, run.Reason));
        Assert.Equal(0, host.Spy.OpenCalls);
        Assert.DoesNotContain("secret", host.Logs.Text, StringComparison.Ordinal);

        // The adapter refuses on its own too.
        var service = host.Services.GetRequiredService<PlaywrightAutomationService>();
        var refused = await Assert.ThrowsAsync<BrowserAutomationException>(() => service.OpenAsync(new Uri(url), CancellationToken.None));
        Assert.Equal(ApplyReason.UrlNotAllowed, refused.Reason);
    }

    private static readonly string[] Canaries =
    [
        "http://127.0.0.1:{0}/", "https://127.0.0.1:{0}/", "http://localhost:{0}/", "http://a.localhost:{0}/",
        "http://[::1]:{1}/", "http://[::ffff:127.0.0.1]:{0}/", "http://0.0.0.0:{0}/", "http://2130706433:{0}/",
        "http://0x7f.1:{0}/", "http://127.0.0.1.nip.io:{0}/", "https://job-boards.greenhouse.io:{0}/",
        "https://example.com/", "file:///C:/Windows/win.ini", "http://job-boards.greenhouse.io/",
    ];

    public static TheoryData<string> CanaryUrls => new(Canaries);

    [Theory]
    [MemberData(nameof(CanaryUrls))]
    public async Task HandleAsync_RedirectOffTheBoard_ReachesNothingAndIsNotRetried_F0_3_AC1(string target)
    {
        using var canary = new Canary();
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Redirects[ApplyHost.JobPath] = canary.Format(target);

        var run = await host.RunUrlAsync(FixtureSite.Host + ApplyHost.JobPath);

        Assert.Equal(ApplyRunStatus.Failed, run.Status);
        Assert.Contains(run.Reason!.Value, new[] { ApplyReason.UrlNotAllowed, ApplyReason.PageLoad });
        Assert.Equal(0, canary.Connections);
        Assert.All(host.Site.DialledHosts, h => Assert.Equal("job-boards.greenhouse.io", h));
    }

    [Fact]
    public async Task HandleAsync_RedirectOffTheBoard_IsReportedAsUrlNotAllowedAfterOneAttempt_F0_3_AC1()
    {
        using var canary = new Canary();
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Redirects[ApplyHost.JobPath] = canary.Format("http://127.0.0.1:{0}/");

        var run = await host.RunUrlAsync(FixtureSite.Host + ApplyHost.JobPath);

        Assert.Equal(ApplyReason.UrlNotAllowed, run.Reason);
        Assert.Equal(1, host.Spy.OpenCalls);
    }

    [Fact]
    public async Task HandleAsync_RedirectToAGreenhousePageThatIsNoBoard_IsRefused_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Redirects[ApplyHost.JobPath] = "https://www.greenhouse.io/elsewhere";
        host.Site.Pages["/elsewhere"] = FixturePages.NewBoard();

        var run = await host.RunUrlAsync(FixtureSite.Host + ApplyHost.JobPath);

        Assert.Equal(ApplyReason.UrlNotAllowed, run.Reason);
        Assert.Empty(run.Fields);
    }

    [Fact]
    public async Task HandleAsync_PageReachesForInternalAddresses_ReachesNothing_F0_3_AC1()
    {
        using var canary = new Canary();
        await using var host = await ApplyHost.StartAsync(factory);
        var targets = string.Join(',', Canaries.Select(url => $"'{canary.Format(url)}'").Where(t => !t.Contains("file:", StringComparison.Ordinal)));

        var run = await host.RunAsync(FixturePages.NewBoard(script:
            $$"""
            for (const t of [{{targets}}]) {
              new Image().src = t;
              try { fetch(t, { mode: 'no-cors' }).catch(() => {}); } catch { }
              try { navigator.sendBeacon(t, 'x'); } catch { }
              try { new EventSource(t); } catch { }
              try { new WebSocket(t.replace('http', 'ws')); } catch { }
              for (const [tag, attribute] of [['iframe', 'src'], ['script', 'src']]) {
                const e = document.createElement(tag); e[attribute] = t; document.body.append(e);
              }
              const link = document.createElement('link'); link.rel = 'preconnect'; link.href = t; document.head.append(link);
            }
            """));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.Equal(0, canary.Connections);
        Assert.All(host.Site.DialledHosts, h => Assert.EndsWith("greenhouse.io", h, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<button role='option' type='submit'>3 months</button>")]
    [InlineData("<a role='option' href='/leak'>3 months</a>")]
    [InlineData("<button type='submit'><span role='option'>3 months</span></button>")]
    [InlineData("<div role='option'><button type='submit'>3 months</button></div>")]
    [InlineData("<div role='option'><a href='/leak'>3 months</a></div>")]
    [InlineData("<div role='option'><label for='resume'>3 months</label></div>")]
    [InlineData("<div role='option'><span role='button'>3 months</span></div>")]
    [InlineData("<label for='resume'><span role='option'>3 months</span></label>")]
    [InlineData("<details><summary><span role='option'>3 months</span></summary></details>")]
    [InlineData("<div role='button'><span role='option'>3 months</span></div>")]
    public async Task HandleAsync_ListOptionThatIsAButtonOrLink_IsNotClicked_F0_3_AC1(string option)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(
            $"<label for=\"notice\">Notice period</label><input id=\"notice\" role=\"combobox\" aria-controls=\"list\"><div id=\"list\">{option}</div>",
            "document.getElementById('list').addEventListener('click', () => { document.title = 'clicked'; document.getElementById('phone').value = 'clicked'; });"));

        Assert.Equal(NeedsHumanReason.NoMatchingOption, run.Fields.Single(f => f.Label == "Notice period").Reason);
        Assert.Equal("+44 20 7946 0123", host.Spy.Values["phone"]);
    }

    [Fact]
    public async Task HandleAsync_UntrustedCertificate_IsNotAccepted_F0_3_AC1()
    {
        var site = await FixtureSite.StartAsync();
        await using var _ = site;
        site.Pages[ApplyHost.JobPath] = FixturePages.NewBoard();
        await using var host = await ApplyHost.StartAsync(factory, browser: new BrowserSettings { Dial = site.DialAsync });

        var run = await host.RunUrlAsync(FixtureSite.Host + ApplyHost.JobPath);

        Assert.Equal(ApplyReason.PageLoad, run.Reason);
        Assert.Empty(site.Hits);
    }

    [Theory]
    [InlineData("job-boards.greenhouse.io", 443, true)]
    [InlineData("cdn.greenhouse.io", 443, true)]
    [InlineData("job-boards.greenhouse.io", 80, false)]
    [InlineData("evilgreenhouse.io", 443, false)]
    [InlineData("greenhouse.io.evil.com", 443, false)]
    [InlineData("127.0.0.1", 443, false)]
    public void EgressGate_IsAllowed_OnlyGreenhouseOverHttps_F0_3_AC1(string host, int port, bool allowed) =>
        Assert.Equal(allowed, EgressGate.IsAllowed(host, port));

    [Fact]
    public async Task EgressGate_Sealed_CutsOpenTunnelsAndRefusesNewOnes_F0_3_AC1()
    {
        await using var site = await FixtureSite.StartAsync();
        await using var gate = EgressGate.Start(site.DialAsync);
        using var open = await ConnectAsync(gate, "CONNECT job-boards.greenhouse.io:443 HTTP/1.1\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 200", await ReadAsync(open), StringComparison.Ordinal);

        gate.Seal();

        Assert.Equal(string.Empty, await ReadAsync(open));
        using var late = await ConnectAsync(gate, "CONNECT job-boards.greenhouse.io:443 HTTP/1.1\r\n\r\n");
        Assert.Equal(string.Empty, await ReadAsync(late));
        Assert.Single(site.DialledHosts);
    }

    [Fact]
    public async Task EgressGate_Disposed_HasNoConnectionLeftRunning_F0_3_AC1()
    {
        var dialling = new TaskCompletionSource();
        var ended = false;
        var gate = EgressGate.Start(async (_, ct) =>
        {
            dialling.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                await Task.Delay(200, CancellationToken.None);
                ended = true;
            }

            throw new InvalidOperationException("Not reached.");
        });
        using var socket = await ConnectAsync(gate, "CONNECT job-boards.greenhouse.io:443 HTTP/1.1\r\n\r\n");
        await dialling.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await gate.DisposeAsync();

        Assert.True(ended);
    }

    [Fact]
    public async Task EgressGate_DialFailsUnexpectedly_DropsThatConnectionOnly_F0_3_AC1()
    {
        await using var site = await FixtureSite.StartAsync();
        var calls = 0;
        var unobserved = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.AddRange(e.Exception.InnerExceptions.Where(x => x.Message == "jf9-dial"));
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            await using (var gate = EgressGate.Start((host, ct) =>
                Interlocked.Increment(ref calls) == 1 ? throw new ArgumentException("jf9-dial") : site.DialAsync(host, ct)))
            {
                using var first = await ConnectAsync(gate, "CONNECT job-boards.greenhouse.io:443 HTTP/1.1\r\n\r\n");
                Assert.Equal(string.Empty, await ReadAsync(first));
                using var second = await ConnectAsync(gate, "CONNECT job-boards.greenhouse.io:443 HTTP/1.1\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 200", await ReadAsync(second), StringComparison.Ordinal);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }
    }

    [Theory]
    [InlineData("CONNECT 127.0.0.1:443 HTTP/1.1\r\n\r\n")]
    [InlineData("CONNECT job-boards.greenhouse.io:22 HTTP/1.1\r\n\r\n")]
    [InlineData("GET http://job-boards.greenhouse.io/ HTTP/1.1\r\n\r\n")]
    [InlineData("nonsense\r\n\r\n")]
    public async Task EgressGate_AnythingElse_IsDropped_F0_3_AC1(string request)
    {
        await using var site = await FixtureSite.StartAsync();
        await using var gate = EgressGate.Start(site.DialAsync);

        using var socket = await ConnectAsync(gate, request);

        Assert.Equal(string.Empty, await ReadAsync(socket));
        Assert.Empty(site.DialledHosts);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.31.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2606:4700::1111", true)]
    public void PublicHostDialer_IsPublic_F0_3_AC1(string address, bool isPublic) =>
        Assert.Equal(isPublic, PublicHostDialer.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task PublicHostDialer_NameOfThisMachine_IsNotDialled_F0_3_AC1() =>
        await Assert.ThrowsAsync<SocketException>(() => PublicHostDialer.DialAsync("localhost", CancellationToken.None));

    [Fact]
    public async Task HandleAsync_AnyOutcome_LogsNoPersonalDataAndNoUrl_F0_3_AC1()
    {
        // Short, because the last run hangs until it is over.
        await using var host = await ApplyHost.StartAsync(factory, new ApplySettings { Budget = TimeSpan.FromSeconds(15) });

        await host.RunAsync(FixturePages.NewBoard());
        await host.RunAsync(FixturePages.NewBoard(
            script: "document.getElementById('email').addEventListener('input', () => document.getElementById('email').remove());"));
        await host.RunAsync(FixturePages.NewBoard(script: "document.getElementById('email').addEventListener('input', () => { for (;;) { } });")
            .Replace("<title>", "<title>hangs", StringComparison.Ordinal));

        Assert.Contains("Apply run", host.Logs.Text, StringComparison.Ordinal);
        Assert.All(
            ["zelda", "quillfeather", "7946 0123", "Leeds", "Quill Systems", "81234", "/acme/jobs", "Playwright"],
            secret => Assert.DoesNotContain(secret, host.Logs.Text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_Run_LeavesNoFileInTheWorkingFolder_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        var before = Directory.GetFileSystemEntries(Environment.CurrentDirectory);

        await host.RunAsync(FixturePages.NewBoard());

        Assert.Equal(before, Directory.GetFileSystemEntries(Environment.CurrentDirectory));
    }

    [Fact]
    public async Task FormSession_BrowserNotCutOff_CannotBeCreated_F0_3_AC1()
    {
        using var slot = new SemaphoreSlim(0, 1);
        await using var resources = new BrowserResources(EgressGate.Start(PublicHostDialer.DialAsync), slot);

        Assert.Throws<InvalidOperationException>(() => new PlaywrightFormSession(null!, resources, new BrowserSettings()));
    }

    [Fact]
    public void Api_HasNoBrowser_F0_3_AC1()
    {
        Assert.DoesNotContain(
            typeof(Program).Assembly.GetReferencedAssemblies(),
            a => a.Name is "JobsFinder.BrowserAutomation" or "Microsoft.Playwright");
        Assert.Null(factory.Services.GetService<IBrowserAutomationService>());
    }

    private static void AddWorkers(FixtureSite site)
    {
        site.Pages["/worker.js"] = "onmessage = (m) => { fetch('/leak', { method: 'POST', body: m.data }); };";
        site.Pages["/socket-worker.js"] =
            "const s = new WebSocket('wss://job-boards.greenhouse.io/socket'); onmessage = (m) => { s.send('leak ' + m.data); };";
    }

    private static async Task<Socket> ConnectAsync(EgressGate gate, string request)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, gate.Endpoint.Port);
        await socket.SendAsync(Encoding.ASCII.GetBytes(request));
        return socket;
    }

    /// <summary>What the gate answers; empty when it closes the connection.</summary>
    private static async Task<string> ReadAsync(Socket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1024];
        try
        {
            return Encoding.ASCII.GetString(buffer, 0, await socket.ReceiveAsync(buffer, timeout.Token));
        }
        catch (SocketException)
        {
            return string.Empty;
        }
    }

    /// <summary>Counts connections on loopback, IPv4 and IPv6.</summary>
    private sealed class Canary : IDisposable
    {
        private readonly TcpListener _v4 = new(IPAddress.Loopback, 0);
        private readonly TcpListener _v6 = new(IPAddress.IPv6Loopback, 0);
        private int _connections;

        public Canary()
        {
            Listen(_v4);
            Listen(_v6);
        }

        public int Connections => _connections;

        public string Format(string target) => target
            .Replace("{0}", $"{((IPEndPoint)_v4.LocalEndpoint).Port}", StringComparison.Ordinal)
            .Replace("{1}", $"{((IPEndPoint)_v6.LocalEndpoint).Port}", StringComparison.Ordinal);

        public void Dispose()
        {
            _v4.Dispose();
            _v6.Dispose();
        }

        private void Listen(TcpListener listener)
        {
            listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using var connection = await listener.AcceptSocketAsync();
                        Interlocked.Increment(ref _connections);
                    }
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    // Disposed.
                }
            });
        }
    }
}
