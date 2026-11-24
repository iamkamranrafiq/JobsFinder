using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Sourcing;

/// <summary>A normalised job posting fetched from a source (F0.2-BR2).</summary>
public sealed class JobPosting
{
    public const int SourceMaxLength = 100;
    public const int ExternalIdMaxLength = 200;
    public const int CompanyMaxLength = 200;
    public const int TitleMaxLength = 300;
    public const int LocationMaxLength = 300;
    public const int DescriptionMaxLength = 200_000;
    public const int ApplyUrlMaxLength = 2048;

    // Required by EF Core; real instances go through Create.
    private JobPosting()
    {
    }

    public Guid Id { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Company { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public bool IsRemote { get; private set; }

    /// <summary>As received from the source (may be HTML). Untrusted: never render it unescaped.</summary>
    public string Description { get; private set; } = string.Empty;

    public Uri? ApplyUrl { get; private set; }
    public ApplyPath ApplyPath { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static JobPosting Create(
        string source,
        string externalId,
        string company,
        string title,
        string? location,
        bool isRemote,
        string description,
        string? applyLink,
        DateTimeOffset? postedAt,
        DateTimeOffset now)
    {
        var target = ApplyPathClassifier.Classify(PostingText.CleanOrNull(applyLink));
        if (target.Url?.OriginalString.Length > ApplyUrlMaxLength)
        {
            throw new DomainException($"Posting apply URL must be at most {ApplyUrlMaxLength} characters.");
        }

        var posting = new JobPosting
        {
            Id = Guid.CreateVersion7(),
            Source = Required(source, SourceMaxLength, "source"),
            ExternalId = Required(externalId, ExternalIdMaxLength, "external id"),
            Company = Required(company, CompanyMaxLength, "company"),
            Title = Required(title, TitleMaxLength, "title"),
            Location = PostingText.CleanOrNull(location) is { } place ? Required(place, LocationMaxLength, "location") : null,
            IsRemote = isRemote,
            Description = Required(description, DescriptionMaxLength, "description"),
            ApplyUrl = target.Url,
            ApplyPath = target.Path,
            PostedAt = postedAt is { } posted ? StoragePrecision.ToMicroseconds(posted.ToUniversalTime()) : null,
            CreatedAt = StoragePrecision.ToMicroseconds(now.ToUniversalTime()),
        };

        posting.ContentHash = PostingContentHash.Compute(
            posting.Company, posting.Title, posting.Location, posting.Description);
        return posting;
    }

    private static string Required(string value, int maxLength, string field)
    {
        var text = PostingText.Clean(value);
        if (text.Length == 0)
        {
            throw new DomainException($"Posting {field} is required.");
        }

        return text.Length <= maxLength
            ? text
            : throw new DomainException($"Posting {field} must be at most {maxLength} characters.");
    }
}
