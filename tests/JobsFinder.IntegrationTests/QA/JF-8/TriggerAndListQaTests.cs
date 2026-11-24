using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JobsFinder.IntegrationTests.QA.JF7;

namespace JobsFinder.IntegrationTests.QA.JF8;

/// <summary>F0.2-AC1 through the real HTTP trigger and list. The Apify side is a scripted fake (never the real Apify).</summary>
[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class TriggerAndListQaTests(ApiFactory factory)
{
    private static string Id() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC01_PostFetch_Returns202WithFetchId_ThenStatusShowsTheStoredRun()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()), Items.Make(Id()), Items.Make(Id()));

        var response = await host.PostFetchAsync();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/sourcing/status", response.Headers.Location?.ToString());
        var accepted = JsonDocument.Parse(body).RootElement;
        var id = accepted.GetProperty("currentFetchId").GetGuid();
        Assert.NotEqual(Guid.Empty, id);

        var status = await host.WaitIdleAsync();
        Assert.Equal("Available", status.GetProperty("state").GetString());
        var last = status.GetProperty("lastRun");
        Assert.Equal(id, last.GetProperty("id").GetGuid());
        Assert.Equal("Succeeded", last.GetProperty("status").GetString());
        Assert.Equal(3, last.GetProperty("insertedCount").GetInt32());
        Assert.Equal(3, last.GetProperty("fetchedCount").GetInt32());
        var run = Assert.Single(await host.RunsAsync());
        Assert.Equal(id, run.Id);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC02_List_ShowsStoredPostings_WithoutDescriptionOrHash()
    {
        await using var host = QaHost.Create(factory);
        var a = Id();
        host.Apify.ItemsJson = Items.Array(
            Items.Make(a, company: "Acme", title: "Backend Engineer", location: "Berlin", postedAt: "2026-01-05T10:00:00Z"),
            Items.Make(Id()),
            Items.Make(Id()));

        await host.FetchAndWaitAsync();
        var list = await host.ListAsync();

        Assert.Equal(3, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, list.GetProperty("page").GetInt32());
        Assert.Equal(20, list.GetProperty("pageSize").GetInt32());
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        var mine = items.Single(i => i.GetProperty("title").GetString() == "Backend Engineer");
        Assert.Equal("Acme", mine.GetProperty("company").GetString());
        Assert.Equal("Berlin", mine.GetProperty("location").GetString());
        Assert.Equal("apify", mine.GetProperty("source").GetString());
        Assert.Equal("AtsUrl", mine.GetProperty("applyPath").GetString());
        Assert.False(mine.GetProperty("isRemote").GetBoolean());
        Assert.Equal(
            DateTimeOffset.Parse("2026-01-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            mine.GetProperty("postedAt").GetDateTimeOffset());
        Assert.NotEqual(Guid.Empty, mine.GetProperty("id").GetGuid());
        Assert.True(mine.TryGetProperty("createdAt", out _));

        var raw = list.GetRawText();
        Assert.DoesNotContain("description", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Build things for", raw, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC03_List_DefaultPageSizeIs20_AndPagesCoverEverythingOnce()
    {
        await using var host = QaHost.Create(factory);
        var ids = Enumerable.Range(0, 25).Select(i => $"p{i:D2}{Id()}").ToList();
        host.Apify.ItemsJson = Items.Array(ids.Select(i => Items.Make(i)));
        await host.FetchAndWaitAsync();

        var page1 = await host.ListAsync();
        var page2 = await host.ListAsync("?page=2");
        var big = await host.ListAsync("?pageSize=100");

        Assert.Equal(25, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(20, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(5, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(25, big.GetProperty("items").GetArrayLength());
        var all = page1.Titles().Concat(page2.Titles()).ToList();
        Assert.Equal(25, all.Distinct().Count());
        Assert.Equal(ids.Select(i => $"Engineer {i}").Order(StringComparer.Ordinal), all.Order(StringComparer.Ordinal));
        Assert.Equal(all, big.Titles());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC04_SecondFetch_ListsItsNewPostingsAboveTheOlderOnes_InAStableOrder()
    {
        await using var host = QaHost.Create(factory);
        var old = Enumerable.Range(0, 3).Select(_ => Id()).ToList();
        var fresh = Enumerable.Range(0, 2).Select(_ => Id()).ToList();
        host.Apify.ItemsJson = Items.Array(old.Select(i => Items.Make(i)));
        await host.FetchAndWaitAsync();

        await Task.Delay(50);
        host.Apify.ItemsJson = Items.Array(old.Concat(fresh).Select(i => Items.Make(i)));
        await host.FetchAndWaitAsync();

        var first = (await host.ListAsync()).Titles();
        var second = (await host.ListAsync()).Titles();
        Assert.Equal(5, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(fresh.Select(i => $"Engineer {i}").Order(StringComparer.Ordinal), first.Take(2).Order(StringComparer.Ordinal));
        Assert.Equal(old.Select(i => $"Engineer {i}").Order(StringComparer.Ordinal), first.Skip(2).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC05_WhileRunning_StatusShowsCurrentFetchId_StateIsUnchanged_ListStillWorks()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        await host.FetchAndWaitAsync();

        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;
        var id = await host.StartAsync();

        var status = await host.StatusAsync();
        Assert.Equal(id, status.GetProperty("currentFetchId").GetGuid());
        Assert.Equal("Available", status.GetProperty("state").GetString());
        Assert.NotEqual(id, status.GetProperty("lastRun").GetProperty("id").GetGuid());
        Assert.Equal(1, (await host.ListAsync()).GetProperty("totalCount").GetInt32());

        host.Apify.PollForever = false;
        var done = await host.WaitIdleAsync();
        Assert.Equal(id, done.GetProperty("lastRun").GetProperty("id").GetGuid());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC2")]
    public async Task TC06_SameFetchTwice_ThroughTheTrigger_StoresNoDuplicates()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()), Items.Make(Id()), Items.Make(Id()));

        await host.FetchAndWaitAsync();
        var second = await host.FetchAndWaitAsync();

        var last = second.GetProperty("lastRun");
        Assert.Equal(0, last.GetProperty("insertedCount").GetInt32());
        Assert.Equal(3, last.GetProperty("duplicateCount").GetInt32());
        Assert.Equal(3, (await host.ListAsync()).GetProperty("totalCount").GetInt32());
        Assert.Equal(3, (await host.ListAsync()).GetProperty("items").GetArrayLength());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC3")]
    public async Task TC07_GreenhouseApplyLink_IsListedAsAtsUrl_OthersAsUnknownOrEmail()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(
            Items.Make("gh" + Id(), url: "https://boards.greenhouse.io/acme/jobs/42", title: "GH"),
            Items.Make("ot" + Id(), url: "https://example.com/careers/42", title: "OTHER"),
            Items.Make("em" + Id(), url: "mailto:jobs@example.com", title: "MAIL"));
        await host.FetchAndWaitAsync();

        var items = (await host.ListAsync()).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("title").GetString()!);
        Assert.Equal("AtsUrl", items["GH"].GetProperty("applyPath").GetString());
        Assert.NotEqual("AtsUrl", items["OTHER"].GetProperty("applyPath").GetString());
        Assert.Equal("Email", items["MAIL"].GetProperty("applyPath").GetString());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC11_SecondPostWhileRunning_Returns409FetchInProgress_AndStartsNoSecondRun()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;
        var id = await host.StartAsync();

        var second = await host.PostFetchAsync();
        var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("fetch_in_progress", problem.GetProperty("code").GetString());
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(id, (await host.StatusAsync()).GetProperty("currentFetchId").GetGuid());

        host.Apify.PollForever = false;
        await host.WaitIdleAsync();
        Assert.Equal(1, host.RunStarts());
        Assert.Single(await host.RunsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC12_TenParallelPosts_StartExactlyOneRun()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        host.Apify.RunStatusOnStart = "RUNNING";
        host.Apify.PollForever = true;

        var gate = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 10).Select(async _ =>
        {
            await gate.Task;
            return (await host.PostFetchAsync()).StatusCode;
        }).ToList();
        gate.SetResult();
        var codes = await Task.WhenAll(calls);

        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.Accepted));
        Assert.Equal(9, codes.Count(c => c == HttpStatusCode.Conflict));

        host.Apify.PollForever = false;
        await host.WaitIdleAsync();
        Assert.Equal(1, host.RunStarts());
        Assert.Single(await host.RunsAsync());
        Assert.Single((await host.ListAsync()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC12b_ParallelPostBursts_NeverStartTwoRunsAtOnce_AndNeverDeadlock()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        await host.FetchAndWaitAsync();

        var accepted = 0;
        for (var burst = 0; burst < 5; burst++)
        {
            var codes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => (await host.PostFetchAsync()).StatusCode));
            Assert.All(codes, c => Assert.True(c is HttpStatusCode.Accepted or HttpStatusCode.Conflict, c.ToString()));
            accepted += codes.Count(c => c == HttpStatusCode.Accepted);
            await host.WaitIdleAsync();
        }

        Assert.True(accepted >= 5, $"accepted {accepted}");
        Assert.Equal(1 + accepted, (await host.RunsAsync()).Count);
        Assert.Single((await host.ListAsync()).GetProperty("items").EnumerateArray());
    }

    public static TheoryData<string, string> NotJsonBodies => new()
    {
        { "text/plain", "{}" },
        { "application/x-www-form-urlencoded", "a=b" },
        { "text/plain;charset=UTF-8", "{\"x\":1}" },
        { "application/xml", "<a/>" },
    };

    [Theory]
    [MemberData(nameof(NotJsonBodies))]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC13_PostWithANonJsonContentType_Returns415_AndStartsNothing(string contentType, string body)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));

        var response = await host.Client.PostAsync(
            "/api/sourcing/fetches", new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType)));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertNothingStartedAsync(host);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC13b_PostWithAMultipartBody_StartsNothing()
    {
        await using var host = QaHost.Create(factory);
        using var form = new MultipartFormDataContent { { new StringContent("x"), "a" } };

        var response = await host.Client.PostAsync("/api/sourcing/fetches", form);

        Assert.True(response.StatusCode is HttpStatusCode.UnsupportedMediaType or HttpStatusCode.BadRequest, response.StatusCode.ToString());
        await AssertNothingStartedAsync(host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC13c_PostWithAnInvalidOrEmptyJsonBody_IsRejectedAs4xx_AndStartsNothing(string body)
    {
        await using var host = QaHost.Create(factory);

        var response = await host.Client.PostAsync("/api/sourcing/fetches", Jf8.Json(body));

        // The UI always sends {}. Anything else is a client error; the important part is that no run starts.
        Assert.True((int)response.StatusCode is >= 400 and < 500, $"{(int)response.StatusCode}");
        await AssertNothingStartedAsync(host);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC13d_PostWithNoBodyAndNoContentType_IsRejected_AndStartsNothing()
    {
        await using var host = QaHost.Create(factory);

        var response = await host.Client.PostAsync("/api/sourcing/fetches", content: null);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType, $"{(int)response.StatusCode}");
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
        await AssertNothingStartedAsync(host);
    }

    [Theory]
    [InlineData("{\"extra\":1,\"other\":[1,2]}")]
    [InlineData("{ }")]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC13e_JsonObjectBodies_AreAccepted_ExtraFieldsIgnored(string body)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));

        var response = await host.Client.PostAsync("/api/sourcing/fetches", Jf8.Json(body));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await host.WaitIdleAsync();
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC14_Preflight_FromAForeignOrigin_IsNotAllowed_FromTheAppOrigin_Is()
    {
        await using var host = QaHost.Create(factory);

        var evil = await PreflightAsync(host, "http://evil.example");
        var evilSub = await PreflightAsync(host, "http://localhost:4200.evil.example");
        var other = await PreflightAsync(host, "http://localhost:9999");
        var app = await PreflightAsync(host, "http://localhost:4200");

        foreach (var response in new[] { evil, evilSub, other })
        {
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"), "a foreign origin was allowed");
        }

        Assert.Equal("http://localhost:4200", Assert.Single(app.Headers.GetValues("Access-Control-Allow-Origin")));
        await AssertNothingStartedAsync(host);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC15_SimpleCrossSitePost_TextPlain_IsRejected_AndStartsNothing()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sourcing/fetches")
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Origin", "http://evil.example");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertNothingStartedAsync(host);
    }

    [Theory]
    [InlineData("GET", "/api/sourcing/fetches")]
    [InlineData("PUT", "/api/sourcing/fetches")]
    [InlineData("DELETE", "/api/sourcing/fetches")]
    [InlineData("PATCH", "/api/sourcing/fetches")]
    [InlineData("POST", "/api/sourcing/postings")]
    [InlineData("PUT", "/api/sourcing/postings")]
    [InlineData("DELETE", "/api/sourcing/postings")]
    [InlineData("POST", "/api/sourcing/status")]
    [InlineData("DELETE", "/api/sourcing/fetches/00000000-0000-0000-0000-000000000000")]
    [InlineData("GET", "/api/sourcing/fetches/00000000-0000-0000-0000-000000000000")]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC24_WrongVerbOrRoute_NeverStartsAFetchOrChangesData(string method, string path)
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(Id()));

        var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : Jf8.Json(),
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"{method} {path} -> {(int)response.StatusCode}");
        await AssertNothingStartedAsync(host);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC26_StatusOnAnEmptyDatabase_HasANullCurrentFetchId()
    {
        await using var host = QaHost.Create(factory);

        var status = await host.StatusAsync();

        Assert.Equal("NeverRun", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastRun").ValueKind);
        Assert.True(status.TryGetProperty("currentFetchId", out var current), "currentFetchId must be present");
        Assert.Equal(JsonValueKind.Null, current.ValueKind);
        var list = await host.ListAsync();
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC29_MalformedItemsMixedIn_AreSkippedAndCounted_RestIsListed()
    {
        await using var host = QaHost.Create(factory);
        var good = Id();
        host.Apify.ItemsJson = "[" + Items.Make(good) + ",{\"id\":\"" + Id() + "\"},123,null,\"x\","
            + Items.Make("nt" + Id(), title: "   ") + "]";

        var status = await host.FetchAndWaitAsync();

        var last = status.GetProperty("lastRun");
        Assert.Equal("Succeeded", last.GetProperty("status").GetString());
        Assert.Equal(1, last.GetProperty("insertedCount").GetInt32());
        Assert.True(last.GetProperty("malformedCount").GetInt32() >= 4, last.GetRawText());
        Assert.Equal($"Engineer {good}", Assert.Single((await host.ListAsync()).Titles()));
    }

    [Fact]
    [Trait("REQ", "F0.2-AC1")]
    public async Task TC28_FiveHundredItems_AreAllStoredAndListed_Quickly()
    {
        await using var host = QaHost.Create(factory);
        var prefix = Id();
        var big = new string('x', 5_000);
        host.Apify.ItemsJson = Items.Array(Enumerable.Range(0, 500).Select(i => Items.Make($"{prefix}-{i}", description: big)));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await host.FetchAndWaitAsync();
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
        var titles = new List<string>();
        for (var page = 1; page <= 5; page++)
        {
            var list = await host.ListAsync($"?page={page}&pageSize=100");
            Assert.Equal(500, list.GetProperty("totalCount").GetInt32());
            Assert.Equal(100, list.GetProperty("items").GetArrayLength());
            Assert.DoesNotContain(big, list.GetRawText(), StringComparison.Ordinal);
            titles.AddRange(list.Titles());
        }

        Assert.Equal(500, titles.Distinct().Count());
        Assert.Empty((await host.ListAsync("?page=6&pageSize=100")).GetProperty("items").EnumerateArray());
    }

    private static async Task AssertNothingStartedAsync(QaHost host)
    {
        Assert.Empty(host.Apify.Requests);
        Assert.Empty(await host.RunsAsync());
        var status = await host.StatusAsync();
        Assert.Equal("NeverRun", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("currentFetchId").ValueKind);
    }

    private static Task<HttpResponseMessage> PreflightAsync(QaHost host, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/sourcing/fetches");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return host.Client.SendAsync(request);
    }
}
