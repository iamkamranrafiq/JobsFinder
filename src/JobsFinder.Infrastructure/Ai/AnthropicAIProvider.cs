using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobsFinder.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobsFinder.Infrastructure.Ai;

/// <summary>
/// Calls the Anthropic Messages API and forces a single tool call whose input schema is the requested
/// JSON schema, so the tool input is the structured answer. Prompts, inputs and outputs are never logged.
/// </summary>
internal sealed partial class AnthropicAIProvider(
    HttpClient http,
    AIOptions options,
    ILogger<AnthropicAIProvider> logger) : IAIProvider
{
    public const string ApiVersion = "2023-06-01";

    public async Task<string> CompleteJsonAsync(AIJsonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["model"] = options.Anthropic.Model,
            ["max_tokens"] = options.Anthropic.MaxTokens,
            ["system"] = request.SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = request.UserContent,
            }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = request.SchemaName,
                ["description"] = "Return the structured result.",
                ["input_schema"] = JsonNode.Parse(request.JsonSchema),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = request.SchemaName },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-api-key", options.Anthropic.ApiKey);
        message.Headers.Add("anthropic-version", ApiVersion);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new AIProviderException("The AI provider could not be reached.", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsed (the caller did not cancel).
            throw new AIProviderException("The AI provider timed out.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogProviderError(logger, (int)response.StatusCode);
                throw new AIProviderException($"The AI provider returned HTTP {(int)response.StatusCode}.");
            }

            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            return ExtractToolInput(json.RootElement, request.SchemaName);
        }
    }

    /// <summary>Returns the forced tool call's input as JSON, or an empty string (treated as invalid by the caller).</summary>
    internal static string ExtractToolInput(JsonElement root, string toolName)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.ValueEquals("tool_use")
                && block.TryGetProperty("name", out var name) && name.ValueEquals(toolName)
                && block.TryGetProperty("input", out var input))
            {
                return input.GetRawText();
            }
        }

        return string.Empty;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic API returned HTTP {StatusCode}")]
    private static partial void LogProviderError(ILogger logger, int statusCode);
}
