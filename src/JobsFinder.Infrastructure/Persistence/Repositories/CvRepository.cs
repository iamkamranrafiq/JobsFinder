using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;
using Microsoft.EntityFrameworkCore;

namespace JobsFinder.Infrastructure.Persistence.Repositories;

internal sealed class CvRepository(AppDbContext db) : ICvRepository
{
    public async Task<CvDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.CvDocuments
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(CvMappings.DocumentProjection)
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        var profile = await db.CandidateProfiles
            .AsNoTracking()
            .Where(p => p.CvDocumentId == id)
            .OrderByDescending(p => p.Version)
            .ThenBy(p => p.Id)
            .Select(CvMappings.ProfileProjection)
            .FirstOrDefaultAsync(cancellationToken);

        // A document is only ever saved together with its first profile.
        return profile is null ? null : new CvDto(document, profile);
    }

    public async Task<CvDto?> GetActiveAsync(CancellationToken cancellationToken)
    {
        // Phase 0 has a single user: the newest profile version across all CVs is the active one (F0.1-BR3).
        var profile = await db.CandidateProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(CvMappings.ProfileProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var document = await db.CvDocuments
            .AsNoTracking()
            .Where(d => d.Id == profile.CvDocumentId)
            .Select(CvMappings.DocumentProjection)
            .SingleAsync(cancellationToken);

        return new CvDto(document, profile);
    }

    public Task<int?> GetLatestProfileVersionAsync(Guid cvDocumentId, CancellationToken cancellationToken) =>
        db.CandidateProfiles
            .AsNoTracking()
            .Where(p => p.CvDocumentId == cvDocumentId)
            .MaxAsync(p => (int?)p.Version, cancellationToken);

    public Task<CvFileDto?> GetFileAsync(Guid id, CancellationToken cancellationToken) =>
        db.CvDocuments
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new CvFileDto(d.FileName, d.ContentType, d.Content))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(CvDocument document) => db.CvDocuments.Add(document);

    public void Add(CandidateProfile profile) => db.CandidateProfiles.Add(profile);
}
