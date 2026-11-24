using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using JobsFinder.Application.Common;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.Infrastructure.Sourcing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace JobsFinder.IntegrationTests.QA.JF7;

/// <summary>One request the real ApifyJobSource sent to the fake Apify.</summary>
internal sealed record CapturedRequest(
    HttpMethod Method,
    string Uri,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

/// <summary>A scripted Apify API v2. Records every request. Never touches the network.</summary>
internal sealed class FakeApify : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<CapturedRequest> _requests = [];
    private readonly Queue<string> _pollStatuses = new();

    public HttpStatusCode StartStatusCode { get; set; } = HttpStatusCode.Created;

    /// <summary>Status the run has when it is started.</summary>
    public string RunStatusOnStart { get; set; } = "SUCCEEDED";

    /// <summary>Statuses returned by successive polls; "RUNNING" for ever when <see cref="PollForever"/> is set.</summary>
    public void QueuePoll(params string[] statuses)
    {
        foreach (var s in statuses)
        {
            _pollStatuses.Enqueue(s);
        }
    }

    public bool PollForever { get; set; }

    public HttpStatusCode PollStatusCode { get; set; } = HttpStatusCode.OK;

    public string? StartBodyOverride { get; set; }

    public HttpStatusCode ItemsStatusCode { get; set; } = HttpStatusCode.OK;

    public string ItemsJson { get; set; } = "[]";

    public Func<HttpRequestMessage, Exception?>? Throw { get; set; }

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
        {
            headers[h.Key] = string.Join(",", h.Value);
        }

        var uri = request.RequestUri!.ToString();
        lock (_lock)
        {
            _requests.Add(new CapturedRequest(request.Method, uri, headers, body));
        }

        if (Throw?.Invoke(request) is { } error)
        {
            throw error;
        }

        var path = request.RequestUri.AbsolutePath;
        if (request.Method == HttpMethod.Post && path.EndsWith("/abort", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, RunBody("ABORTING"));
        }

        if (request.Method == HttpMethod.Post && path.Contains("/acts/", StringComparison.Ordinal))
        {
            return Json(StartStatusCode, StartBodyOverride ?? RunBody(RunStatusOnStart));
        }

        if (path.Contains("/actor-runs/", StringComparison.Ordinal))
        {
            string status;
            lock (_lock)
            {
                status = _pollStatuses.Count > 0 ? _pollStatuses.Dequeue() : PollForever ? "RUNNING" : "SUCCEEDED";
            }

            if (PollStatusCode != HttpStatusCode.OK)
            {
                return Json(PollStatusCode, "{}");
            }

            return Json(HttpStatusCode.OK, RunBody(status));
        }

        if (path.Contains("/datasets/", StringComparison.Ordinal))
        {
            return Json(ItemsStatusCode, ItemsJson);
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static string RunBody(string status) =>
        $"{{\"data\":{{\"id\":\"run-qa\",\"status\":\"{status}\",\"defaultDatasetId\":\"ds-qa\"}}}}";

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed record LogEntry(string Category, LogLevel Level, string Text);

internal sealed class LogSink : ILoggerProvider
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    private sealed class SinkLogger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            // Structured values too (e.g. {Source}), plus the whole exception chain.
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
            }

            if (exception is not null)
            {
                text += " | " + exception;
            }

            sink.Add(new LogEntry(category, logLevel, text));
        }
    }
}

internal sealed class QaSettings
{
    public string? Token { get; set; } = "qa-token-" + Guid.NewGuid().ToString("N");

    public string? ActorId { get; set; } = "qa-user/qa-actor";

    public string? InputJson { get; set; } = "{\"boardToken\":\"qa\"}";

    public int RunTimeoutSeconds { get; set; } = 300;

    public int MaxItems { get; set; } = 500;

    public string? BaseUrl { get; set; }

    public Dictionary<string, string> Fields { get; } = new()
    {
        ["ExternalId"] = "id",
        ["Company"] = "company",
        ["Title"] = "title",
        ["Description"] = "description",
        ["ApplyUrl"] = "url",
        ["Location"] = "location",
        ["Remote"] = "remote",
        ["PostedAt"] = "postedAt",
    };
}

