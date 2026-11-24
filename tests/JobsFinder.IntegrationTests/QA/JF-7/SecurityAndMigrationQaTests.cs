using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace JobsFinder.IntegrationTests.QA.JF7;

[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed partial class SecurityAndMigrationQaTests(ApiFactory factory)
{
    private const string Token = "qa-Tok3n-SENTINEL-";

    private static QaSettings WithSentinel(QaSettings s)
    {
        s.Token = Token + Guid.NewGuid().ToString("N");
        return s;
    }

    // ---- BR1: the token ---------------------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-BR1")]
    public async Task TC29_Token_OnlyInAuthorizationHeader_NeverInUrlOrBody()
    {
        await using var host = QaHost.Create(factory, s => WithSentinel(s));
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.QueuePoll("RUNNING", "SUCCEEDED");
        host.Apify.ItemsJson = Items.Array(Items.Make("tok-1"));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess, run.Error?.Message);
        var token = host.Settings.Token!;
        Assert.True(host.Apify.Requests.Count >= 3);
        foreach (var r in host.Apify.Requests)
        {
            Assert.DoesNotContain(token, r.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain(Uri.EscapeDataString(token), r.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain(token, r.Body, StringComparison.Ordinal);
            Assert.Equal($"Bearer {token}", r.Headers["Authorization"]);
            Assert.DoesNotContain(r.Headers, h => h.Key != "Authorization" && h.Value.Contains(token, StringComparison.Ordinal));
            Assert.StartsWith("https://", r.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain("token=", r.Uri, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [Trait("REQ", "F0.2-BR1")]
    [InlineData("success")]
    [InlineData("unauthorized")]
    [InlineData("server-error")]
    [InlineData("network-error")]
    [InlineData("timeout-request")]
    [InlineData("actor-failed")]
    [InlineData("malformed-token-space")]
    [InlineData("malformed-token-unicode")]
    [InlineData("timed-out-run")]
    public async Task TC30_Token_NeverInLogsOrErrors(string scenario)
    {
        await using var host = QaHost.Create(factory, s =>
        {
            WithSentinel(s);
            if (scenario == "malformed-token-space")
            {
                s.Token = $"{Token}with space {Guid.NewGuid():N}";
            }
            else if (scenario == "malformed-token-unicode")
            {
                s.Token = $"{Token}tök€{Guid.NewGuid():N}";
            }

            if (scenario == "timed-out-run")
            {
                s.RunTimeoutSeconds = 1;
            }
        });
        host.Apify.ItemsJson = Items.Array(Items.Make("log-1"));
        switch (scenario)
        {
            case "unauthorized": host.Apify.StartStatusCode = HttpStatusCode.Unauthorized; break;
            case "server-error": host.Apify.StartStatusCode = HttpStatusCode.InternalServerError; break;
            case "network-error": host.Apify.Throw = r => new HttpRequestException($"No connection could be made ({r.RequestUri})"); break;
            case "timeout-request": host.Apify.Throw = _ => new TaskCanceledException("HttpClient.Timeout elapsed"); break;
            case "actor-failed": host.Apify.RunStatusOnStart = "FAILED"; break;
            case "timed-out-run":
                host.Apify.RunStatusOnStart = "RUNNING";
                host.Apify.PollForever = true;
                break;
        }

        var run = await host.FetchAsync();

        var secret = host.Settings.Token!;
        Assert.NotEmpty(host.Logs.Entries);
        Assert.DoesNotContain(host.Logs.Entries, e => e.Text.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(host.Logs.Entries, e => e.Text.Contains(Token, StringComparison.Ordinal));
        if (!run.IsSuccess)
        {
            Assert.DoesNotContain(secret, run.Error!.Message, StringComparison.Ordinal);
        }

        var status = await host.Client.GetStringAsync("/api/sourcing/status");
        Assert.DoesNotContain(Token, status, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR1")]
    public async Task TC31_Token_NeverInTheDatabase()
    {
        await using var host = QaHost.Create(factory, s => WithSentinel(s));
        host.Apify.ItemsJson = Items.Array(Items.Make("db-1"), Items.Make("db-2"));
        await host.FetchAsync();
        host.Apify.StartStatusCode = HttpStatusCode.Unauthorized;
        await host.FetchAsync();

        var runs = await host.ScalarAsync("select coalesce(string_agg(t::text, E'\\n'), '') from job_fetch_runs t");
        var postings = await host.ScalarAsync("select coalesce(string_agg(t::text, E'\\n'), '') from job_postings t");

        Assert.NotEmpty(runs);
        Assert.NotEmpty(postings);
        Assert.DoesNotContain(Token, runs, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, postings, StringComparison.Ordinal);
        // Any text column anywhere in the database (jobs, cvs, history ...).
        var columns = (await host.ScalarAsync(
            "select coalesce(string_agg(c.table_name || '.' || c.column_name, ',') filter (where true), '') " +
            "from information_schema.columns c where c.table_schema='public' and c.data_type in ('text','character varying','character') " +
            "and c.table_name in (select table_name from information_schema.tables where table_schema='public' and table_type='BASE TABLE')"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(columns);
        foreach (var column in columns)
        {
            var (table, name) = (column.Split('.')[0], column.Split('.')[1]);
            Assert.Equal("0", await host.ScalarAsync($"select count(*) from public.\"{table}\" where \"{name}\"::text like '%{Token}%'"));
        }
    }

    [Fact]
    [Trait("REQ", "F0.2-BR1")]
    public void TC32_NoTokenInCommittedFiles()
    {
        var root = RepoRoot();
        var tracked = Git(root, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(tracked);

        // Never read secret stores: only report whether such files are tracked at all.
        var envFiles = tracked.Where(f => Path.GetFileName(f).StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(f).EndsWith(".example", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(envFiles);
        Assert.DoesNotContain(tracked, f => f.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase));

        var pattern = TokenLike();
        var textual = new[] { ".json", ".yml", ".yaml", ".cs", ".ts", ".md", ".props", ".csproj", ".config", ".sh", ".conf", ".html", ".txt", ".slnx" };
        foreach (var file in tracked.Where(f => textual.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path) || new FileInfo(path).Length > 2_000_000)
            {
                continue;
            }

            var text = File.ReadAllText(path);
            Assert.False(pattern.IsMatch(text), $"{file} looks like it holds an Apify token");
        }

        foreach (var settings in tracked.Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, settings)), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty("Apify", out var apify) && apify.TryGetProperty("Token", out var token))
            {
                Assert.True(token.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(token.GetString()), $"{settings} sets Apify:Token");
            }
        }

        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        Assert.DoesNotMatch(@"(?im)^\s*-?\s*Apify__Token\s*[:=]\s*\S+", compose);
    }

    // ---- BR4: no cookies / user credentials -------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-BR4")]
    public void TC33_ProductionHttpClient_HasCookiesOff_AndHttpsBase()
    {
        // The root factory still carries the production named-client registration (only the IJobSource service is faked).
        var handlerFactory = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var handler = handlerFactory.CreateHandler(nameof(JobsFinder.Application.Sourcing.IJobSource));
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.UseCookies);

        using var client = factory.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(JobsFinder.Application.Sourcing.IJobSource));
        Assert.Equal("https", client.BaseAddress!.Scheme);
        Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
        Assert.DoesNotContain(client.DefaultRequestHeaders, h => h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("REQ", "F0.2-BR4")]
    public async Task TC33b_RequestsCarryNoCookieOrOtherCredentialHeaders()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.QueuePoll("SUCCEEDED");
        host.Apify.ItemsJson = Items.Array(Items.Make("c-1"));

        await host.FetchAsync();

        Assert.NotEmpty(host.Apify.Requests);
        var allowed = new[] { "Authorization", "Content-Type", "Content-Length", "Accept", "Accept-Encoding", "User-Agent", "Host", "Traceparent", "Request-Id" };
        foreach (var r in host.Apify.Requests)
        {
            Assert.DoesNotContain(r.Headers.Keys, k => k.Contains("cookie", StringComparison.OrdinalIgnoreCase));
            Assert.All(r.Headers.Keys, k => Assert.Contains(k, allowed, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [Trait("REQ", "F0.2-BR4")]
    [InlineData("{\"cookies\":[{\"name\":\"li_at\",\"value\":\"x\"}]}")]
    [InlineData("{\"Cookie\":\"a=b\"}")]
    [InlineData("{\"li_at\":\"abc\"}")]
    [InlineData("{\"password\":\"hunter2\"}")]
    [InlineData("{\"sessionCookie\":\"abc\"}")]
    [InlineData("{\"username\":\"me\",\"boardToken\":\"acme\"}")]
    [InlineData("{\"login\":\"me\"}")]
    [InlineData("{\"nested\":{\"deep\":[{\"proxyPassword\":\"p\"}]}}")]
    [InlineData("{\"extraHeaders\":{\"Authorization\":\"Basic abc\"}}")]
    [InlineData("{\"startUrls\":[{\"url\":\"https://x.example\",\"headers\":{\"Set-Cookie\":\"a=b\"}}]}")]
    public async Task TC34_InputWithCredentialsOrCookies_Refused_NothingSent(string input)
    {
        await using var host = QaHost.Create(factory, s => s.InputJson = input);
        host.Apify.ItemsJson = Items.Array(Items.Make("never"));

        var run = await host.FetchAsync();

        Assert.False(run.IsSuccess);
        Assert.Empty(host.Apify.Requests);
        Assert.Equal(FetchFailureReason.NotConfigured, Assert.Single(await host.RunsAsync()).FailureReason);
    }

    [Theory]
    [Trait("REQ", "F0.2-BR4")]
    [InlineData("{\"sessionPoolName\":\"pool\",\"boardToken\":\"acme\",\"maxItems\":10}")]
    [InlineData("{}")]
    public async Task TC34b_OrdinaryInput_IsSentUnchanged(string input)
    {
        await using var host = QaHost.Create(factory, s => s.InputJson = input);
        host.Apify.ItemsJson = Items.Array(Items.Make("ok-1"));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess, run.Error?.Message);
        var start = host.Apify.Requests.First(r => r.Method == HttpMethod.Post);
        Assert.Equal(input, start.Body);
        Assert.Contains("/v2/acts/qa-user~qa-actor/runs", start.Uri, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR4")]
    public async Task TC34c_CredentialsHiddenInHeaderNameValuePairs_AreRefused()
    {
        await using var host = QaHost.Create(factory, s => s.InputJson = "{\"headers\":[{\"name\":\"Cookie\",\"value\":\"sid=abc\"}]}");
        host.Apify.ItemsJson = Items.Array(Items.Make("never"));

        var run = await host.FetchAsync();

        Assert.False(run.IsSuccess);
        Assert.Empty(host.Apify.Requests);
    }

    // ---- Migration ---------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC41_Migration_DownThenUp_OnThrowawayDatabase()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("mig-1"), Items.Make("mig-2"));
        await host.FetchAsync();

        const string columnsSql =
            "select coalesce(string_agg(table_name||'.'||column_name||':'||data_type||':'||is_nullable||':'||coalesce(character_maximum_length::text,''), ',' order by table_name, column_name), '') " +
            "from information_schema.columns where table_schema='public' and table_name in ('job_postings','job_fetch_runs')";
        const string otherTablesSql =
            "select coalesce(string_agg(table_name, ',' order by table_name), '') from information_schema.tables where table_schema='public' and table_name not in ('job_postings','job_fetch_runs')";
        const string indexesSql =
            "select coalesce(string_agg(indexdef, ';' order by indexname), '') from pg_indexes where schemaname='public' and tablename in ('job_postings','job_fetch_runs')";

        var columnsBefore = await host.ScalarAsync(columnsSql);
        var indexesBefore = await host.ScalarAsync(indexesSql);
        var otherBefore = await host.ScalarAsync(otherTablesSql);
        Assert.Equal("2", await host.ScalarAsync("select count(*) from job_postings"));
        Assert.Contains("jobs", otherBefore, StringComparison.Ordinal);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var all = db.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddJobPostingsAndFetchRuns", StringComparison.Ordinal));
            Assert.True(index > 0, "migration AddJobPostingsAndFetchRuns not found after an earlier one");
            Assert.Equal(all.Count - 1, index);

            await db.GetService<IMigrator>().MigrateAsync(all[index - 1]);

            Assert.Equal(string.Empty, await host.ScalarAsync(columnsSql));
            Assert.Equal(string.Empty, await host.ScalarAsync(indexesSql));
            Assert.Equal(otherBefore, await host.ScalarAsync(otherTablesSql));
            Assert.DoesNotContain(all[index], (await db.Database.GetAppliedMigrationsAsync()).ToList());
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        Assert.Equal(columnsBefore, await host.ScalarAsync(columnsSql));
        Assert.Equal(indexesBefore, await host.ScalarAsync(indexesSql));
        Assert.Equal(otherBefore, await host.ScalarAsync(otherTablesSql));
        Assert.Contains("UNIQUE INDEX \"IX_job_postings_ContentHash\"", indexesBefore, StringComparison.Ordinal);
        Assert.Contains("UNIQUE INDEX \"IX_job_postings_Source_ExternalId\"", indexesBefore, StringComparison.Ordinal);
        Assert.Equal("0", await host.ScalarAsync("select count(*) from job_postings"));

        // Usable again after Up.
        var run = await host.FetchAsync();
        Assert.True(run.IsSuccess, run.Error?.Message);
        Assert.Equal(2, run.Value!.InsertedCount);
        Assert.Equal(ApplyPath.AtsUrl, (await host.PostingsAsync())[0].ApplyPath);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobsFinder.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Git(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }

    [GeneratedRegex("apify_api_[A-Za-z0-9]{16,}")]
    private static partial Regex TokenLike();
}
