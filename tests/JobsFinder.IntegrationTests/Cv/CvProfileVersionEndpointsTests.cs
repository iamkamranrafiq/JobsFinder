using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;
using JobsFinder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>
/// JF-6: correcting the parsed profile stores new versions, and the newest one is active.
/// "Active" is global in Phase 0, so each test uploads its own CV first; the collection runs tests one at a time.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class CvProfileVersionEndpointsTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CvProfileVersionEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.AI.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private async Task<CvDto> UploadAsync()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(CvFixtures.TextPdf());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"cv-{Guid.NewGuid():N}.pdf");

        var response = await _client.PostAsync("/api/cv", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CvDto>(Json))!;
    }

    private Task<HttpResponseMessage> SaveAsync(Guid cvId, int baseVersion, CandidateProfileDataDto data) =>
        _client.PostAsJsonAsync($"/api/cv/{cvId}/profile/versions", new SaveProfileVersionRequest(baseVersion, data), Json);

    private async Task<List<CandidateProfile>> StoredProfilesAsync(Guid cvId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CandidateProfiles.AsNoTracking().Where(p => p.CvDocumentId == cvId).OrderBy(p => p.Version).ToListAsync();
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    [Fact]
    public async Task PostVersion_EditedHeadline_Returns201Version2_AndActiveReturnsIt_F0_1_AC4()
    {
        var cv = await UploadAsync();
        var edited = cv.Profile.Data with { Identity = cv.Profile.Data.Identity with { Headline = "Staff .NET Engineer" } };

        var response = await SaveAsync(cv.Document.Id, 1, edited);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<CvDto>(Json);
        Assert.Equal(2, saved!.Profile.Version);
        Assert.Equal("Staff .NET Engineer", saved.Profile.Data.Identity.Headline);
        Assert.Equal(cv.Document, saved.Document);
        Assert.EndsWith($"/api/cv/{cv.Document.Id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var active = await _client.GetFromJsonAsync<CvDto>("/api/cv/active", Json);
        Assert.Equal((saved.Profile.Id, 2), (active!.Profile.Id, active.Profile.Version));

        var byId = await _client.GetFromJsonAsync<CvDto>($"/api/cv/{cv.Document.Id}", Json);
        Assert.Equal(2, byId!.Profile.Version);
    }

    [Fact]
    public async Task PostVersion_KeepsVersion1Unchanged_F0_1_BR3()
    {
        var cv = await UploadAsync();
        var edited = cv.Profile.Data with { Summary = "Corrected summary." };

        var response = await SaveAsync(cv.Document.Id, 1, edited);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var stored = await StoredProfilesAsync(cv.Document.Id);
        Assert.Equal([1, 2], stored.Select(p => p.Version));
        Assert.Equal(cv.Profile.Data.Summary, stored[0].Data.Summary);
        Assert.Equal("Corrected summary.", stored[1].Data.Summary);
    }

    [Fact]
    public async Task PostVersion_UnchangedData_StillCreatesNewVersion_F0_1_BR3()
    {
        var cv = await UploadAsync();

        var first = await SaveAsync(cv.Document.Id, 1, cv.Profile.Data);
        var second = await SaveAsync(cv.Document.Id, 2, cv.Profile.Data);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal([1, 2, 3], (await StoredProfilesAsync(cv.Document.Id)).Select(p => p.Version));
    }

    [Fact]
    public async Task PostVersion_StaleBaseVersion_Returns409_F0_1_BR3()
    {
        var cv = await UploadAsync();
        Assert.Equal(HttpStatusCode.Created, (await SaveAsync(cv.Document.Id, 1, cv.Profile.Data)).StatusCode);

        var response = await SaveAsync(cv.Document.Id, 1, cv.Profile.Data with { Summary = "Lost update" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "profile_version_conflict");
        Assert.Equal(2, (await StoredProfilesAsync(cv.Document.Id)).Count);
    }

    [Fact]
    public async Task PostVersion_ConcurrentSavesSameBase_NeverForkVersions_F0_1_BR3()
    {
        var cv = await UploadAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            SaveAsync(cv.Document.Id, 1, cv.Profile.Data with { Summary = $"Concurrent edit {i}" })));

        // Every request ends as 201 or 409 (never 500), and only one version 2 exists.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal([1, 2], (await StoredProfilesAsync(cv.Document.Id)).Select(p => p.Version));
    }

    [Fact]
    public async Task GetActive_AfterNewUpload_ReturnsNewUploadsProfile_F0_1_BR3()
    {
        var older = await UploadAsync();
        Assert.Equal(HttpStatusCode.Created, (await SaveAsync(older.Document.Id, 1, older.Profile.Data)).StatusCode);

        var newer = await UploadAsync();

        var active = await _client.GetFromJsonAsync<CvDto>("/api/cv/active", Json);
        Assert.Equal(newer.Document.Id, active!.Document.Id);
        Assert.Equal(newer.Profile.Id, active.Profile.Id);
    }

    [Fact]
    public async Task Upload_NoOngoingRole_CurrentEmployerNull_ThenSavedEmployerIsActive_F0_1_AC5()
    {
        _factory.AI.Enqueue(JsonSerializer.Serialize(
            CandidateProfileDataDto.Empty with { Identity = new ProfileIdentityDto("Grace Hopper", null) },
            CandidateProfileSchema.SerializerOptions));
        var cv = await UploadAsync();
        Assert.Null(cv.Profile.Data.CurrentEmployer);

        var response = await SaveAsync(cv.Document.Id, 1, cv.Profile.Data with { CurrentEmployer = "US Navy" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var active = await _client.GetFromJsonAsync<CvDto>("/api/cv/active", Json);
        Assert.Equal(cv.Document.Id, active!.Document.Id);
        Assert.Equal("US Navy", active.Profile.Data.CurrentEmployer);
    }

    [Fact]
    public async Task PostVersion_InvalidLink_Returns400ValidationProblem_F0_1_BR2()
    {
        var cv = await UploadAsync();
        var invalid = cv.Profile.Data with { Links = [new ProfileLinkDto("Site", "ftp://example.com")] };

        var response = await SaveAsync(cv.Document.Id, 1, invalid);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.TryGetProperty("errors", out _));
        Assert.Single(await StoredProfilesAsync(cv.Document.Id));
    }

    [Fact]
    public async Task PostVersion_UnknownCv_Returns404_F0_1_AC4()
    {
        var response = await SaveAsync(Guid.CreateVersion7(), 1, CandidateProfileDataDto.Empty);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "cv_not_found");
    }

    [Fact]
    public async Task GetActive_ReturnsActiveProfileForLaterFeatures_F0_1_BR5()
    {
        var cv = await UploadAsync();

        var response = await _client.GetAsync("/api/cv/active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var active = await response.Content.ReadFromJsonAsync<CvDto>(Json);
        Assert.Equal((cv.Profile.Id, cv.Profile.Version, cv.Profile.CreatedAt), (active!.Profile.Id, active.Profile.Version, active.Profile.CreatedAt));
        Assert.Equal(cv.Document, active.Document);
    }
}