/// <summary>
/// The real API on its own throwaway database inside the shared Testcontainers server, with the real
/// ApifyJobSource talking to a <see cref="FakeApify"/>. The fetch status is global, so each test gets its own database.
/// </summary>
internal sealed class QaHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _adminConnection;

    private QaHost(
        WebApplicationFactory<Program> factory,
        FakeApify apify,
        LogSink logs,
        QaSettings settings,
        string connectionString,
        string adminConnection)
    {
        _factory = factory;
        Apify = apify;
        Logs = logs;
        Settings = settings;
        ConnectionString = connectionString;
        _adminConnection = adminConnection;
        Client = factory.CreateClient();
    }

    public FakeApify Apify { get; }

    public LogSink Logs { get; }

    public QaSettings Settings { get; }

    public string ConnectionString { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _factory.Services;

    public static QaHost Create(ApiFactory root, Action<QaSettings>? configure = null, bool realApify = true)
    {
        var settings = new QaSettings();
        configure?.Invoke(settings);

        var baseCs = root.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:JobsFinder"]!;
        var cs = new NpgsqlConnectionStringBuilder(baseCs) { Database = "qa_jf7_" + Guid.NewGuid().ToString("N") };
        var admin = new NpgsqlConnectionStringBuilder(baseCs) { Database = "postgres" }.ConnectionString;

        var apify = new FakeApify();
        var logs = new LogSink();

        var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:JobsFinder", cs.ConnectionString);
            builder.UseSetting("Apify:Token", settings.Token ?? string.Empty);
            builder.UseSetting("Apify:ActorId", settings.ActorId ?? string.Empty);
            builder.UseSetting("Apify:InputJson", settings.InputJson ?? string.Empty);
            builder.UseSetting("Apify:RunTimeoutSeconds", settings.RunTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Apify:MaxItems", settings.MaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (settings.BaseUrl is not null)
            {
                builder.UseSetting("Apify:BaseUrl", settings.BaseUrl);
            }

            foreach (var (key, value) in settings.Fields)
            {
                builder.UseSetting($"Apify:Fields:{key}", value);
            }

            builder.ConfigureLogging(l =>
            {
                l.SetMinimumLevel(LogLevel.Trace);
                l.AddProvider(logs);
                l.AddFilter<LogSink>(null, LogLevel.Trace);
            });

            if (realApify)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IJobSource>();
                    services.AddHttpClient<IJobSource, ApifyJobSource>()
                        .ConfigurePrimaryHttpMessageHandler(() => apify);
                });
            }
        });

        return new QaHost(factory, apify, logs, settings, cs.ConnectionString, admin);
    }

    public async Task<Result<FetchRunDto>> FetchAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FetchJobPostingsHandler>().HandleAsync(cancellationToken);
    }

    public async Task<List<JobPosting>> PostingsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobPostings.AsNoTracking().OrderBy(p => p.ExternalId).ToListAsync();
    }

    public async Task<List<FetchRun>> RunsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FetchRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
    }

    public async Task<string> ScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var name = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
        try
        {
            await using var conn = new NpgsqlConnection(_adminConnection);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // Throwaway container; nothing to clean up if the drop fails.
        }
    }
}

/// <summary>Builds Apify dataset items with the default QA field mapping. A null argument omits the property.</summary>
internal static class Items
{
    public static string Make(
        string id,
        string? company = "Acme",
        string? title = "",
        string? description = "",
        string? url = "https://boards.greenhouse.io/acme/jobs/1",
        string? location = "Berlin",
        JsonNode? remote = null,
        JsonNode? postedAt = null)
    {
        var item = new JsonObject();
        Add(item, "id", id);
        Add(item, "company", company);
        Add(item, "title", title == string.Empty ? $"Engineer {id}" : title);
        Add(item, "description", description == string.Empty ? $"Build things for {id}" : description);
        Add(item, "url", url);
        Add(item, "location", location);
        if (remote is not null)
        {
            item["remote"] = remote;
        }

        if (postedAt is not null)
        {
            item["postedAt"] = postedAt;
        }

        return item.ToJsonString();
    }

    public static string Array(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

    public static string Array(params string[] items) => Array((IEnumerable<string>)items);

    private static void Add(JsonObject item, string name, string? value)
    {
        if (value is not null)
        {
            item[name] = value;
        }
    }
}
