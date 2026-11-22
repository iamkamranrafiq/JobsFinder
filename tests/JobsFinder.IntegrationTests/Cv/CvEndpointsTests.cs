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

[Collection(ApiTestGroup.Name)]
public sealed class CvEndpointsTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CvEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.AI.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private static string UniqueName(string extension) => $"cv-{Guid.NewGuid():N}.{extension}";

    private Task<HttpResponseMessage> UploadAsync(byte[] content, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return _client.PostAsync("/api/cv", form);
    }

    /// <summary>"Nothing is stored" has no HTTP list endpoint, so check the database directly.</summary>
    private async Task<int> StoredCountAsync(string fileName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CvDocuments.CountAsync(d => d.FileName == fileName);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task UploadCv_TextPdf_Returns201WithProfile_F0_1_AC1()
    {
        var name = UniqueName("pdf");

        var response = await UploadAsync(CvFixtures.TextPdf(), name);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cv = await response.Content.ReadFromJsonAsync<CvDto>(Json);
        Assert.Equal(name, cv!.Document.FileName);
        Assert.Equal(CvFormat.Pdf, cv.Document.Format);
        Assert.Equal("Ada Lovelace", cv.Profile.Data.Identity.FullName);
        Assert.Equal(1, cv.Profile.Version);
        Assert.Equal(1, _factory.AI.Calls);

        var get = await _client.GetFromJsonAsync<CvDto>(response.Headers.Location, Json);
        Assert.Equal(cv.Document, get!.Document);
        Assert.Equal("Analytical Engines Ltd", get.Profile.Data.CurrentEmployer);
    }

    [Fact]
    public async Task UploadCv_Docx_Returns201WithProfile_F0_1_AC2()
    {
        var response = await UploadAsync(CvFixtures.Docx(), UniqueName("docx"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cv = await response.Content.ReadFromJsonAsync<CvDto>(Json);
        Assert.Equal(CvFormat.Docx, cv!.Document.Format);
        Assert.Equal(CvDocument.DocxContentType, cv.Document.ContentType);
        Assert.Equal("Ada Lovelace", cv.Profile.Data.Identity.FullName);
    }

    [Fact]
    public async Task UploadCv_ImageOnlyPdf_Returns422NoTextAndStoresNothing_F0_1_AC3()
    {
        var name = UniqueName("pdf");

        var response = await UploadAsync(CvFixtures.ImageOnlyPdf(), name);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cv_no_text");
        Assert.Equal(0, await StoredCountAsync(name));
        Assert.Equal(0, _factory.AI.Calls);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    public async Task UploadCv_PasswordProtected_Returns422AndStoresNothing_F0_1_AC3(string extension)
    {
        var name = UniqueName(extension);
        var content = extension == "pdf" ? CvFixtures.PasswordProtectedPdf() : CvFixtures.EncryptedDocx();

        var response = await UploadAsync(content, name);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cv_password_protected");
        Assert.Equal(0, await StoredCountAsync(name));
    }

    [Fact]
    public async Task UploadCv_Over5Mb_Returns422TooLarge_F0_1_AC3()
    {
        var name = UniqueName("pdf");

        var response = await UploadAsync(CvFixtures.OfSize(CvDocument.MaxSizeBytes + 1), name);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cv_too_large");
        Assert.Equal(0, await StoredCountAsync(name));
    }

    [Fact]
    public async Task UploadCv_FarOverLimit_Returns413TooLarge_F0_1_AC3()
    {
        var response = await UploadAsync(CvFixtures.OfSize(8 * 1024 * 1024), UniqueName("pdf"));

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "cv_too_large");
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("png")]
    public async Task UploadCv_OtherFileType_Returns422UnsupportedType_F0_1_AC3(string extension)
    {
        var name = UniqueName(extension);
        var content = extension == "txt" ? CvFixtures.PlainText() : CvFixtures.PngImage();

        var response = await UploadAsync(content, name);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cv_unsupported_type");
        Assert.Equal(0, await StoredCountAsync(name));
    }

    [Fact]
    public async Task UploadCv_NoFile_Returns400ValidationProblem_F0_1_AC3()
    {
        using var form = new MultipartFormDataContent { { new StringContent("x"), "other" } };

        var response = await _client.PostAsync("/api/cv", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadCv_ThenDownloadFile_ReturnsIdenticalBytesAndFileName_F0_1_AC6()
    {
        var name = UniqueName("pdf");
        var content = CvFixtures.TextPdf();
        var upload = await UploadAsync(content, name);
        var cv = await upload.Content.ReadFromJsonAsync<CvDto>(Json);

        var download = await _client.GetAsync($"/api/cv/{cv!.Document.Id}/file");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(CvDocument.PdfContentType, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(name, download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await StoredCountAsync(name));
    }

    [Fact]
    public async Task GetCv_UnknownId_Returns404_F0_1_AC6()
    {
        var get = await _client.GetAsync($"/api/cv/{Guid.NewGuid()}");
        var file = await _client.GetAsync($"/api/cv/{Guid.NewGuid()}/file");

        await AssertProblemAsync(get, HttpStatusCode.NotFound, "cv_not_found");
        await AssertProblemAsync(file, HttpStatusCode.NotFound, "cv_not_found");
    }

    [Fact]
    public async Task UploadCv_ModelReturnsInvalidJson3Times_Returns422UnreadableAndStoresNothing_F0_1_BR4()
    {
        var name = UniqueName("pdf");
        _factory.AI.Enqueue("not json", "{}", "{\"identity\":null}");

        var response = await UploadAsync(CvFixtures.TextPdf(), name);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cv_unreadable");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Could not read this CV; try another format", problem.GetProperty("title").GetString());
        Assert.Equal(3, _factory.AI.Calls);
        Assert.Equal(0, await StoredCountAsync(name));
    }
}
