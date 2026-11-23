using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <summary>
/// Implemented in Infrastructure. Read methods return DTOs projected in the database
/// (no tracking); write methods work on tracked entities.
/// </summary>
public interface ICvRepository
{
    /// <summary>The CV metadata (never the file bytes) plus its latest profile version.</summary>
    Task<CvDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The active profile (newest profile version across all CVs) with its CV metadata (F0.1-BR3).</summary>
    Task<CvDto?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>The highest profile version of a CV, or null when the CV does not exist.</summary>
    Task<int?> GetLatestProfileVersionAsync(Guid cvDocumentId, CancellationToken cancellationToken);

    /// <summary>The raw file for download (F0.1-AC6).</summary>
    Task<CvFileDto?> GetFileAsync(Guid id, CancellationToken cancellationToken);

    void Add(CvDocument document);

    void Add(CandidateProfile profile);
}
