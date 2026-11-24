using System.Net;
using System.Text.Json;
using JobsFinder.Domain.Sourcing;
using JobsFinder.IntegrationTests.QA.JF7;
using JobsFinder.Infrastructure.Sourcing;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.IntegrationTests.QA.JF8;

/// <summary>Failures through the trigger, the freed slot, paging bounds, ordering, safety of what is listed, shutdown.</summary>
[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class FailuresPagingAndSafetyQaTests(ApiFactory factory)
{
    private static string Id() => Guid.NewGuid().ToString("N")[..10];

    // ---- Failures and the freed slot -------------------------------------------------------------------------

    public static TheoryData<string> Failures => new()
    {
        "not-configured",
        "unauthorized",
        "actor-failed",
        "timed-out",
        "items-not-array",
        "apify-500",
        "relative-base-url",
    };

    private static QaHost CreateFailing(ApiFactory factory, string scenario)
    {
        var host = QaHost.Create(factory, settings =>
        {
            switch (scenario)
            {
                case "not-configured": settings.Token = null; break;
                case "relative-base-url": settings.BaseUrl = "relative/path"; break;
            }
        });
        switch (scenario)
        {
            case "unauthorized": host.Apify.StartStatusCode = HttpStatusCode.Unauthorized; break;
            case "actor-failed": host.Apify.RunStatusOnStart = "FAILED"; break;
            case "timed-out": host.Apify.RunStatusOnStart = "TIMED-OUT"; break;
            case "items-not-array": host.Apify.ItemsJson = "{\"not\":\"an array\"}"; break;
            case "apify-500": host.Apify.StartStatusCode = HttpStatusCode.InternalServerError; break;
        }

        return host;
    }

    [Theory]
    [MemberData(nameof(Failures))]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC10_AfterEveryKindOfFailure_TheSlotIsFree_AndTheFailureIsStored(string scenario)
    {
        await using var host = CreateFailing(factory, scenario);

        var first = await host.FetchAndWaitAsync();
        Assert.Equal("Unavailable", first.GetProperty("state").GetString());
        Assert.Equal("Failed", first.GetProperty("lastRun").GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("lastRun").GetProperty("failureReason").ValueKind);
        Assert.Empty(await host.PostingsAsync());

        // The slot must be free: a new trigger is accepted, not refused with 409.
        var second = await host.FetchAndWaitAsync();
        Assert.Equal("Unavailable", second.GetProperty("state").GetString());
        Assert.Equal(2, (await host.RunsAsync()).Count);
        Assert.All(await host.RunsAsync(), r => Assert.Equal(FetchRunStatus.Failed, r.Status));
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC08_NoToken_FetchFailsAsNotConfigured_WithoutAnyCallToApify()
    {
        await using var host = QaHost.Create(factory, s => s.Token = null);

        var status = await host.FetchAndWaitAsync();

        Assert.Empty(host.Apify.Requests);
        Assert.Equal("Unavailable", status.GetProperty("state").GetString());
        Assert.Equal("NotConfigured", status.GetProperty("lastRun").GetProperty("failureReason").GetString());
        Assert.Empty((await host.ListAsync()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC09_ARejectedTokenAfterAGoodFetch_LeavesTheStoredPostingsListed()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()), Items.Make(Id()), Items.Make(Id()));
        await host.FetchAndWaitAsync();
        var before = (await host.ListAsync()).Titles();

        host.Apify.StartStatusCode = HttpStatusCode.Unauthorized;
        var status = await host.FetchAndWaitAsync();

        Assert.Equal("Unavailable", status.GetProperty("state").GetString());
        Assert.Equal("Unauthorized", status.GetProperty("lastRun").GetProperty("failureReason").GetString());
        Assert.Equal(before, (await host.ListAsync()).Titles());

        host.Apify.StartStatusCode = HttpStatusCode.Created;
        var recovered = await host.FetchAndWaitAsync();
        Assert.Equal("Available", recovered.GetProperty("state").GetString());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC10b_UnexpectedError_IsStoredAsUnexpected_AndLoggedWithoutAHttpFailure()
    {
        await using var host = QaHost.Create(factory, s => s.BaseUrl = "relative/path");

        var response = await host.PostFetchAsync();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var status = await host.WaitIdleAsync();

        Assert.Equal("Unavailable", status.GetProperty("state").GetString());
        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal(FetchRunStatus.Failed, run.Status);
        Assert.Contains(host.Logs.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
    }

    // ---- Token never leaks -----------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-BR1")]
    public async Task TC19_TheTokenAppearsInNoResponseAndNoLog_NotEvenOnFailures()
    {
        await using var host = QaHost.Create(factory);
        var token = host.Settings.Token!;
        var seen = new List<string>();

        async Task CollectAsync(HttpResponseMessage response)
        {
            seen.Add(response.ToString());
            seen.Add(await response.Content.ReadAsStringAsync());
        }

        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        await CollectAsync(await host.PostFetchAsync());
        await host.WaitIdleAsync();

        foreach (var failure in new[] { "unauthorized", "actor-failed", "timed-out", "apify-500" })
        {
            host.Apify.StartStatusCode = HttpStatusCode.Created;
            host.Apify.RunStatusOnStart = "SUCCEEDED";
            switch (failure)
            {
                case "unauthorized": host.Apify.StartStatusCode = HttpStatusCode.Unauthorized; break;
                case "actor-failed": host.Apify.RunStatusOnStart = "FAILED"; break;
                case "timed-out": host.Apify.RunStatusOnStart = "TIMED-OUT"; break;
                case "apify-500": host.Apify.StartStatusCode = HttpStatusCode.InternalServerError; break;
            }

            await CollectAsync(await host.PostFetchAsync());
            await host.WaitIdleAsync();
        }

        await CollectAsync(await host.Client.GetAsync("/api/sourcing/status"));
        await CollectAsync(await host.Client.GetAsync("/api/sourcing/postings"));
        await CollectAsync(await host.Client.GetAsync("/api/sourcing/postings?page=0"));
        await CollectAsync(await host.Client.GetAsync("/openapi/v1.json"));

        // The token is sent to Apify (that is its purpose) but only in the Authorization header.
        Assert.All(host.Apify.Requests, r =>
        {
            Assert.DoesNotContain(token, r.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain(token, r.Body, StringComparison.Ordinal);
        });
        Assert.All(seen, text => Assert.DoesNotContain(token, text, StringComparison.Ordinal));
        Assert.All(host.Logs.Entries, e => Assert.DoesNotContain(token, e.Text, StringComparison.Ordinal));
    }

    // ---- Paging ----------------------------------------------------------------------------------------------

    public static TheoryData<string> BadPaging => new()
    {
        "?page=0",
        "?page=-1",
        "?page=abc",
        "?page=1.5",
        "?page=",
        "?pageSize=0",
        "?pageSize=-5",
        "?pageSize=101",
        "?pageSize=1000000",
        "?pageSize=abc",
        "?page=100001",
        "?page=2147483647",
        "?page=2147483648",
        "?page=99999999999999999999",
        "?pageSize=2147483647",
        "?page=100000&pageSize=2147483647",
        "?page=1&page=0",
    };

    [Theory]
    [MemberData(nameof(BadPaging))]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC16_BadPagingValues_AreRejectedWith400ProblemDetails_NeverA500(string query)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()), Items.Make(Id()));
        await host.FetchAndWaitAsync();

        var response = await host.Client.GetAsync("/api/sourcing/postings" + query);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{query} -> {(int)response.StatusCode}: {text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(text).RootElement;
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.DoesNotContain("items", text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?page=1&pageSize=1", 1)]
    [InlineData("?page=1&pageSize=100", 5)]
    [InlineData("?page=5&pageSize=1", 1)]
    [InlineData("?page=6&pageSize=1", 0)]
    [InlineData("?page=99999", 0)]
    [InlineData("?page=100000", 0)]
    [InlineData("?page=100000&pageSize=100", 0)]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC17_BoundaryPages_AreOk_AndTheTotalIsAlwaysCorrect(string query, int expectedItems)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Enumerable.Range(0, 5).Select(_ => Items.Make(Id())));
        await host.FetchAndWaitAsync();

        var list = await host.ListAsync(query);

        Assert.Equal(5, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(expectedItems, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC18_PostingsOfOneFetch_PageDeterministically_WithNothingRepeatedOrMissing()
    {
        await using var host = QaHost.Create(factory);
        var ids = Enumerable.Range(0, 13).Select(i => $"s{i:D2}{Id()}").ToList();
        host.Apify.ItemsJson = Items.Array(ids.Select(i => Items.Make(i)));
        await host.FetchAndWaitAsync();

        async Task<List<string>> WalkAsync()
        {
            var all = new List<string>();
            for (var page = 1; page <= 7; page++)
            {
                all.AddRange((await host.ListAsync($"?page={page}&pageSize=2")).Titles());
            }

            return all;
        }

        var first = await WalkAsync();
        var second = await WalkAsync();

        Assert.Equal(13, first.Count);
        Assert.Equal(13, first.Distinct().Count());
        Assert.Equal(first, second);
        Assert.Equal(ids.Select(i => $"Engineer {i}").Order(StringComparer.Ordinal), first.Order(StringComparer.Ordinal));
        var created = (await host.ListAsync("?pageSize=100")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(created.OrderByDescending(c => c), created);
    }

    // ---- What is listed is safe ------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC20_HtmlInTitleOrCompany_IsReturnedAsPlainText()
    {
        await using var host = QaHost.Create(factory);
        const string title = "<img src=x onerror=alert(1)>Dev";
        const string company = "<b>Evil</b> & Co";
        host.Apify.ItemsJson = Items.Array(Items.Make(Id(), company: company, title: title, location: "<script>alert(2)</script>"));

        await host.FetchAndWaitAsync();

        var item = Assert.Single((await host.ListAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(title, item.GetProperty("title").GetString());
        Assert.Equal(company, item.GetProperty("company").GetString());
        Assert.Equal("<script>alert(2)</script>", item.GetProperty("location").GetString());
        var response = await host.Client.GetAsync("/api/sourcing/postings");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/job")]
    [InlineData("//example.com/job")]
    [InlineData("/relative/job")]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC21_UnsafeApplyUrls_NeverReachTheList(string url)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id(), url: url), Items.Make(Id(), url: "https://example.com/ok"));

        await host.FetchAndWaitAsync();

        foreach (var item in (await host.ListAsync()).GetProperty("items").EnumerateArray())
        {
            var applyUrl = item.GetProperty("applyUrl");
            if (applyUrl.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var uri = new Uri(applyUrl.GetString()!, UriKind.Absolute);
            Assert.True(uri.Scheme is "http" or "https" or "mailto", applyUrl.GetString());
        }
    }

    // ---- Shutdown and abort ----------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC22_GracefulStopMidFetch_StoresAnInterruptedRun_AbortsTheApifyRun_AndStoresNoPostings()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;
        var id = await host.StartAsync();
        await WaitForAsync(() => host.RunStarts() == 1);

        await host.Services.GetRequiredService<FetchWorker>().StopAsync(CancellationToken.None);

        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal(id, run.Id);
        Assert.Equal(FetchRunStatus.Failed, run.Status);
        Assert.Equal(FetchFailureReason.Interrupted, run.FailureReason);
        Assert.Equal(1, host.Aborts());
        Assert.Empty(await host.PostingsAsync());
        var status = await host.StatusAsync();
        Assert.Equal(JsonValueKind.Null, status.GetProperty("currentFetchId").ValueKind);
        Assert.Equal("Unavailable", status.GetProperty("state").GetString());
        Assert.Equal("Interrupted", status.GetProperty("lastRun").GetProperty("failureReason").GetString());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC27_AFailedStatusPoll_AbortsTheRunAtApify_AndStoresNothing()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollStatusCode = HttpStatusCode.InternalServerError;

        var status = await host.FetchAndWaitAsync();

        Assert.Equal("Unavailable", status.GetProperty("state").GetString());
        Assert.Equal("Unreachable", status.GetProperty("lastRun").GetProperty("failureReason").GetString());
        Assert.Equal(1, host.RunStarts());
        Assert.Equal(1, host.Aborts());
        Assert.Empty(await host.PostingsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC27b_ACancelledCaller_AbortsTheRunAtApify_AndStoresNothing()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;
        using var cts = new CancellationTokenSource();

        var fetch = host.FetchAsync(cts.Token);
        await WaitForAsync(() => host.RunStarts() == 1);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);

        Assert.Equal(1, host.Aborts());
        Assert.Empty(await host.PostingsAsync());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "condition never became true");
            await Task.Delay(50);
        }
    }
}
