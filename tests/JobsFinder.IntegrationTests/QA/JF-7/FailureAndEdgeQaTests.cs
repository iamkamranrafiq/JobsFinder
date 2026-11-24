using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobsFinder.Application.Common;
using JobsFinder.Domain.Sourcing;
using Microsoft.Extensions.Logging;

namespace JobsFinder.IntegrationTests.QA.JF7;

[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class FailureAndEdgeQaTests(ApiFactory factory)
{
    private static async Task<JsonElement> StatusAsync(QaHost host)
    {
        var response = await host.Client.GetAsync("/api/sourcing/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertFailedRunAsync(QaHost host, Result<JobsFinder.Application.Sourcing.FetchRunDto> result, FetchFailureReason reason)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal("source_unavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal(FetchRunStatus.Failed, run.Status);
        Assert.Equal(reason, run.FailureReason);
        Assert.Equal(0, run.InsertedCount);
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Error && e.Text.Contains(reason.ToString(), StringComparison.Ordinal));
        var status = await StatusAsync(host);
        Assert.Equal("Unavailable", status.GetProperty("state").GetString());
        Assert.Equal("Failed", status.GetProperty("lastRun").GetProperty("status").GetString());
        Assert.Equal(reason.ToString(), status.GetProperty("lastRun").GetProperty("failureReason").GetString());
    }

    // ---- Edge 1: malformed items -------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-Edge1")]
    public async Task TC18_MalformedItems_SkippedAndCounted_RunStillSucceeds()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(
            Items.Make("ok-1"),
            Items.Make("ok-2"),
            Items.Make("m-noid", company: "NoId").Replace("\"id\":\"m-noid\",", string.Empty, StringComparison.Ordinal),
            Items.Make("m-notitle", title: null),
            Items.Make("m-nocompany", company: null),
            Items.Make("m-nodesc", description: null),
            Items.Make("m-emptydesc", description: "   "),
            "\"just a string\"",
            "42",
            "null",
            "[1,2]",
            Items.Make("m-baddate", postedAt: "banana"));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess, run.Error?.Message);
        Assert.Equal(2, run.Value!.InsertedCount);
        Assert.Equal(10, run.Value.MalformedCount);
        Assert.Equal(0, run.Value.DuplicateCount);
        Assert.Equal(12, run.Value.FetchedCount);
        Assert.Equal(["ok-1", "ok-2"], (await host.PostingsAsync()).Select(p => p.ExternalId));
        Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("10", StringComparison.Ordinal) && e.Text.Contains("malformed", StringComparison.OrdinalIgnoreCase));

        var status = await StatusAsync(host);
        Assert.Equal("Available", status.GetProperty("state").GetString());
        Assert.Equal(10, status.GetProperty("lastRun").GetProperty("malformedCount").GetInt32());
    }

    [Fact]
    [Trait("REQ", "F0.2-Edge1")]
    public async Task TC19_EmptyDatasetAndOnlyMalformedItems_Succeed()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = "[]";
        var empty = await host.FetchAsync();
        Assert.True(empty.IsSuccess);
        Assert.Equal(0, empty.Value!.InsertedCount);

        host.Apify.ItemsJson = "[1,\"x\",{},null]";
        var bad = await host.FetchAsync();
        Assert.True(bad.IsSuccess);
        Assert.Equal(4, bad.Value!.MalformedCount);
        Assert.Empty(await host.PostingsAsync());
        Assert.Equal("Available", (await StatusAsync(host)).GetProperty("state").GetString());
    }

    [Theory]
    [Trait("REQ", "F0.2-Edge1")]
    [InlineData("{\"items\":[]}")]
    [InlineData("this is not json")]
    [InlineData("")]
    public async Task TC20_DatasetNotAnArrayOrNotJson_FailsAndStoresNothing(string body)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = body;

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.Unreachable);
        Assert.Empty(await host.PostingsAsync());
    }

    // ---- AC4: failures -------------------------------------------------------------------------------------------

    [Theory]
    [Trait("REQ", "F0.2-AC4")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TC21_MissingToken_FailsWithLoggedError_AndNoCallToApify(string? token)
    {
        await using var host = QaHost.Create(factory, s => s.Token = token);

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.NotConfigured);
        Assert.Empty(host.Apify.Requests);
    }

    [Theory]
    [Trait("REQ", "F0.2-AC4")]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TC22_InvalidToken_FailsAsUnauthorized_StatusUnavailable_NoTokenInResponses(HttpStatusCode code)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.StartStatusCode = code;
        host.Apify.StartBodyOverride = "{\"error\":{\"type\":\"user-or-token-not-found\",\"message\":\"Authentication token is not valid\"}}";

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.Unauthorized);
        var statusText = (await StatusAsync(host)).GetRawText();
        Assert.DoesNotContain(host.Settings.Token!, statusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC22b_ActorNotFoundOnStart_IsNotConfigured()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.StartStatusCode = HttpStatusCode.NotFound;

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.NotConfigured);
    }

    public static TheoryData<string> BadConfigurations => new()
    {
        "no-actor", "no-external-id-mapping", "no-title-mapping", "no-company-mapping", "no-description-mapping",
        "no-apply-mapping", "http-base-url", "timeout-zero", "timeout-too-large", "max-items-zero",
        "input-array", "input-not-json",
    };

    [Theory]
    [Trait("REQ", "F0.2-AC4")]
    [MemberData(nameof(BadConfigurations))]
    public async Task TC23_BadConfiguration_FailsAsNotConfigured_NothingSentToApify(string problem)
    {
        await using var host = QaHost.Create(factory, s =>
        {
            switch (problem)
            {
                case "no-actor": s.ActorId = string.Empty; break;
                case "no-external-id-mapping": s.Fields["ExternalId"] = string.Empty; break;
                case "no-title-mapping": s.Fields["Title"] = string.Empty; break;
                case "no-company-mapping": s.Fields["Company"] = string.Empty; break;
                case "no-description-mapping": s.Fields["Description"] = string.Empty; break;
                case "no-apply-mapping": s.Fields["ApplyUrl"] = string.Empty; break;
                case "http-base-url": s.BaseUrl = "http://api.apify.example/"; break;
                case "timeout-zero": s.RunTimeoutSeconds = 0; break;
                case "timeout-too-large": s.RunTimeoutSeconds = 3601; break;
                case "max-items-zero": s.MaxItems = 0; break;
                case "input-array": s.InputJson = "[1]"; break;
                case "input-not-json": s.InputJson = "{not json"; break;
            }
        });
        host.Apify.ItemsJson = Items.Array(Items.Make("never"));

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.NotConfigured);
        Assert.Empty(host.Apify.Requests);
        Assert.Empty(await host.PostingsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC24_FailedFetch_LeavesExistingPostingsUntouched_AndNextGoodFetchRecovers()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("e1"), Items.Make("e2"), Items.Make("e3"));
        await host.FetchAsync();
        var before = await host.PostingsAsync();
        Assert.Equal("Available", (await StatusAsync(host)).GetProperty("state").GetString());

        host.Apify.StartStatusCode = HttpStatusCode.Unauthorized;
        var failed = await host.FetchAsync();

        Assert.False(failed.IsSuccess);
        var after = await host.PostingsAsync();
        Assert.Equal(before.Select(p => (p.Id, p.ContentHash, p.Title, p.CreatedAt)), after.Select(p => (p.Id, p.ContentHash, p.Title, p.CreatedAt)));
        Assert.Equal("Unavailable", (await StatusAsync(host)).GetProperty("state").GetString());
        Assert.Equal(2, (await host.RunsAsync()).Count);

        host.Apify.StartStatusCode = HttpStatusCode.Created;
        var good = await host.FetchAsync();
        Assert.True(good.IsSuccess);
        Assert.Equal(3, good.Value!.DuplicateCount);
        Assert.Equal("Available", (await StatusAsync(host)).GetProperty("state").GetString());
        Assert.Equal(3, (await host.PostingsAsync()).Count);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC25_StatusOnEmptyDatabase_IsNeverRun()
    {
        await using var host = QaHost.Create(factory);

        var response = await host.Client.GetAsync("/api/sourcing/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NeverRun", body.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastRun").ValueKind);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC4")]
    public async Task TC26_StatusAfterGoodFetch_IsAvailableWithCounts()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("s1"), Items.Make("s2"), "7");
        await host.FetchAsync();

        var run = (await StatusAsync(host)).GetProperty("lastRun");

        Assert.Equal("Succeeded", run.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("failureReason").ValueKind);
        Assert.Equal("apify", run.GetProperty("source").GetString());
        Assert.Equal(2, run.GetProperty("insertedCount").GetInt32());
        Assert.Equal(1, run.GetProperty("malformedCount").GetInt32());
        Assert.Equal(3, run.GetProperty("fetchedCount").GetInt32());
    }

    [Theory]
    [Trait("REQ", "F0.2-AC4")]
    [InlineData("start-500")]
    [InlineData("start-429")]
    [InlineData("start-502")]
    [InlineData("network-error")]
    [InlineData("http-timeout")]
    [InlineData("unreadable-run")]
    [InlineData("poll-500")]
    [InlineData("dataset-500")]
    public async Task TC27_ApifyUnreachableOrBroken_FailsAsUnreachable(string scenario)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("never"));
        switch (scenario)
        {
            case "start-500": host.Apify.StartStatusCode = HttpStatusCode.InternalServerError; break;
            case "start-429": host.Apify.StartStatusCode = HttpStatusCode.TooManyRequests; break;
            case "start-502": host.Apify.StartStatusCode = HttpStatusCode.BadGateway; break;
            case "network-error": host.Apify.Throw = _ => new HttpRequestException("Connection refused"); break;
            case "http-timeout": host.Apify.Throw = _ => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"); break;
            case "unreadable-run": host.Apify.StartBodyOverride = "{\"foo\":1}"; break;
            case "poll-500":
                host.Apify.RunStatusOnStart = "RUNNING";
                host.Apify.PollStatusCode = HttpStatusCode.InternalServerError;
                break;
            case "dataset-500": host.Apify.ItemsStatusCode = HttpStatusCode.InternalServerError; break;
        }

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.Unreachable);
        Assert.Empty(await host.PostingsAsync());
    }

    [Theory]
    [Trait("REQ", "F0.2-AC4")]
    [InlineData("POST", "/api/sourcing/status")]
    [InlineData("PUT", "/api/sourcing/status")]
    [InlineData("DELETE", "/api/sourcing/status")]
    [InlineData("POST", "/api/sourcing")]
    [InlineData("POST", "/api/sourcing/fetch")]
    [InlineData("POST", "/api/sourcing/fetch-now")]
    [InlineData("POST", "/api/sourcing/runs")]
    [InlineData("GET", "/api/sourcing/runs")]
    [InlineData("GET", "/api/sourcing/postings")]
    public async Task TC28_NothingOverHttpCanStartAFetch(string method, string path)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("never"));

        var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json"),
        });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{method} {path} -> {(int)response.StatusCode}");
        Assert.Empty(host.Apify.Requests);
        Assert.Empty(await host.RunsAsync());
        Assert.Empty(await host.PostingsAsync());
    }

    // ---- Edge 2: timed-out / failed runs --------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-Edge2")]
    public async Task TC35_ApifyReportsTimedOut_FailsAndStoresNothing_EvenWithPartialDataset()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.QueuePoll("TIMING-OUT", "TIMED-OUT");
        host.Apify.ItemsJson = Items.Array(Items.Make("partial-1"), Items.Make("partial-2"));

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.TimedOut);
        Assert.Empty(await host.PostingsAsync());
        Assert.DoesNotContain(host.Apify.Requests, r => r.Uri.Contains("/datasets/", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("REQ", "F0.2-Edge2")]
    public async Task TC35b_TimedOutAtStart_FailsAndStoresNothing()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.RunStatusOnStart = "TIMED-OUT";
        host.Apify.ItemsJson = Items.Array(Items.Make("partial-1"));

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.TimedOut);
        Assert.Empty(await host.PostingsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-Edge2")]
    public async Task TC36_RunNeverFinishes_GivesUpAbortsRunAndStoresNothing()
    {
        await using var host = QaHost.Create(factory, s => s.RunTimeoutSeconds = 1);
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;
        host.Apify.ItemsJson = Items.Array(Items.Make("partial-1"));
        var clock = Stopwatch.StartNew();

        var run = await host.FetchAsync();

        clock.Stop();
        await AssertFailedRunAsync(host, run, FetchFailureReason.TimedOut);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Gave up after {clock.Elapsed}");
        Assert.Contains(host.Apify.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/abort", StringComparison.Ordinal));
        Assert.DoesNotContain(host.Apify.Requests, r => r.Uri.Contains("/datasets/", StringComparison.Ordinal));
        Assert.Empty(await host.PostingsAsync());
    }

    [Theory]
    [Trait("REQ", "F0.2-Edge2")]
    [InlineData("FAILED")]
    [InlineData("ABORTED")]
    public async Task TC37_RunFailedOrAborted_FailsAsActorFailed_StoresNothing(string status)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.RunStatusOnStart = status;
        host.Apify.ItemsJson = Items.Array(Items.Make("partial-1"));

        var run = await host.FetchAsync();

        await AssertFailedRunAsync(host, run, FetchFailureReason.ActorFailed);
        Assert.Empty(await host.PostingsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-Edge2")]
    public async Task TC38_CallerCancelsMidFetch_StoresNothing()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;
        host.Apify.ItemsJson = Items.Array(Items.Make("partial-1"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.FetchAsync(cts.Token));

        Assert.Empty(await host.PostingsAsync());
    }

    // ---- NFR -------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("REQ", "F0.2-NFR")]
    public async Task TC39_FiveHundredItems_ProcessedWellUnderThirtySeconds()
    {
        await using var host = QaHost.Create(factory);
        var body = new string('x', 3_000);
        host.Apify.ItemsJson = Items.Array(Enumerable.Range(0, 500).Select(i =>
            Items.Make($"n{i:D4}", description: $"{body} {i}", url: $"https://boards.greenhouse.io/acme/jobs/{i}")));

        var first = Stopwatch.StartNew();
        var run1 = await host.FetchAsync();
        first.Stop();
        var second = Stopwatch.StartNew();
        var run2 = await host.FetchAsync();
        second.Stop();

        Assert.True(run1.IsSuccess, run1.Error?.Message);
        Assert.Equal(500, run1.Value!.InsertedCount);
        Assert.Equal(500, run2.Value!.DuplicateCount);
        Assert.True(first.Elapsed < TimeSpan.FromSeconds(30), $"first fetch took {first.Elapsed}");
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(30), $"second fetch took {second.Elapsed}");
        Assert.Contains(host.Apify.Requests, r => r.Uri.Contains("/datasets/", StringComparison.Ordinal) && r.Uri.Contains("limit=500", StringComparison.Ordinal));
        Console.WriteLine($"QA-NFR first={first.Elapsed.TotalMilliseconds:F0}ms second={second.Elapsed.TotalMilliseconds:F0}ms");
    }

    [Fact]
    [Trait("REQ", "F0.2-NFR")]
    public async Task TC39b_FiveHundredItemsWithLargeDescriptions_StillUnderThirtySeconds()
    {
        await using var host = QaHost.Create(factory);
        var body = new string('y', 50_000);
        host.Apify.ItemsJson = Items.Array(Enumerable.Range(0, 500).Select(i =>
            Items.Make($"L{i:D4}", description: $"{body} {i}")));

        var clock = Stopwatch.StartNew();
        var run = await host.FetchAsync();
        clock.Stop();

        Assert.True(run.IsSuccess, run.Error?.Message);
        Assert.Equal(500, run.Value!.InsertedCount);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
        Console.WriteLine($"QA-NFR large={clock.Elapsed.TotalMilliseconds:F0}ms");
    }

    [Fact]
    [Trait("REQ", "regression")]
    public async Task TC42_SampleJobsEndpoint_StillWorks()
    {
        await using var host = QaHost.Create(factory);

        var response = await host.Client.GetAsync("/api/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(body["items"]);
    }
}
