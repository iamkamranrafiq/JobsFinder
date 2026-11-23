namespace JobsFinder.Application.Abstractions;

/// <summary>
/// Provider-neutral access to a language model that answers with JSON matching a schema.
/// Implemented in Infrastructure (Anthropic client or a local stub); tests use fakes.
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Returns the model's raw JSON answer. The caller must validate it: providers do not guarantee
    /// that the output matches the schema. Throws <see cref="AIProviderException"/> when the provider
    /// cannot be reached or rejects the request.
    /// </summary>
    Task<string> CompleteJsonAsync(AIJsonRequest request, CancellationToken cancellationToken);
}

/// <param name="SystemPrompt">Instructions for the model.</param>
/// <param name="UserContent">The input to work on (e.g. CV text).</param>
/// <param name="JsonSchema">JSON schema (draft 2020-12 subset) the answer must match; root is an object.</param>
/// <param name="SchemaName">Short identifier for the schema, letters, digits and underscores only.</param>
public sealed record AIJsonRequest(string SystemPrompt, string UserContent, string JsonSchema, string SchemaName);

/// <summary>The AI provider is unavailable or refused the request (transport failure, auth, rate limit).</summary>
public sealed class AIProviderException : Exception
{
    public AIProviderException()
    {
    }

    public AIProviderException(string message)
        : base(message)
    {
    }

    public AIProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
