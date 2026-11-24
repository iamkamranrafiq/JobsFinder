using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Npgsql;

namespace JobsFinder.IntegrationTests.Sourcing;

/// <summary>
/// The API on its own throwaway database, with the real Apify adapter talking to a scripted Apify. The running fetch
/// and the latest run are global, so every trigger test gets its own host. No real token, actor or network is used.
/// </summary>
internal sealed class FetchHost : IAsyncDisposable
{
    public const string FakeToken = "fake-token-for-tests";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _connectionString;

    private FetchHost(WebApplicationFactory<Program> factory, ScriptedApify apify, string connectionString)
    {
        _factory = factory;
        _connectionString = connectionString;
        Apify = apify;
        Client = factory.CreateClient();
    }

    public ScriptedApify Apify { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _factory.Services;

    /// <param name="root">Shared factory; provides the PostgreSQL server.</param>
    /// <param name="source">Replaces the Apify adapter when set.</param>
    /// <param name="settings">Overrides the settings of a configured source.</param>
    public static FetchHost Create(ApiFactory root, IJobSource? source = null, params (string Key, string Value)[] settings)
    {
        var baseConnection = root.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:JobsFinder"]!;
        var connection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = $"fetch_{Guid.NewGuid():N}" }.ConnectionString;
        var apify = new ScriptedApify();

        var factory = root.WithWebHostBuilder(builder =>
        {
            (string Key, string Value)[] defaults =
            [
                ("ConnectionStrings:JobsFinder", connection),
                ("Apify:Token", FakeToken),
                ("Apify:ActorId", "someone/some-actor"),
                ("Apify:InputJson", """{"boardToken":"acme"}"""),
                ("Apify:Fields:ExternalId", "id"),
                ("Apify:Fields:Company", "company"),
                ("Apify:Fields:Title", "title"),
                ("Apify:Fields:Description", "content"),
                ("Apify:Fields:ApplyUrl", "url"),
                ("Apify:Fields:Location", "location"),
                ("Apify:Fields:Remote", "remote"),
                ("Apify:Fields:PostedAt", "updated"),
            ];
            foreach (var (key, value) in defaults.Concat(settings))
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IJobSource>();
                if (source is null)
                {
                    services.AddHttpClient<IJobSource, ApifyJobSource>().ConfigurePrimaryHttpMessageHandler(() => apify);
                }
                else
                {
                    services.AddSingleton(source);
                }
            });
        });

        return new FetchHost(factory, apify, connection);
    }

    public Task<HttpResponseMessage> PostFetchAsync() => Client.PostAsJsonAsync("/api/sourcing/fetches", new { });

    public async Task<SourceStatusDto> StatusAsync() =>
        (await Client.GetFromJsonAsync<SourceStatusDto>("/api/sourcing/status", Json))!;

    public async Task<PagedResult<JobPostingDto>> PostingsAsync(string query = "") =>
        (await Client.GetFromJsonAsync<PagedResult<JobPostingDto>>($"/api/sourcing/postings{query}", Json))!;

    /// <summary>Triggers a fetch over HTTP and polls the status, as the page does, until it has ended.</summary>
    public async Task<FetchRunDto> FetchToEndAsync()
    {
        var response = await PostFetchAsync();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var started = (await response.Content.ReadFromJsonAsync<SourceStatusDto>(Json))!;

        var status = await WaitUntilIdleAsync();
        Assert.Equal(started.CurrentFetchId, status.LastRun!.Id);
        return status.LastRun;
    }

    public async Task<SourceStatusDto> WaitUntilIdleAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var status = await StatusAsync();
            if (status.CurrentFetchId is null)
            {
                return status;
            }

            await Task.Delay(25, timeout.Token);
        }
    }

    /// <summary>Uses its own context, so it also works after the host has stopped.</summary>
    public async Task<List<FetchRun>> RunsAsync()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);
        return await db.FetchRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();

        // Only this database's pool: other hosts may be running.
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        builder.Database = "postgres";
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>A scripted Apify API v2: start, poll, dataset and abort. Runs wait in a poll while held.</summary>
internal sealed class ScriptedApify : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly List<string> _requests = [];
    private TaskCompletionSource _release = Released();

    public HttpStatusCode StartStatus { get; set; } = HttpStatusCode.Created;

    public string ItemsJson { get; set; } = "[]";

    /// <summary>Method and path of every request.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public int StartedRuns => Requests.Count(r => r.StartsWith("POST /v2/acts/", StringComparison.Ordinal));

    public int AbortedRuns => Requests.Count(r => r.EndsWith("/abort", StringComparison.Ordinal));

    public void Hold() => _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _release.TrySetResult();

    public async Task WaitForPollAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!Requests.Any(r => r.StartsWith("GET /v2/actor-runs/", StringComparison.Ordinal)))
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        lock (_lock)
        {
            _requests.Add($"{request.Method} {path}");
        }

        if (path.EndsWith("/abort", StringComparison.Ordinal))
        {
            return Response(HttpStatusCode.OK, Run("ABORTING"));
        }

        if (request.Method == HttpMethod.Post)
        {
            return Response(StartStatus, Run("RUNNING"));
        }

        if (path.Contains("/actor-runs/", StringComparison.Ordinal))
        {
            await _release.Task.WaitAsync(cancellationToken);
            return Response(HttpStatusCode.OK, Run("SUCCEEDED"));
        }

        return Response(HttpStatusCode.OK, ItemsJson);
    }

    private static TaskCompletionSource Released()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }

    private static string Run(string status) =>
        $$$"""{"data":{"id":"run1","status":"{{{status}}}","defaultDatasetId":"ds1"}}""";

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
