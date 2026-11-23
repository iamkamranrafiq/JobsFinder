using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Cv;

/// <summary>
/// One version of the structured profile for a <see cref="CvDocument"/>. The parse is version 1 and every
/// save by the candidate adds the next version (F0.1-AC4, BR3). Rows are never changed after insert, and the
/// newest row across all CVs is the active profile.
/// </summary>
public sealed class CandidateProfile
{
    public const int FirstVersion = 1;

    // Required by EF Core; real instances go through Create.
    private CandidateProfile()
    {
    }

    public Guid Id { get; private set; }
    public Guid CvDocumentId { get; private set; }
    public int Version { get; private set; }
    public CandidateProfileData Data { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static CandidateProfile CreateFromParse(Guid cvDocumentId, CandidateProfileData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (cvDocumentId == Guid.Empty)
        {
            throw new DomainException("A profile must belong to a CV document.");
        }

        return new CandidateProfile
        {
            Id = Guid.CreateVersion7(),
            CvDocumentId = cvDocumentId,
            Version = FirstVersion,
            Data = data,
            CreatedAt = StoragePrecision.ToMicroseconds(now),
        };
    }

    /// <summary>A corrected profile saved by the candidate: the version after <paramref name="currentVersion"/>.</summary>
    public static CandidateProfile CreateNextVersion(
        Guid cvDocumentId,
        int currentVersion,
        CandidateProfileData data,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (cvDocumentId == Guid.Empty)
        {
            throw new DomainException("A profile must belong to a CV document.");
        }

        if (currentVersion < FirstVersion)
        {
            throw new DomainException($"A new profile version must follow version {FirstVersion} or later.");
        }

        return new CandidateProfile
        {
            Id = Guid.CreateVersion7(),
            CvDocumentId = cvDocumentId,
            Version = currentVersion + 1,
            Data = data,
            CreatedAt = StoragePrecision.ToMicroseconds(now),
        };
    }
}
