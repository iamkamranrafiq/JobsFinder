using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobsFinder.IntegrationTests.Sourcing;

/// <summary>F0.2-AC1 over HTTP: trigger, background fetch from a scripted Apify, PostgreSQL, list.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class StartFetchTests(ApiFactory factory)
{
    private static string Items(params string[] ids) =>
        "[" + string.Join(',', ids.Select(id =>
            $$"""
            {"id":"{{id}}","company":"Acme","title":"Engineer {{id}}","content":"<p>Build {{id}}</p>","location":"London",
             "remote":true,"url":"https://boards.greenhouse.io/acme/jobs/{{id}}?x=a%20b","updated":"2026-11-20T10:00:00Z"}
            """)) + "]";

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task PostFetch_ConfiguredSource_StoresAndListsNewPostings_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1", "2");
        Assert.Empty((await host.PostingsAsync()).Items);

        var run = await host.FetchToEndAsync();

        Assert.Equal((FetchRunStatus.Succeeded, 2), (run.Status, run.InsertedCount));
        var page = await host.PostingsAsync();
        Assert.Equal((2, 1, 20), (page.TotalCount, page.Page, page.PageSize));
        var first = Assert.Single(page.Items, p => p.Title == "Engineer 1");
        Assert.Equal(("apify", "Acme", "London", true), (first.Source, first.Company, first.Location, first.IsRemote));
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/1?x=a%20b", first.ApplyUrl!.OriginalString);
        Assert.Equal(ApplyPath.AtsUrl, first.ApplyPath);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), first.PostedAt);
        Assert.Equal(SourceState.Available, (await host.StatusAsync()).State);
    }

    [Fact]
    public async Task PostFetch_SecondFetch_ListsOnlyNewOnesOnTop_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1", "2");
        await host.FetchToEndAsync();
        host.Apify.ItemsJson = Items("1", "2", "3");

        var run = await host.FetchToEndAsync();

        Assert.Equal((1, 2), (run.InsertedCount, run.DuplicateCount));
        var page = await host.PostingsAsync();
        Assert.Equal("Engineer 3", page.Items[0].Title);
        Assert.Equal(["Engineer 1", "Engineer 2"], page.Items.Skip(1).Select(p => p.Title).Order());
    }

    [Fact]
    public async Task PostFetch_WhileRunning_Returns409AndStartsOneRun_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1");
        host.Apify.Hold();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => host.PostFetchAsync()));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        var refused = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Equal(5, refused.Count);
        Assert.Equal("fetch_in_progress", await CodeAsync(refused[0]));

        await host.Apify.WaitForPollAsync();
        var running = await host.StatusAsync();
        Assert.NotNull(running.CurrentFetchId);
        Assert.Equal((SourceState.NeverRun, null), (running.State, running.LastRun));

        host.Apify.Release();
        Assert.Equal(running.CurrentFetchId, (await host.WaitUntilIdleAsync()).LastRun!.Id);
        Assert.Equal(1, host.Apify.StartedRuns);
        Assert.Single(await host.RunsAsync());
    }

    [Fact]
    public async Task PostFetch_WhileRunning_StatusKeepsLastOutcome_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.StartStatus = HttpStatusCode.Unauthorized;
        var failed = await host.FetchToEndAsync();
        host.Apify.StartStatus = HttpStatusCode.Created;
        host.Apify.Hold();

        await host.PostFetchAsync();
        await host.Apify.WaitForPollAsync();

        var running = await host.StatusAsync();
        Assert.Equal((SourceState.Unavailable, failed.Id), (running.State, running.LastRun!.Id));
        host.Apify.Release();
        Assert.Equal(SourceState.Available, (await host.WaitUntilIdleAsync()).State);
    }

    [Fact]
    public async Task PostFetch_AfterFailureAndAfterSuccess_CanStartAgain_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1");
        host.Apify.StartStatus = HttpStatusCode.InternalServerError;
        Assert.Equal(FetchFailureReason.Unreachable, (await host.FetchToEndAsync()).FailureReason);
        host.Apify.StartStatus = HttpStatusCode.Created;

        Assert.Equal(1, (await host.FetchToEndAsync()).InsertedCount);
        Assert.Equal(1, (await host.FetchToEndAsync()).DuplicateCount);
    }

    [Fact]
    public async Task PostFetch_SourceFails_ListUnchanged_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1", "2");
        await host.FetchToEndAsync();
        var before = await host.PostingsAsync();
        host.Apify.StartStatus = HttpStatusCode.Unauthorized;

        var run = await host.FetchToEndAsync();

        Assert.Equal((FetchRunStatus.Failed, FetchFailureReason.Unauthorized), (run.Status, run.FailureReason));
        Assert.Equal(SourceState.Unavailable, (await host.StatusAsync()).State);
        Assert.Equal(before.Items, (await host.PostingsAsync()).Items);
    }

    [Fact]
    public async Task PostFetch_NotConfigured_FailsWithoutCallingApify_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory, settings: ("Apify:Token", string.Empty));

        var run = await host.FetchToEndAsync();

        Assert.Equal(FetchFailureReason.NotConfigured, run.FailureReason);
        Assert.Empty(host.Apify.Requests);
    }

    [Fact]
    public async Task PostFetch_UnexpectedError_RecordsFailedRunAndFreesTheSlot_F0_2_AC1()
    {
        var source = new BrokenSource();
        await using var host = FetchHost.Create(factory, source);

        var run = await host.FetchToEndAsync();

        Assert.Equal((FetchRunStatus.Failed, FetchFailureReason.Unexpected, "broken"), (run.Status, run.FailureReason, run.Source));
        source.Broken = false;
        Assert.Equal(FetchRunStatus.Succeeded, (await host.FetchToEndAsync()).Status);
    }

    /// <summary>JF-7 review N2: a relative BaseUrl stops the source from being created at all.</summary>
    [Fact]
    public async Task PostFetch_SourceCannotBeCreated_RecordsFailedRun_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory, settings: ("Apify:BaseUrl", "api.apify.com"));

        var run = await host.FetchToEndAsync();

        Assert.Equal((FetchFailureReason.Unexpected, "unknown"), (run.FailureReason, run.Source));
        Assert.Equal(SourceState.Unavailable, (await host.StatusAsync()).State);
    }

    [Fact]
    public async Task PostFetch_CallerGone_FetchStillCompletes_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1");
        host.Apify.Hold();
        using (var caller = host.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>() is Microsoft.AspNetCore.TestHost.TestServer server
            ? server.CreateClient()
            : throw new InvalidOperationException("Test server expected."))
        {
            (await caller.PostAsJsonAsync("/api/sourcing/fetches", new { })).Dispose();
            await host.Apify.WaitForPollAsync();
        }

        host.Apify.Release();

        Assert.Equal(1, (await host.WaitUntilIdleAsync()).LastRun!.InsertedCount);
    }

    [Theory]
    [InlineData("text/plain", "{}")]
    [InlineData("application/x-www-form-urlencoded", "a=b")]
    [InlineData(null, null)]
    public async Task PostFetch_NotJson_IsRefusedAndStartsNothing_F0_2_AC1(string? contentType, string? body)
    {
        await using var host = FetchHost.Create(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sourcing/fetches");
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType!);
        }

        var response = await host.Client.SendAsync(request);

        Assert.Equal(body is null ? HttpStatusCode.BadRequest : HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Null((await host.StatusAsync()).CurrentFetchId);
        Assert.Empty(host.Apify.Requests);
        Assert.Empty(await host.RunsAsync());
    }

    /// <summary>The JSON body forces this preflight, so another site cannot spend credit from a browser.</summary>
    [Theory]
    [InlineData("https://evil.example", false)]
    [InlineData("http://localhost:4200", true)]
    public async Task PostFetch_CrossOriginPreflight_OnlyAllowedForTheApp_F0_2_AC1(string origin, bool allowed)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/sourcing/fetches");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Shutdown_DuringFetch_AbortsRunAndRecordsInterrupted_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        host.Apify.ItemsJson = Items("1");
        host.Apify.Hold();
        var started = await (await host.PostFetchAsync()).Content.ReadFromJsonAsync<SourceStatusDto>(FetchHost.Json);
        await host.Apify.WaitForPollAsync();

        await host.Services.GetRequiredService<IHost>().StopAsync();

        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal(
            (started!.CurrentFetchId!.Value, FetchRunStatus.Failed, (FetchFailureReason?)FetchFailureReason.Interrupted),
            (run.Id, run.Status, run.FailureReason));
        Assert.Equal(1, host.Apify.AbortedRuns);
    }

    private sealed class BrokenSource : IJobSource
    {
        public string Name => "broken";

        public bool Broken { get; set; } = true;

        public Task<JobSourceResult> FetchAsync(CancellationToken cancellationToken) => Broken
            ? throw new InvalidOperationException("a bug")
            : Task.FromResult(new JobSourceResult([], 0));
    }
}
