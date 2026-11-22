using System.Text.Json;
using System.Text.RegularExpressions;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Cv;
using Microsoft.Extensions.Logging;

namespace JobsFinder.Infrastructure.Ai;

/// <summary>
/// Local, deterministic stand-in used when no API key is configured. It does not understand the CV:
/// it fills only what simple patterns find (email, phone, links, a name guess and a short summary)
/// and always returns a schema-valid profile. Only the CV profile schema is supported.
/// </summary>
internal sealed partial class StubAIProvider : IAIProvider
{
    private const int SummaryMaxLength = 500;

    public StubAIProvider(ILogger<StubAIProvider> logger) => LogStubActive(logger);

    public Task<string> CompleteJsonAsync(AIJsonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SchemaName != CandidateProfileSchema.Name)
        {
            throw new AIProviderException($"The stub AI provider does not support schema '{request.SchemaName}'.");
        }

        var text = request.UserContent;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstLine = lines.FirstOrDefault();

        var empty = CandidateProfileDataDto.Empty;
        var profile = empty with
        {
            Identity = new ProfileIdentityDto(firstLine is { Length: <= 80 } ? firstLine : null, null),
            Contact = new ProfileContactDto(Match(EmailPattern(), text), Match(PhonePattern(), text), null),
            Summary = Truncate(string.Join(' ', lines.Skip(1).Take(5))),
            Links = [.. UrlPattern().Matches(text)
                .Select(m => m.Value.TrimEnd('.', ',', ')'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(url => new ProfileLinkDto(null, url))],
        };

        return Task.FromResult(JsonSerializer.Serialize(profile, CandidateProfileSchema.SerializerOptions));
    }

    private static string? Match(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? Truncate(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= SummaryMaxLength ? value : value[..SummaryMaxLength];

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\+?\d[\d\s().-]{7,}\d")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stub AI provider is active: CV parsing returns a minimal profile. Configure AI:Anthropic:ApiKey for real parsing.")]
    private static partial void LogStubActive(ILogger logger);
}
