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

    public Task<CvFileDto?> GetFileAsync(Guid id, CancellationToken cancellationToken) =>
        db.CvDocuments
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new CvFileDto(d.FileName, d.ContentType, d.Content))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(CvDocument document) => db.CvDocuments.Add(document);

    public void Add(CandidateProfile profile) => db.CandidateProfiles.Add(profile);
}
