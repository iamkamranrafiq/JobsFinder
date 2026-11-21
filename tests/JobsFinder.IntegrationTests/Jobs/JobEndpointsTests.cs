using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Application.Common;
using JobsFinder.Application.Jobs;

namespace JobsFinder.IntegrationTests.Jobs;

[Collection(ApiTestGroup.Name)]
public class JobEndpointsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private static object NewJob(string url) => new
    {
        title = "Senior .NET Developer",
        company = "Acme",
        location = "Remote",
        remotePolicy = "Remote",
        url,
        postedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    [Fact]
    public async Task Post_ThenGet_ReturnsCreatedJob()
    {
        var create = await _client.PostAsJsonAsync("/api/jobs", NewJob($"https://example.com/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JobDto>(Json);

        var get = await _client.GetFromJsonAsync<JobDto>(create.Headers.Location, Json);

        Assert.Equal(created, get);
    }

    [Fact]
    public async Task Post_DuplicateUrl_Returns409()
    {
        var url = $"https://example.com/{Guid.NewGuid()}";
        (await _client.PostAsJsonAsync("/api/jobs", NewJob(url))).EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync("/api/jobs", NewJob(url));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidBody_Returns400ValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new { title = "", company = "", url = "https://example.com/x", postedAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_ByText_ReturnsMatches()
    {
        var company = $"Co{Guid.NewGuid():N}";
        var body = new { title = "Angular Dev", company, remotePolicy = "Hybrid", url = $"https://example.com/{Guid.NewGuid()}", postedAt = DateTimeOffset.UtcNow };
        (await _client.PostAsJsonAsync("/api/jobs", body)).EnsureSuccessStatusCode();

        var page = await _client.GetFromJsonAsync<PagedResult<JobDto>>($"/api/jobs?text={company}", Json);

        Assert.Equal(1, page!.TotalCount);
        Assert.Equal(company, page.Items[0].Company);
    }
}
