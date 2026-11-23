using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Cv;
using JobsFinder.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>AI provider adapters, tested without network or API key. No Docker needed.</summary>
public class AIProviderTests
{
    private static AIJsonRequest Request(string userContent = "Ada Lovelace\nada@example.com\nhttps://github.com/ada") =>
        new(UploadCvHandler.SystemPrompt, userContent, CandidateProfileSchema.Json, CandidateProfileSchema.Name);

    [Fact]
    public async Task StubProvider_ReturnsSchemaValidProfile_F0_1_AC1()
    {
        var sut = new StubAIProvider(NullLogger<StubAIProvider>.Instance);

        var json = await sut.CompleteJsonAsync(Request(), CancellationToken.None);

        var parsed = CandidateProfileSchema.Parse(json);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal("Ada Lovelace", parsed.Profile!.Identity.FullName);
        Assert.Equal("ada@example.com", parsed.Profile.Contact.Email);
        Assert.Equal("https://github.com/ada", Assert.Single(parsed.Profile.Links).Url);
    }

    [Fact]
    public async Task AnthropicProvider_SendsForcedToolCallAndReturnsToolInput_F0_1_BR4()
    {
        var toolInput = new JsonObject { ["summary"] = "x" };
        var handler = new RecordingHandler(HttpStatusCode.OK, new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = "ignored" },
                new JsonObject { ["type"] = "tool_use", ["name"] = CandidateProfileSchema.Name, ["input"] = toolInput }),
        }.ToJsonString());
        var sut = CreateAnthropic(handler);

        var json = await sut.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.Equal(toolInput.ToJsonString(), JsonNode.Parse(json)!.ToJsonString());
        var sent = handler.Request!;
        Assert.Equal("https://api.anthropic.test/v1/messages", sent.Uri.ToString());
        Assert.Equal("test-key", sent.Headers["x-api-key"]);
        Assert.Equal(AnthropicAIProvider.ApiVersion, sent.Headers["anthropic-version"]);
        var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal("test-model", body["model"]!.GetValue<string>());
        Assert.Equal("tool", body["tool_choice"]!["type"]!.GetValue<string>());
        Assert.Equal(CandidateProfileSchema.Name, body["tool_choice"]!["name"]!.GetValue<string>());
        Assert.Equal("object", body["tools"]![0]!["input_schema"]!["type"]!.GetValue<string>());
        Assert.Equal(UploadCvHandler.SystemPrompt, body["system"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnthropicProvider_NoToolUse_ReturnsEmptySoCallerRetries_F0_1_BR4()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"content":[{"type":"text","text":"sorry"}]}""");
        var sut = CreateAnthropic(handler);

        var json = await sut.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.Equal(string.Empty, json);
        Assert.False(CandidateProfileSchema.Parse(json).IsValid);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnthropicProvider_ErrorStatus_ThrowsProviderException_F0_1_AC1(HttpStatusCode status)
    {
        var sut = CreateAnthropic(new RecordingHandler(status, """{"type":"error"}"""));

        await Assert.ThrowsAsync<AIProviderException>(() => sut.CompleteJsonAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public void AIOptions_NoKeyAndNoProvider_ResolvesToStub_F0_1_AC1()
    {
        Assert.Equal(AIProviderKind.Stub, new AIOptions().ResolveProvider());
        Assert.Equal(
            AIProviderKind.Anthropic,
            new AIOptions { Anthropic = new AnthropicOptions { ApiKey = "k" } }.ResolveProvider());
        Assert.Equal(
            AIProviderKind.Stub,
            new AIOptions { Provider = AIProviderKind.Stub, Anthropic = new AnthropicOptions { ApiKey = "k" } }.ResolveProvider());
    }

    private static AnthropicAIProvider CreateAnthropic(HttpMessageHandler handler)
    {
        var options = new AIOptions
        {
            Provider = AIProviderKind.Anthropic,
            Anthropic = new AnthropicOptions { ApiKey = "test-key", Model = "test-model" },
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test/") };
        return new AnthropicAIProvider(http, options, NullLogger<AnthropicAIProvider>.Instance);
    }

    private sealed record SentRequest(Uri Uri, Dictionary<string, string> Headers, string Body);

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public SentRequest? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Request = new SentRequest(request.RequestUri!, headers, body);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
