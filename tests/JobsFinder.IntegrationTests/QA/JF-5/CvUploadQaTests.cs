using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Application.Cv;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.IntegrationTests.Cv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.IntegrationTests.QA.JF5;

[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class CvUploadQaTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CvUploadQaTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.AI.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private static string Name(string ext = "pdf") => $"qa-{Guid.NewGuid():N}.{ext}";

    private Task<HttpResponseMessage> Upload(byte[] content, string fileName, string field = "file")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, field, fileName);
        return _client.PostAsync("/api/cv", form);
    }

    private async Task<int> Stored(string fileName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CvDocuments.CountAsync(d => d.FileName == fileName);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>(Json);

    private static async Task AssertProblem(HttpResponseMessage r, string code)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Body(r);
        Assert.Equal(code, p.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("title").GetString()));
    }

    [Fact, Trait("REQ", "F0.1-AC1")]
    public async Task TC01_ValidPdf_Returns201WithProfile()
    {
        var name = Name();
        var r = await Upload(CvFixtures.TextPdf(), name);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        var b = await Body(r);
        Assert.Equal(name, b.GetProperty("document").GetProperty("fileName").GetString());
        Assert.Equal(1, b.GetProperty("profile").GetProperty("version").GetInt32());
        Assert.Equal("Ada Lovelace", b.GetProperty("profile").GetProperty("data").GetProperty("identity").GetProperty("fullName").GetString());
        Assert.Equal(1, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-AC2")]
    public async Task TC02_ValidDocx_ParsedSameAsPdf()
    {
        var r = await Upload(CvFixtures.Docx(), Name("docx"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var b = await Body(r);
        Assert.Equal("Docx", b.GetProperty("document").GetProperty("format").GetString());
        Assert.Equal("Ada Lovelace", b.GetProperty("profile").GetProperty("data").GetProperty("identity").GetProperty("fullName").GetString());
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC03_ImageOnlyPdf_Rejected_NothingStored()
    {
        var name = Name();
        await AssertProblem(await Upload(CvFixtures.ImageOnlyPdf(), name), "cv_no_text");
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC04_PasswordProtectedPdf_Rejected_NothingStored()
    {
        var name = Name();
        await AssertProblem(await Upload(CvFixtures.PasswordProtectedPdf(), name), "cv_password_protected");
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC04b_EncryptedDocx_Rejected_NothingStored()
    {
        var name = Name("docx");
        await AssertProblem(await Upload(CvFixtures.EncryptedDocx(), name), "cv_password_protected");
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC05_SixMbFile_RejectedAsTooLarge_NothingStored()
    {
        var name = Name();
        var r = await Upload(CvFixtures.OfSize(6 * 1024 * 1024), name);
        Assert.True(r.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity, r.StatusCode.ToString());
        Assert.Equal("cv_too_large", (await Body(r)).GetProperty("code").GetString());
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC05b_FarOversizedFile_20Mb_RejectedWithoutStoring()
    {
        var name = Name();
        var r = await Upload(CvFixtures.OfSize(20 * 1024 * 1024), name);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Equal(0, await Stored(name));
    }

    [Theory, Trait("REQ", "F0.1-AC3")]
    [InlineData("txt")]
    [InlineData("png")]
    [InlineData("zip")]
    public async Task TC06_OtherFileTypes_Rejected(string kind)
    {
        var name = Name(kind);
        byte[] bytes = kind switch
        {
            "png" => CvFixtures.PngImage(),
            "zip" => CvFixtures.PlainZip(),
            _ => CvFixtures.PlainText(),
        };
        await AssertProblem(await Upload(bytes, name), "cv_unsupported_type");
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-BR1")]
    public async Task TC07a_PngNamedPdf_RejectedByContent()
    {
        var name = Name();
        await AssertProblem(await Upload(CvFixtures.PngImage(), name), "cv_unsupported_type");
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-BR1")]
    public async Task TC07b_PdfNamedTxt_AcceptedByContent()
    {
        var r = await Upload(CvFixtures.TextPdf(), Name("txt"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-BR1")]
    public async Task TC08_ExactlyFiveMb_NotRejectedForSize()
    {
        var r = await Upload(CvFixtures.OfSize(5 * 1024 * 1024), Name());
        var code = r.StatusCode == HttpStatusCode.Created ? string.Empty : (await Body(r)).GetProperty("code").GetString();
        Assert.NotEqual("cv_too_large", code);
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-BR1")]
    public async Task TC09_FiveMbPlusOne_RejectedAsTooLarge()
    {
        var name = Name();
        var r = await Upload(CvFixtures.OfSize((5 * 1024 * 1024) + 1), name);
        Assert.Equal("cv_too_large", (await Body(r)).GetProperty("code").GetString());
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC10a_EmptyFile_Returns400()
    {
        var r = await Upload([], Name());
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC10b_WrongFieldName_Returns400()
    {
        var r = await Upload(CvFixtures.TextPdf(), Name(), field: "document");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-AC3")]
    public async Task TC10c_NonMultipartBody_Returns400()
    {
        var r = await _client.PostAsync("/api/cv", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType, r.StatusCode.ToString());
    }

    [Fact, Trait("REQ", "F0.1-BR4")]
    public async Task TC11_InvalidJsonThreeTimes_UnreadableAndNotStored()
    {
        _factory.AI.Enqueue("not json", "{}", "[1]");
        var name = Name();
        var r = await Upload(CvFixtures.TextPdf(), name);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Body(r);
        Assert.Equal("cv_unreadable", p.GetProperty("code").GetString());
        Assert.Equal("Could not read this CV; try another format", p.GetProperty("title").GetString());
        Assert.Equal(3, _factory.AI.Calls);
        Assert.Equal(0, await Stored(name));
    }

    [Fact, Trait("REQ", "F0.1-BR4")]
    public async Task TC12_InvalidOnce_ThenValid_Succeeds()
    {
        _factory.AI.Enqueue("garbage");
        var r = await Upload(CvFixtures.TextPdf(), Name());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(2, _factory.AI.Calls);
    }

    [Fact, Trait("REQ", "F0.1-BR4")]
    public async Task TC13_InvalidTwice_ThenValid_SucceedsOnThirdCall()
    {
        _factory.AI.Enqueue("garbage", "garbage");
        var r = await Upload(CvFixtures.TextPdf(), Name());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(3, _factory.AI.Calls);
    }

    [Fact, Trait("REQ", "F0.1-AC6")]
    public async Task TC14_GetAndDownload_ReturnSameProfileAndIdenticalBytes()
    {
        var bytes = CvFixtures.TextPdf();
        var name = Name();
        var created = await Body(await Upload(bytes, name));
        var id = created.GetProperty("document").GetProperty("id").GetGuid();

        var get = await _client.GetAsync($"/api/cv/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(
            created.GetProperty("profile").GetProperty("data").ToString(),
            (await Body(get)).GetProperty("profile").GetProperty("data").ToString());

        var file = await _client.GetAsync($"/api/cv/{id}/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", file.Content.Headers.ContentType?.MediaType);
        var disp = file.Content.Headers.ContentDisposition;
        Assert.Contains(name, disp?.FileNameStar ?? disp?.FileName);
    }

    [Fact, Trait("REQ", "F0.1-AC6")]
    public async Task TC14b_DocxDownload_ReturnsIdenticalBytes()
    {
        var bytes = CvFixtures.Docx();
        var created = await Body(await Upload(bytes, Name("docx")));
        var id = created.GetProperty("document").GetProperty("id").GetGuid();
        var file = await _client.GetAsync($"/api/cv/{id}/file");
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());
    }

    [Fact, Trait("REQ", "F0.1-AC6")]
    public async Task TC15_UnknownId_Returns404ForGetAndFile()
    {
        var id = Guid.NewGuid();
        var a = await _client.GetAsync($"/api/cv/{id}");
        var b = await _client.GetAsync($"/api/cv/{id}/file");
        Assert.Equal(HttpStatusCode.NotFound, a.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, b.StatusCode);
        Assert.Equal("cv_not_found", (await Body(a)).GetProperty("code").GetString());
    }

    [Fact, Trait("REQ", "F0.1-AC6")]
    public async Task TC16_NonGuidId_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cv/abc")).StatusCode);
    }

    [Fact, Trait("REQ", "F0.1-BR2")]
    public async Task TC17_FullProfile_AllSectionsRoundTrip()
    {
        var full = CandidateProfileDataDto.Empty with
        {
            Identity = new ProfileIdentityDto("Grace Hopper", "Rear Admiral"),
            Contact = new ProfileContactDto("grace@example.com", "+1 555 0100", "Arlington, US"),
            Summary = "Compiler pioneer",
            Skills = [new ProfileSkillDto("COBOL", null, 30.5m)],
            Experience = [new ProfileExperienceDto("Engineer", "Navy", "1943", null, true, ["Built compiler"])],
            Education = [new ProfileEducationDto("Yale", "PhD", "Maths", "1930", "1934")],
            Certifications = [new ProfileCertificationDto("Cert A", "Org", 1999)],
            Links = [new ProfileLinkDto("Site", "https://example.com/grace")],
            WorkAuthorisation = "US citizen",
            CurrentEmployer = "Navy",
        };
        _factory.AI.Enqueue(JsonSerializer.Serialize(full, CandidateProfileSchema.SerializerOptions));
        var r = await Upload(CvFixtures.TextPdf(), Name());
        var raw = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.Created, raw);
        var d = JsonDocument.Parse(raw).RootElement.GetProperty("profile").GetProperty("data");
        Assert.Equal("Grace Hopper", d.GetProperty("identity").GetProperty("fullName").GetString());
        Assert.Equal("Navy", d.GetProperty("currentEmployer").GetString());
        Assert.Equal("US citizen", d.GetProperty("workAuthorisation").GetString());
        Assert.Single(d.GetProperty("skills").EnumerateArray());
        Assert.Single(d.GetProperty("experience").EnumerateArray());
        Assert.Single(d.GetProperty("education").EnumerateArray());
        Assert.Single(d.GetProperty("certifications").EnumerateArray());
        Assert.Single(d.GetProperty("links").EnumerateArray());
    }

    [Fact, Trait("REQ", "F0.1-BR2")]
    public async Task TC17b_CurrentEmployerNull_StillSucceeds()
    {
        _factory.AI.Enqueue(JsonSerializer.Serialize(CandidateProfileDataDto.Empty, CandidateProfileSchema.SerializerOptions));
        var r = await Upload(CvFixtures.TextPdf(), Name());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Body(r)).GetProperty("profile").GetProperty("data").GetProperty("currentEmployer").ValueKind);
    }

    [Fact, Trait("REQ", "F0.1-BR2")]
    public async Task TC18_NonEnglishCv_ParsesWithEnglishKeys()
    {
        _factory.AI.Enqueue(JsonSerializer.Serialize(
            CandidateProfileDataDto.Empty with { Identity = new ProfileIdentityDto("Jürgen Müller", "Softwareentwickler") },
            CandidateProfileSchema.SerializerOptions));
        var r = await Upload(CvFixtures.TextPdf(), Name());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await Body(r)).GetProperty("profile").GetProperty("data").GetProperty("identity");
        Assert.Equal("Jürgen Müller", id.GetProperty("fullName").GetString());
    }

    [Fact, Trait("REQ", "F0.1-AC6")]
    public async Task TC20_PathTraversalFileName_IsSanitised()
    {
        var r = await Upload(CvFixtures.TextPdf(), "..\\..\\evil/../cv-" + Guid.NewGuid().ToString("N") + ".pdf");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var fn = (await Body(r)).GetProperty("document").GetProperty("fileName").GetString()!;
        Assert.DoesNotContain("/", fn);
        Assert.DoesNotContain("\\", fn);
        Assert.DoesNotContain("..", fn);
    }
}
