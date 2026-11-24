using System.Text.Json.Nodes;
using JobsFinder.Domain.Sourcing;
using Npgsql;

namespace JobsFinder.IntegrationTests.QA.JF7;

[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class DedupAndStorageQaTests(ApiFactory factory)
{
    private static string Three() => Items.Array(Items.Make("a1"), Items.Make("a2"), Items.Make("a3"));

    [Fact]
    [Trait("REQ", "F0.2-AC2")]
    public async Task TC01_SameFetchTwice_CreatesNoDuplicates()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Three();

        var first = await host.FetchAsync();
        var second = await host.FetchAsync();

        Assert.True(first.IsSuccess);
        Assert.Equal(3, first.Value!.InsertedCount);
        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value!.InsertedCount);
        Assert.Equal(3, second.Value.DuplicateCount);
        Assert.Equal(3, (await host.PostingsAsync()).Count);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR3")]
    public async Task TC02_SameContentOtherExternalId_IsNotInsertedAgain()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("x1", title: "Same Title", description: "Same body"));
        await host.FetchAsync();

        host.Apify.ItemsJson = Items.Array(Items.Make("x2-other-id", title: "Same Title", description: "Same body"));
        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess);
        Assert.Equal(0, run.Value!.InsertedCount);
        Assert.Equal(1, run.Value.DuplicateCount);
        Assert.Single(await host.PostingsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-AC2")]
    public async Task TC03_SameExternalIdChangedContent_IsNotInsertedAndExistingRowUnchanged()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("same-id", title: "Original title", description: "Original body"));
        await host.FetchAsync();
        var before = Assert.Single(await host.PostingsAsync());

        host.Apify.ItemsJson = Items.Array(Items.Make("same-id", title: "Edited title", description: "Edited body"));
        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess);
        Assert.Equal(0, run.Value!.InsertedCount);
        Assert.Equal(1, run.Value.DuplicateCount);
        var after = Assert.Single(await host.PostingsAsync());
        Assert.Equal(before.Id, after.Id);
        Assert.Equal("Original title", after.Title);
        Assert.Equal(before.ContentHash, after.ContentHash);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC2")]
    public async Task TC04_SameItemTwiceInOneFetch_StoredOnce()
    {
        await using var host = QaHost.Create(factory);
        var item = Items.Make("dup-1");
        host.Apify.ItemsJson = Items.Array(item, item);

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess);
        Assert.Equal(1, run.Value!.InsertedCount);
        Assert.Equal(1, run.Value.DuplicateCount);
        Assert.Single(await host.PostingsAsync());
    }

    [Fact]
    [Trait("REQ", "F0.2-BR3")]
    public async Task TC05_HashIgnoresCaseAndWhitespaceButNotLocation()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("h1", company: "Globex", title: "Data Engineer", description: "Work with data", location: "Paris"));
        await host.FetchAsync();

        host.Apify.ItemsJson = Items.Array(
            Items.Make("h2", company: "  GLOBEX ", title: "data   engineer", description: "Work\n with   DATA", location: "paris"));
        var sameContent = await host.FetchAsync();
        Assert.Equal(0, sameContent.Value!.InsertedCount);
        Assert.Equal(1, sameContent.Value.DuplicateCount);

        host.Apify.ItemsJson = Items.Array(
            Items.Make("h3", company: "Globex", title: "Data Engineer", description: "Work with data", location: "Madrid"));
        var otherLocation = await host.FetchAsync();
        Assert.Equal(1, otherLocation.Value!.InsertedCount);
        Assert.Equal(2, (await host.PostingsAsync()).Count);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC2")]
    public async Task TC06_TwoConcurrentFetches_StoreEachPostingOnce()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Enumerable.Range(0, 40).Select(i => Items.Make($"c{i:D3}")));

        var results = await Task.WhenAll(host.FetchAsync(), host.FetchAsync(), host.FetchAsync());

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));
        Assert.Equal(40, results.Sum(r => r.Value!.InsertedCount));
        Assert.Equal(40, (await host.PostingsAsync()).Count);
        Assert.Equal(3, (await host.RunsAsync()).Count);
    }

    [Fact]
    [Trait("REQ", "F0.2-AC2")]
    public async Task TC07_OverlappingFetch_InsertsOnlyTheNewOnes()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Three();
        await host.FetchAsync();

        host.Apify.ItemsJson = Items.Array(Items.Make("a1"), Items.Make("a2"), Items.Make("a3"), Items.Make("a4"), Items.Make("a5"));
        var run = await host.FetchAsync();

        Assert.Equal(2, run.Value!.InsertedCount);
        Assert.Equal(3, run.Value.DuplicateCount);
        Assert.Equal(5, (await host.PostingsAsync()).Count);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC13_FullItem_StoresEveryAttribute()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make(
            "full-1",
            company: "Initech",
            title: "Staff Engineer",
            description: "<p>Do <b>good</b> work</p>",
            url: "https://job-boards.greenhouse.io/initech/jobs/42",
            location: "Berlin, Germany",
            remote: true,
            postedAt: "2025-03-04T05:06:07Z"));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess);
        var p = Assert.Single(await host.PostingsAsync());
        Assert.Equal("apify", p.Source);
        Assert.Equal("full-1", p.ExternalId);
        Assert.Equal("Initech", p.Company);
        Assert.Equal("Staff Engineer", p.Title);
        Assert.Equal("Berlin, Germany", p.Location);
        Assert.True(p.IsRemote);
        Assert.Equal("<p>Do <b>good</b> work</p>", p.Description);
        Assert.Equal("https://job-boards.greenhouse.io/initech/jobs/42", p.ApplyUrl!.OriginalString);
        Assert.Equal(ApplyPath.AtsUrl, p.ApplyPath);
        Assert.Equal(DateTimeOffset.Parse("2025-03-04T05:06:07Z", System.Globalization.CultureInfo.InvariantCulture), p.PostedAt);
        Assert.Matches("^[0-9a-f]{64}$", p.ContentHash);
        Assert.Equal(
            await host.ScalarAsync("select \"ContentHash\" from job_postings limit 1"),
            p.ContentHash);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC14_RemoteFlag_FromBooleanTextAndLocation()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(
            Items.Make("r-true", remote: true),
            Items.Make("r-text", remote: "Remote"),
            Items.Make("r-false", remote: false, location: "Remote - EU"),
            Items.Make("r-loc", location: "Remote - EU"),
            Items.Make("r-none", location: "Lisbon"));

        await host.FetchAsync();

        var byId = (await host.PostingsAsync()).ToDictionary(p => p.ExternalId);
        Assert.True(byId["r-true"].IsRemote);
        Assert.True(byId["r-text"].IsRemote);
        Assert.False(byId["r-false"].IsRemote);
        Assert.True(byId["r-loc"].IsRemote);
        Assert.False(byId["r-none"].IsRemote);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC14b_LocationSayingNoRemoteWork_IsNotFlaggedRemote()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("r-onsite", location: "Munich (on-site, no remote work)"));

        await host.FetchAsync();

        Assert.False(Assert.Single(await host.PostingsAsync()).IsRemote);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC15_PostedDate_ParsedFromIsoAndEpochs_MissingAllowed_BadIsMalformed()
    {
        await using var host = QaHost.Create(factory);
        var moment = DateTimeOffset.Parse("2025-03-04T05:06:07Z", System.Globalization.CultureInfo.InvariantCulture);
        host.Apify.ItemsJson = Items.Array(
            Items.Make("d-iso", postedAt: "2025-03-04T05:06:07Z"),
            Items.Make("d-sec", postedAt: moment.ToUnixTimeSeconds()),
            Items.Make("d-ms", postedAt: moment.ToUnixTimeMilliseconds()),
            Items.Make("d-none"),
            Items.Make("d-bad", postedAt: "banana"));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess);
        Assert.Equal(4, run.Value!.InsertedCount);
        Assert.Equal(1, run.Value.MalformedCount);
        var byId = (await host.PostingsAsync()).ToDictionary(p => p.ExternalId);
        Assert.Equal(moment, byId["d-iso"].PostedAt);
        Assert.Equal(moment, byId["d-sec"].PostedAt);
        Assert.Equal(moment, byId["d-ms"].PostedAt);
        Assert.Null(byId["d-none"].PostedAt);
        Assert.False(byId.ContainsKey("d-bad"));
    }

    [Theory]
    [Trait("REQ", "F0.2-BR2")]
    [InlineData("title", 300, true)]
    [InlineData("title", 301, false)]
    [InlineData("company", 200, true)]
    [InlineData("company", 201, false)]
    [InlineData("id", 200, true)]
    [InlineData("id", 201, false)]
    [InlineData("location", 300, true)]
    [InlineData("location", 301, false)]
    [InlineData("description", 200_000, true)]
    [InlineData("description", 200_001, false)]
    public async Task TC16_LengthBoundaries(string field, int length, bool accepted)
    {
        await using var host = QaHost.Create(factory);
        var item = JsonNode.Parse(Items.Make("len-ok"))!.AsObject();
        item[field] = new string('x', length);
        host.Apify.ItemsJson = Items.Array(item.ToJsonString(), Items.Make("len-control"));

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess, run.Error?.Message);
        Assert.Equal(accepted ? 2 : 1, run.Value!.InsertedCount);
        Assert.Equal(accepted ? 0 : 1, run.Value.MalformedCount);
    }

    [Fact]
    [Trait("REQ", "F0.2-BR2")]
    public async Task TC17_NulAndLoneSurrogate_DoNotFailTheRun_HtmlKeptAsReceived()
    {
        await using var host = QaHost.Create(factory);
        var nul = Items.Make("t-nul", description: "abc\0def");
        const string surrogate = "{\"id\":\"t-sur\",\"company\":\"Acme\",\"title\":\"T\",\"description\":\"abc\\ud800def\",\"url\":\"https://x.example\"}";
        var html = Items.Make("t-html", description: "<script>alert(1)</script><b>x</b>");
        host.Apify.ItemsJson = Items.Array(nul, surrogate, html);

        var run = await host.FetchAsync();

        Assert.True(run.IsSuccess, run.Error?.Message);
        var byId = (await host.PostingsAsync()).ToDictionary(p => p.ExternalId);
        Assert.Equal("abcdef", byId["t-nul"].Description);
        Assert.Equal("<script>alert(1)</script><b>x</b>", byId["t-html"].Description);
        if (byId.TryGetValue("t-sur", out var cleaned))
        {
            Assert.DoesNotContain(cleaned.Description, c => char.IsSurrogate(c));
        }
        else
        {
            Assert.Equal(1, run.Value!.MalformedCount);
        }
    }

    [Fact]
    [Trait("REQ", "F0.2-BR3")]
    public async Task TC40_DatabaseRejectsDuplicateHashAndDuplicateExternalId()
    {
        await using var host = QaHost.Create(factory);
        host.Apify.ItemsJson = Items.Array(Items.Make("u1"));
        await host.FetchAsync();

        const string insert =
            "insert into job_postings (\"Id\",\"Source\",\"ExternalId\",\"Company\",\"Title\",\"IsRemote\",\"Description\",\"ApplyPath\",\"ContentHash\",\"CreatedAt\") " +
            "select gen_random_uuid(), \"Source\", {0}, 'c', 't', false, 'd', 'Unknown', {1}, now() from job_postings limit 1";

        var sameHash = await Assert.ThrowsAsync<PostgresException>(() =>
            host.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "'other-id'", "\"ContentHash\"")));
        Assert.Equal("23505", sameHash.SqlState);

        var sameKey = await Assert.ThrowsAsync<PostgresException>(() =>
            host.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "'u1'", "repeat('a', 64)")));
        Assert.Equal("23505", sameKey.SqlState);
    }
}
