namespace JobsFinder.Infrastructure.Sourcing;

/// <summary>
/// Bound from the "Apify" configuration section. The token comes from user-secrets or an environment variable
/// only (F0.2-BR1). The actor, its input and the field mapping are the operator's configuration: no actor is assumed.
/// </summary>
public sealed class ApifyOptions
{
    public const string SectionName = "Apify";

    public Uri BaseUrl { get; set; } = new("https://api.apify.com/");

    public string? Token { get; set; }

    /// <summary>Actor id or "user/actor-name".</summary>
    public string? ActorId { get; set; }

    /// <summary>Actor input as a JSON object. Must not contain user credentials or cookies (F0.2-BR4).</summary>
    public string? InputJson { get; set; }

    /// <summary>Longest an actor run may take before the fetch fails as timed out.</summary>
    public int RunTimeoutSeconds { get; set; } = 300;

    /// <summary>Most items read from one run.</summary>
    public int MaxItems { get; set; } = 500;

    public ApifyFieldOptions Fields { get; set; } = new();
}

/// <summary>
/// Where each posting property is in an actor's output item: a dotted path such as "company.name" or "offices.0.name".
/// </summary>
public sealed class ApifyFieldOptions
{
    public string? ExternalId { get; set; }

    public string? Company { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? ApplyUrl { get; set; }

    /// <summary>Optional.</summary>
    public string? Location { get; set; }

    /// <summary>Optional; without it, a location that says "remote" counts as remote.</summary>
    public string? Remote { get; set; }

    /// <summary>Optional.</summary>
    public string? PostedAt { get; set; }

    public bool HasRequiredPaths =>
        !string.IsNullOrWhiteSpace(ExternalId)
        && !string.IsNullOrWhiteSpace(Company)
        && !string.IsNullOrWhiteSpace(Title)
        && !string.IsNullOrWhiteSpace(Description)
        && !string.IsNullOrWhiteSpace(ApplyUrl);
}
