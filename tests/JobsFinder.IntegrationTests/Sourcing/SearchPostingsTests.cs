using System.Net;
using System.Text.Json;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.IntegrationTests.Sourcing;

/// <summary>F0.2-AC1: the postings list. Each test has its own database, because the list shows everything.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class SearchPostingsTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static JobPosting Posting(string id, DateTimeOffset fetchedAt, DateTimeOffset? postedAt = null, string? link = null) =>
        JobPosting.Create("apify", id, "Acme", $"Engineer {id}", null, false, $"Secret text {id}", link, postedAt, fetchedAt);

    private static async Task StoreAsync(FetchHost host, params JobPosting[] postings)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobPostings.AddRange(postings);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GetPostings_NewestFetchFirst_EvenWhenPostedLongAgo_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        await StoreAsync(
            host,
            Posting("old-fetch", Now.AddDays(-1), postedAt: Now),
            Posting("new-fetch", Now, postedAt: Now.AddYears(-1)),
            Posting("new-fetch-no-date", Now.AddSeconds(-1)));

        var titles = (await host.PostingsAsync()).Items.Select(p => p.Title).ToList();

        Assert.Equal(["Engineer new-fetch", "Engineer new-fetch-no-date", "Engineer old-fetch"], titles);
    }

    [Fact]
    public async Task GetPostings_Paging_EveryPostingOnceInStableOrder_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        await StoreAsync(host, [.. Enumerable.Range(1, 25).Select(i => Posting($"{i:D2}", Now))]);

        var pages = new[]
        {
            await host.PostingsAsync("?page=1&pageSize=10"),
            await host.PostingsAsync("?page=2&pageSize=10"),
            await host.PostingsAsync("?page=3&pageSize=10"),
            await host.PostingsAsync("?page=4&pageSize=10"),
        };

        Assert.Equal([10, 10, 5, 0], pages.Select(p => p.Items.Count));
        Assert.All(pages, p => Assert.Equal(25, p.TotalCount));
        var ids = pages.SelectMany(p => p.Items).Select(p => p.Id).ToList();
        Assert.Equal(25, ids.Distinct().Count());
        Assert.Equal((await host.PostingsAsync("?pageSize=100")).Items.Select(p => p.Id), ids);
        Assert.Equal(20, (await host.PostingsAsync()).Items.Count);
    }

    [Fact]
    public async Task GetPostings_ReturnsListFieldsOnly_EnumsAsStrings_F0_2_AC1()
    {
        await using var host = FetchHost.Create(factory);
        await StoreAsync(host, Posting("1", Now, link: "jobs@acme.com"));

        var body = await host.Client.GetStringAsync("/api/sourcing/postings");

        Assert.DoesNotContain("Secret text", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(
            ["id", "source", "company", "title", "location", "isRemote", "applyUrl", "applyPath", "postedAt", "createdAt"],
            item.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Email", item.GetProperty("applyPath").GetString());
        Assert.Equal("mailto:jobs@acme.com", item.GetProperty("applyUrl").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("postedAt").ValueKind);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=2147483647&pageSize=100")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=abc")]
    public async Task GetPostings_InvalidPaging_Returns400Problem_F0_2_AC1(string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/sourcing/postings{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
