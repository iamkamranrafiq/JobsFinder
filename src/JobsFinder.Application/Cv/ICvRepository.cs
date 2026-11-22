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

    /// <summary>The raw file for download (F0.1-AC6).</summary>
    Task<CvFileDto?> GetFileAsync(Guid id, CancellationToken cancellationToken);

    void Add(CvDocument document);

    void Add(CandidateProfile profile);
}
