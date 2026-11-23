namespace JobsFinder.Infrastructure.Ai;

public enum AIProviderKind
{
    /// <summary>Local deterministic stub; the default when no API key is configured.</summary>
    Stub,

    /// <summary>Anthropic Messages API; requires <see cref="AnthropicOptions.ApiKey"/>.</summary>
    Anthropic,
}

/// <summary>Bound from the "AI" configuration section. The API key comes from user-secrets or env vars only.</summary>
public sealed class AIOptions
{
    public const string SectionName = "AI";

    /// <summary>Null means: Anthropic when a key is configured, otherwise Stub.</summary>
    public AIProviderKind? Provider { get; set; }

    public AnthropicOptions Anthropic { get; set; } = new();

    public AIProviderKind ResolveProvider() =>
        Provider ?? (string.IsNullOrWhiteSpace(Anthropic.ApiKey) ? AIProviderKind.Stub : AIProviderKind.Anthropic);
}

public sealed class AnthropicOptions
{
    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com/");

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-4-5";

    public int MaxTokens { get; set; } = 8192;

    /// <summary>Per-request timeout; the CV parse budget (55 s) still bounds the whole upload.</summary>
    public int TimeoutSeconds { get; set; } = 45;
}
