using System.Collections.Concurrent;
using JobsFinder.Application;
using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.BrowserAutomation;
using JobsFinder.Domain.Applying;
using JobsFinder.Domain.Cv;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.IntegrationTests.Cv;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// The real handler, database and Chromium against a <see cref="FixtureSite"/>, with a made-up profile and CV.
/// Disposing checks, for every test, that nothing was sent to the site after it was cut off and that it was only read.
/// </summary>
internal sealed class ApplyHost : IAsyncDisposable
{
    public const string JobPath = "/acme/jobs/123";

    private readonly WebApplicationFactory<Program> _factory;

    private ApplyHost(WebApplicationFactory<Program> factory, FixtureSite site, LogSink logs, BrowserSpy spy)
    {
        Spy = spy;
        _factory = factory;
        Site = site;
        Logs = logs;
    }

    public FixtureSite Site { get; }

    public LogSink Logs { get; }

    public BrowserSpy Spy { get; }

    public IServiceProvider Services => _factory.Services;

    public static byte[] CvBytes { get; } = CvFixtures.TextPdf();

    public static CandidateProfileDataDto Profile { get; } = CandidateProfileDataDto.Empty with
    {
        Identity = new ProfileIdentityDto("Zelda Quillfeather", "Engineer"),
        Contact = new ProfileContactDto("zelda.quillfeather@example.com", "+44 20 7946 0123", "Leeds, UK"),
        Links =
        [
            new ProfileLinkDto("LinkedIn", "https://www.linkedin.com/in/zelda-quillfeather"),
            new ProfileLinkDto("GitHub", "https://github.com/zelda-quillfeather"),
        ],
        CurrentEmployer = "Quill Systems Ltd",
        Preferences = new ProfilePreferencesDto([], [], null, 81234, 91234, "GBP", "3 months"),
    };

    public static async Task<ApplyHost> StartAsync(
        ApiFactory root, ApplySettings? settings = null, BrowserSettings? browser = null)
    {
        var site = await FixtureSite.StartAsync();
        var logs = new LogSink();
        var spy = new BrowserSpy(site);
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace));
            services.AddApplyPreparation();
            services.AddBrowserAutomation(browser ?? site.Settings());
            services.RemoveAll<IBrowserAutomationService>();
            services.AddSingleton<PlaywrightAutomationService>();
            services.AddSingleton(spy);
            services.AddSingleton<IBrowserAutomationService>(sp =>
                spy.Wrap(sp.GetRequiredService<PlaywrightAutomationService>()));
            services.RemoveAll<ApplySettings>();
            services.AddSingleton(settings ?? new ApplySettings
            {
                RetryDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)],
                QuietTime = TimeSpan.FromMilliseconds(600),
                MaxSettleTime = TimeSpan.FromSeconds(4),
            });
        }));
        return new ApplyHost(factory, site, logs, spy);
    }

    /// <summary>Serves the page as a board, stores the profile and a posting that points at it, and runs the applier.</summary>
    public async Task<ApplyRunDto> RunAsync(string html, CandidateProfileDataDto? profile = null)
    {
        Site.Pages[JobPath] = html;
        return await RunUrlAsync(FixtureSite.Host + JobPath, profile);
    }

    public async Task<ApplyRunDto> RunUrlAsync(string applyUrl, CandidateProfileDataDto? profile = null)
    {
        var postingId = await SeedAsync(applyUrl, profile);
        await using var scope = Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<PrepareApplicationHandler>()
            .HandleAsync(new PrepareApplicationCommand(postingId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);

        // The browser is closed by now; give anything it may have sent on the way out time to arrive.
        await Task.Delay(300);
        return result.Value!;
    }

    public async Task<Guid> SeedAsync(string applyUrl, CandidateProfileDataDto? profile = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = TimeProvider.System.GetUtcNow();
        var document = CvDocument.Create(@"C:\fakepath\zelda-cv.pdf", CvFormat.Pdf, CvBytes, now);
        db.Add(document);
        db.Add(CandidateProfile.CreateFromParse(document.Id, (profile ?? Profile).ToDomain(), now));

        var id = Guid.NewGuid().ToString("N");
        var posting = JobPosting.Create("test", id, "Acme", $"Engineer {id}", null, false, $"Description {id}", applyUrl, null, now);
        db.Add(posting);
        await db.SaveChangesAsync();
        return posting.Id;
    }

    public async Task<IReadOnlyList<ApplyScreenshot>> ScreenshotsAsync(Guid runId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ApplyScreenshots
            .AsNoTracking().Where(s => s.ApplyRunId == runId).OrderBy(s => s.Step).ToListAsync();
    }

    public async Task<ApplyRun?> StoredAsync(Guid runId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ApplyRuns
            .AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Spy.AssertNothingWasSentOnceOpen();
        await Site.DisposeAsync();

        // A worker may open a WebSocket while the page loads, when there is nothing to send yet; the gate cuts it.
        Assert.DoesNotContain(Site.Hits, h => h.Method is not ("GET" or "HEAD" or "WEBSOCKET"));
        Assert.DoesNotContain(Site.Hits, h => h.Path.StartsWith("/submit", StringComparison.Ordinal));
        Assert.DoesNotContain(Site.Hits, h => h.Path.StartsWith("/leak", StringComparison.Ordinal));
        Assert.DoesNotContain(Site.WebSocketMessages, m => m.StartsWith("leak", StringComparison.Ordinal));
    }
}

/// <summary>Everything that is logged, with the text of exceptions.</summary>
internal sealed class LogSink : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();

    public string Text => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _lines.Enqueue($"{formatter(state, exception)} {exception}");

    public void Dispose()
    {
    }
}
