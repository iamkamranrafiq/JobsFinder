using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Application.Common;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.Infrastructure.Sourcing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobsFinder.IntegrationTests.Sourcing;

/// <summary>
/// JF-7 against PostgreSQL. Fetches run the handler in a DI scope (the trigger is tested in StartFetchTests);
/// the status is read over HTTP. "Latest run" is global, so each test runs its own fetch first.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FetchJobPostingsTests(ApiFactory factory) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();
    private readonly string _company = $"Co{Guid.NewGuid():N}";

    public void Dispose() => _client.Dispose();

    private SourcedPosting Item(string id, string? description = null) => new(
        $"{_company}-{id}",
        _company,
        $"Engineer {id}",
        "London",
        true,
        description ?? $"Description {id}",
        $"https://boards.greenhouse.io/acme/jobs/{id}?x=a%20b",
        new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(5)));

    private async Task<Result<FetchRunDto>> FetchAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FetchJobPostingsHandler>().HandleAsync(CancellationToken.None);
    }

    private async Task<List<JobPosting>> StoredAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobPostings
            .AsNoTracking()
            .Where(p => p.Company == _company)
            .OrderBy(p => p.ExternalId)
            .ToListAsync();
    }

    private async Task<SourceStatusDto> StatusAsync() =>
        (await _client.GetFromJsonAsync<SourceStatusDto>("/api/sourcing/status", Json))!;

    [Fact]
    public async Task Fetch_StoresAllFields_F0_2_BR2()
    {
        factory.JobSource.Returns([Item("1")]);

        var result = await FetchAsync();

        Assert.Equal(1, result.Value!.InsertedCount);
        var posting = Assert.Single(await StoredAsync());
        Assert.Equal("scripted", posting.Source);
        Assert.Equal($"{_company}-1", posting.ExternalId);
        Assert.Equal("Engineer 1", posting.Title);
        Assert.Equal("London", posting.Location);
        Assert.True(posting.IsRemote);
        Assert.Equal("Description 1", posting.Description);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/1?x=a%20b", posting.ApplyUrl!.OriginalString);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero), posting.PostedAt);
        Assert.Equal(PostingContentHash.Compute(_company, "Engineer 1", "London", "Description 1"), posting.ContentHash);
    }

    [Fact]
    public async Task Fetch_GreenhouseLink_StoredAsAtsUrl_F0_2_AC3()
    {
        factory.JobSource.Returns([Item("1")]);
        await FetchAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Database
            .SqlQuery<string>($"""SELECT "ApplyPath" AS "Value" FROM job_postings WHERE "Company" = {_company}""")
            .SingleAsync();

        Assert.Equal("AtsUrl", stored);
    }

    [Fact]
    public async Task Fetch_Twice_NoDuplicateRows_F0_2_AC2()
    {
        factory.JobSource.Returns([Item("1"), Item("2"), Item("3")]);

        var first = await FetchAsync();
        var second = await FetchAsync();

        Assert.Equal((3, 0), (first.Value!.InsertedCount, first.Value.DuplicateCount));
        Assert.Equal((0, 3), (second.Value!.InsertedCount, second.Value.DuplicateCount));
        Assert.Equal(3, (await StoredAsync()).Count);
    }

    [Fact]
    public async Task Fetch_ConcurrentSameItems_NoDuplicateRows_F0_2_BR3()
    {
        factory.JobSource.Returns([.. Enumerable.Range(1, 50).Select(i => Item($"{i}"))]);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(FetchAsync)));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(50, results.Sum(r => r.Value!.InsertedCount));
        Assert.Equal(50, (await StoredAsync()).Count);
    }

    /// <summary>A lost race against another instance, which the in-process gate cannot see.</summary>
    [Fact]
    public async Task Fetch_OtherInstanceStoredFirst_UniqueIndexesHoldAndFetchRetries_F0_2_BR3()
    {
        factory.JobSource.Returns([Item("1"), Item("2")]);
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = new RacingRepository(
            scope.ServiceProvider.GetRequiredService<IJobPostingRepository>(),
            async () =>
            {
                await using var other = factory.Services.CreateAsyncScope();
                var db = other.ServiceProvider.GetRequiredService<AppDbContext>();
                db.JobPostings.Add(JobPosting.Create(
                    "scripted", $"{_company}-1", _company, "Engineer 1", "London", true, "Description 1", null, null, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(CancellationToken.None);
            });
        var handler = ActivatorUtilities.CreateInstance<FetchJobPostingsHandler>(scope.ServiceProvider, (IJobPostingRepository)repository);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Value!.InsertedCount, result.Value.DuplicateCount));
        Assert.Equal([$"{_company}-1", $"{_company}-2"], (await StoredAsync()).Select(p => p.ExternalId));
    }

    [Fact]
    public async Task Fetch_MalformedItems_SkippedCountedAndReported_F0_2_AC2()
    {
        factory.JobSource.Returns([Item("1"), Item("2") with { Title = " " }], malformed: 1);

        await FetchAsync();

        Assert.Single(await StoredAsync());
        var status = await StatusAsync();
        Assert.Equal(SourceState.Available, status.State);
        Assert.Equal((3, 1, 2), (status.LastRun!.FetchedCount, status.LastRun.InsertedCount, status.LastRun.MalformedCount));
    }

    [Theory]
    [InlineData(FetchFailureReason.Unauthorized)]
    [InlineData(FetchFailureReason.TimedOut)]
    public async Task Fetch_SourceUnavailable_StatusUnavailable_ExistingPostingsUnchanged_F0_2_AC4(FetchFailureReason reason)
    {
        factory.JobSource.Returns([Item("1")]);
        await FetchAsync();
        var before = Assert.Single(await StoredAsync());
        factory.JobSource.Fails(reason);

        var result = await FetchAsync();

        Assert.Equal("source_unavailable", result.Error!.Code);
        var status = await StatusAsync();
        Assert.Equal(SourceState.Unavailable, status.State);
        Assert.Equal((FetchRunStatus.Failed, reason), (status.LastRun!.Status, status.LastRun.FailureReason));
        var after = Assert.Single(await StoredAsync());
        Assert.Equal((before.Id, before.Title, before.ContentHash), (after.Id, after.Title, after.ContentHash));
    }

    [Fact]
    public async Task Fetch_AfterFailureThenSuccess_StatusAvailable_F0_2_AC4()
    {
        factory.JobSource.Fails(FetchFailureReason.Unauthorized);
        await FetchAsync();
        Assert.Equal(SourceState.Unavailable, (await StatusAsync()).State);
        factory.JobSource.Returns([Item("1")]);

        await FetchAsync();

        Assert.Equal(SourceState.Available, (await StatusAsync()).State);
    }

    [Fact]
    public async Task Fetch_RealApifySourceWithoutToken_StatusUnavailable_F0_2_AC4()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var http = new HttpClient(new NoNetworkHandler()) { BaseAddress = new Uri("https://apify.test/") };
        IJobSource source = new ApifyJobSource(http, new ApifyOptions(), TimeProvider.System, NullLogger<ApifyJobSource>.Instance);
        var handler = ActivatorUtilities.CreateInstance<FetchJobPostingsHandler>(scope.ServiceProvider, source);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal("Source unavailable", result.Error!.Message);
        var status = await StatusAsync();
        Assert.Equal(SourceState.Unavailable, status.State);
        Assert.Equal(FetchFailureReason.NotConfigured, status.LastRun!.FailureReason);
    }

    [Fact]
    public async Task GetStatus_SerializesEnumsAsStrings_F0_2_AC4()
    {
        factory.JobSource.Fails(FetchFailureReason.Unauthorized);
        await FetchAsync();

        var response = await _client.GetAsync("/api/sourcing/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unavailable", body.GetProperty("state").GetString());
        Assert.Equal("Unauthorized", body.GetProperty("lastRun").GetProperty("failureReason").GetString());
    }

    [Fact]
    public async Task Fetch_500Items_Under30Seconds_F0_2_NFR()
    {
        var text = string.Concat(Enumerable.Repeat("<p>A realistic paragraph of a job description.</p>", 120));
        factory.JobSource.Returns([.. Enumerable.Range(1, 500).Select(i => Item($"{i}", $"{text} {i}"))]);
        var stopwatch = Stopwatch.StartNew();

        var result = await FetchAsync();

        Assert.Equal(500, result.Value!.InsertedCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Took {stopwatch.Elapsed}.");
    }

    /// <summary>Lets a competitor store a posting between the first lookup and the save.</summary>
    private sealed class RacingRepository(IJobPostingRepository inner, Func<Task> competitor) : IJobPostingRepository
    {
        private bool _raced;

        public async Task<IReadOnlyList<PostingKey>> FindExistingAsync(
            string source,
            IReadOnlyCollection<string> externalIds,
            IReadOnlyCollection<string> contentHashes,
            CancellationToken cancellationToken)
        {
            var existing = await inner.FindExistingAsync(source, externalIds, contentHashes, cancellationToken);
            if (!_raced)
            {
                _raced = true;
                await competitor();
            }

            return existing;
        }

        public Task<PagedResult<JobPostingDto>> SearchAsync(SearchPostingsQuery query, CancellationToken cancellationToken) =>
            inner.SearchAsync(query, cancellationToken);

        public Task<PostingApplyTarget?> GetApplyTargetAsync(Guid id, CancellationToken cancellationToken) =>
            inner.GetApplyTargetAsync(id, cancellationToken);

        public void AddRange(IEnumerable<JobPosting> postings) => inner.AddRange(postings);

        public void DiscardPending(IEnumerable<JobPosting> postings) => inner.DiscardPending(postings);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No HTTP call is allowed without a token.");
    }
}
