using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Jobs;

public sealed class Job
{
    public const int TitleMaxLength = 200;
    public const int CompanyMaxLength = 200;
    public const int LocationMaxLength = 200;
    public const int UrlMaxLength = 2048;

    // Required by EF Core; real instances go through Create.
    private Job()
    {
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Company { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public RemotePolicy RemotePolicy { get; private set; }
    public Uri Url { get; private set; } = null!;
    public DateTimeOffset PostedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Job Create(
        string title,
        string company,
        string? location,
        RemotePolicy remotePolicy,
        Uri url,
        DateTimeOffset postedAt,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Job title is required.");
        }

        if (string.IsNullOrWhiteSpace(company))
        {
            throw new DomainException("Company is required.");
        }

        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
        {
            throw new DomainException("Job URL must be absolute.");
        }

        if (postedAt > now)
        {
            throw new DomainException("Posted date cannot be in the future.");
        }

        return new Job
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            Company = company.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            RemotePolicy = remotePolicy,
            Url = url,
            PostedAt = postedAt,
            CreatedAt = now,
        };
    }
}
