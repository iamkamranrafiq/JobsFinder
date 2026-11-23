using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.IntegrationTests.Cv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.IntegrationTests.QA.JF6;

[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class ProfileVersionQaTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProfileVersionQaTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.AI.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private async Task<CvDto> UploadAsync(string? profileJson = null)
    {
        if (profileJson is not null)
        {
            _factory.AI.Enqueue(profileJson);
        }

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(CvFixtures.TextPdf());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"qa6-{Guid.NewGuid():N}.pdf");
        var res = await _client.PostAsync("/api/cv", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<CvDto>(Json))!;
    }

    private Task<HttpResponseMessage> Save(Guid id, int baseVersion, CandidateProfileDataDto data) =>
        _client.PostAsJsonAsync($"/api/cv/{id}/profile/versions", new SaveProfileVersionRequest(baseVersion, data), Json);

    private async Task<CvDto> Get(Guid id) =>
        (await _client.GetFromJsonAsync<CvDto>($"/api/cv/{id}", Json))!;

    private async Task<int> VersionRows(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<CandidateProfile>().CountAsync(p => p.CvDocumentId == id);
    }

    private static CandidateProfileDataDto With(CandidateProfileDataDto d, string headline) =>
        d with { Identity = d.Identity with { Headline = headline } };

    [Fact, Trait("REQ", "F0.1-AC4")]
    public async Task TC01_SaveEdit_CreatesVersion2WithEditedValue()
    {
        var cv = await UploadAsync();
        var headline = "Edited " + Guid.NewGuid().ToString("N");
        var res = await Save(cv.Document.Id, 1, With(cv.Profile.Data, headline));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var saved = (await res.Content.ReadFromJsonAsync<CvDto>(Json))!;
        Assert.Equal(2, saved.Profile.Version);
        Assert.Equal(headline, saved.Profile.Data.Identity.Headline);
        Assert.Equal(cv.Document.Id, saved.Document.Id);
    }

    [Fact, Trait("REQ", "F0.1-AC4")]
    public async Task TC02_AfterSave_ActiveEndpointReturnsNewVersion()
    {
        var cv = await UploadAsync();
        var headline = "Active " + Guid.NewGuid().ToString("N");
        await Save(cv.Document.Id, 1, With(cv.Profile.Data, headline));
        var active = await _client.GetFromJsonAsync<CvDto>("/api/cv/active", Json);
        Assert.Equal(cv.Document.Id, active!.Document.Id);
        Assert.Equal(2, active.Profile.Version);
        Assert.Equal(headline, active.Profile.Data.Identity.Headline);
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC03_AfterSave_GetCvReturnsLatestVersion()
    {
        var cv = await UploadAsync();
        await Save(cv.Document.Id, 1, With(cv.Profile.Data, "x"));
        var got = await Get(cv.Document.Id);
        Assert.Equal(2, got.Profile.Version);
        Assert.Equal("x", got.Profile.Data.Identity.Headline);
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC04_UnchangedSave_StillCreatesNewVersion()
    {
        var cv = await UploadAsync();
        var res = await Save(cv.Document.Id, 1, cv.Profile.Data);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(2, (await Get(cv.Document.Id)).Profile.Version);
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC05_RepeatedSaves_AccumulateVersions_AndKeepHistory()
    {
        var cv = await UploadAsync();
        for (var v = 1; v <= 3; v++)
        {
            var res = await Save(cv.Document.Id, v, With(cv.Profile.Data, "h" + v));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            Assert.Equal(v + 1, (await res.Content.ReadFromJsonAsync<CvDto>(Json))!.Profile.Version);
        }

        Assert.Equal(4, await VersionRows(cv.Document.Id));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v1 = await db.Set<CandidateProfile>().AsNoTracking()
            .SingleAsync(p => p.CvDocumentId == cv.Document.Id && p.Version == 1);
        Assert.Equal(cv.Profile.Data.Identity.Headline, v1.Data.Identity.Headline);
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC06_ActiveProfile_IsNewestAcrossCvs()
    {
        var a = await UploadAsync();
        var b = await UploadAsync();
        var active1 = await _client.GetFromJsonAsync<CvDto>("/api/cv/active", Json);
        Assert.Equal(b.Document.Id, active1!.Document.Id);
        await Save(a.Document.Id, 1, With(a.Profile.Data, "a-edited"));
        var active2 = await _client.GetFromJsonAsync<CvDto>("/api/cv/active", Json);
        Assert.Equal(a.Document.Id, active2!.Document.Id);
        Assert.Equal(2, active2.Profile.Version);
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC07_SecondSaveFromStaleBase_Returns409()
    {
        var cv = await UploadAsync();
        Assert.Equal(HttpStatusCode.Created, (await Save(cv.Document.Id, 1, With(cv.Profile.Data, "first"))).StatusCode);
        var res = await Save(cv.Document.Id, 1, With(cv.Profile.Data, "second"));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("profile_version_conflict", await res.Content.ReadAsStringAsync());
        Assert.Equal("first", (await Get(cv.Document.Id)).Profile.Data.Identity.Headline);
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC08_ConcurrentSavesSameBase_OneWins_NoServerError()
    {
        var cv = await UploadAsync();
        var tasks = Enumerable.Range(0, 6).Select(i => Save(cv.Document.Id, 1, With(cv.Profile.Data, "c" + i))).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, await VersionRows(cv.Document.Id));
    }

    [Fact, Trait("REQ", "F0.1-AC5")]
    public async Task TC09_ParsedEmployer_IsFilled()
    {
        var cv = await UploadAsync();
        Assert.Equal("Analytical Engines Ltd", cv.Profile.Data.CurrentEmployer);
    }

    [Fact, Trait("REQ", "F0.1-AC5")]
    public async Task TC10_NoEmployerInCv_IsEmpty_ThenUserCanFillIt()
    {
        var json = JsonSerializer.Serialize(CandidateProfileDataDto.Empty, CandidateProfileSchema.SerializerOptions);
        var cv = await UploadAsync(json);
        Assert.True(string.IsNullOrEmpty(cv.Profile.Data.CurrentEmployer));
        var res = await Save(cv.Document.Id, 1, cv.Profile.Data with { CurrentEmployer = "Babbage & Co" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("Babbage & Co", (await Get(cv.Document.Id)).Profile.Data.CurrentEmployer);
    }

    [Fact, Trait("REQ", "F0.1-AC5")]
    public async Task TC11_ClearingEmployer_IsAllowed()
    {
        var cv = await UploadAsync();
        var res = await Save(cv.Document.Id, 1, cv.Profile.Data with { CurrentEmployer = null });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.True(string.IsNullOrEmpty((await Get(cv.Document.Id)).Profile.Data.CurrentEmployer));
    }

    [Fact, Trait("REQ", "F0.1-BR2")]
    public async Task TC12_FullProfile_RoundTrips()
    {
        var cv = await UploadAsync();
        var full = new CandidateProfileDataDto(
            new ProfileIdentityDto("Grace Hopper", "Rear Admiral"),
            new ProfileContactDto("grace@example.com", "+1 555 0100", "New York"),
            "Summary text",
            [new ProfileSkillDto("COBOL", SkillProficiency.Expert, 30.5m)],
            [new ProfileExperienceDto("Engineer", "Navy", "1943-12", "1986", false, ["Built compiler", "Led team"])],
            [new ProfileEducationDto("Yale", "PhD", "Maths", "1930", "1934")],
            [new ProfileCertificationDto("Cert", "Issuer", 1999)],
            [new ProfileLinkDto("Site", "https://example.com/g")],
            "US citizen",
            "US Navy",
            new ProfilePreferencesDto(["CTO"], ["Remote"], RemotePreference.Remote, 100m, 200m, "USD", "1 month"));
        var res = await Save(cv.Document.Id, 1, full);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var got = (await Get(cv.Document.Id)).Profile.Data;
        Assert.Equal(JsonSerializer.Serialize(full, Json), JsonSerializer.Serialize(got, Json));
    }

    [Fact, Trait("REQ", "F0.1-BR2")]
    public async Task TC13_EmployerTooLong_Returns400_NoNewVersion()
    {
        var cv = await UploadAsync();
        var res = await Save(cv.Document.Id, 1, cv.Profile.Data with { CurrentEmployer = new string('x', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(1, (await Get(cv.Document.Id)).Profile.Version);
        Assert.Equal(1, await VersionRows(cv.Document.Id));
    }

    [Fact, Trait("REQ", "F0.1-BR2")]
    public async Task TC14_BoundaryLengths_Accepted()
    {
        var cv = await UploadAsync();
        var res = await Save(cv.Document.Id, 1, cv.Profile.Data with
        {
            CurrentEmployer = new string('e', 200),
            Summary = new string('s', 4000),
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    public static TheoryData<string> InvalidCases() => new()
    {
        "skill-empty-name", "bad-date", "bad-url", "salary-range", "cert-year", "skill-years", "summary-4001", "bad-email-len",
    };

    [Theory, Trait("REQ", "F0.1-BR2")]
    [MemberData(nameof(InvalidCases))]
    public async Task TC15_InvalidFieldValues_Return400_NoNewVersion(string kind)
    {
        var cv = await UploadAsync();
        var d = cv.Profile.Data;
        var bad = kind switch
        {
            "skill-empty-name" => d with { Skills = [new ProfileSkillDto("", null, null)] },
            "bad-date" => d with { Experience = [new ProfileExperienceDto("R", "C", "2020-13", null, false, [])] },
            "bad-url" => d with { Links = [new ProfileLinkDto(null, "javascript:alert(1)")] },
            "salary-range" => d with { Preferences = d.Preferences with { SalaryMin = 200, SalaryMax = 100 } },
            "cert-year" => d with { Certifications = [new ProfileCertificationDto("C", null, 1800)] },
            "skill-years" => d with { Skills = [new ProfileSkillDto("S", null, 71)] },
            "summary-4001" => d with { Summary = new string('s', 4001) },
            _ => d with { Contact = d.Contact with { Email = new string('a', 201) } },
        };
        var res = await Save(cv.Document.Id, 1, bad);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(1, await VersionRows(cv.Document.Id));
    }

    [Theory, Trait("REQ", "F0.1-BR3")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TC16_NonPositiveBaseVersion_Returns400(int baseVersion)
    {
        var cv = await UploadAsync();
        var res = await Save(cv.Document.Id, baseVersion, cv.Profile.Data);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(1, await VersionRows(cv.Document.Id));
    }

    [Fact, Trait("REQ", "F0.1-BR3")]
    public async Task TC17_FutureBaseVersion_Returns409()
    {
        var cv = await UploadAsync();
        var res = await Save(cv.Document.Id, 5, cv.Profile.Data);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(1, await VersionRows(cv.Document.Id));
    }

    [Fact, Trait("REQ", "F0.1-AC4")]
    public async Task TC18_UnknownCv_Returns404()
    {
        var res = await Save(Guid.NewGuid(), 1, CandidateProfileDataDto.Empty);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("cv_not_found", await res.Content.ReadAsStringAsync());
    }

    [Theory, Trait("REQ", "F0.1-AC4")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"baseVersion\":1}")]
    [InlineData("{\"baseVersion\":1,\"data\":null}")]
    [InlineData("not json")]
    [InlineData("{\"baseVersion\":\"x\",\"data\":{}}")]
    public async Task TC19_MalformedBody_Returns4xx_NoNewVersion(string body)
    {
        var cv = await UploadAsync();
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var res = await _client.PostAsync($"/api/cv/{cv.Document.Id}/profile/versions", content);
        Assert.True((int)res.StatusCode is >= 400 and < 500, $"got {(int)res.StatusCode}");
        Assert.Equal(1, await VersionRows(cv.Document.Id));
    }

    [Fact, Trait("REQ", "F0.1-AC4")]
    public async Task TC20_NonGuidId_Returns404()
    {
        var res = await _client.PostAsJsonAsync(
            "/api/cv/not-a-guid/profile/versions",
            new SaveProfileVersionRequest(1, CandidateProfileDataDto.Empty),
            Json);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-AC4")]
    public async Task TC22_TooManySkills_Returns400()
    {
        var cv = await UploadAsync();
        var skills = Enumerable.Range(0, 101).Select(i => new ProfileSkillDto("s" + i, null, null)).ToList();
        var res = await Save(cv.Document.Id, 1, cv.Profile.Data with { Skills = skills });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-AC4")]
    public async Task TC23_ScriptLikeText_StoredVerbatim()
    {
        var cv = await UploadAsync();
        const string evil = "<script>alert(1)</script>'; DROP TABLE cv;--";
        var res = await Save(cv.Document.Id, 1, With(cv.Profile.Data, evil));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(evil, (await Get(cv.Document.Id)).Profile.Data.Identity.Headline);
    }

    [Fact, Trait("REQ", "F0.1-AC6")]
    public async Task TC24_RawFile_StillDownloadableAfterSave()
    {
        var cv = await UploadAsync();
        var before = await _client.GetByteArrayAsync($"/api/cv/{cv.Document.Id}/file");
        await Save(cv.Document.Id, 1, With(cv.Profile.Data, "z"));
        var after = await _client.GetByteArrayAsync($"/api/cv/{cv.Document.Id}/file");
        Assert.Equal(before, after);
        Assert.NotEmpty(after);
    }
}
