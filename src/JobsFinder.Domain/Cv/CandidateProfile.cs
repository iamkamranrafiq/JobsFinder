using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Cv;

/// <summary>
/// A structured profile parsed from a <see cref="CvDocument"/>. The first parse is version 1;
/// editing and new versions arrive with F0.1-AC4 (JF-6).
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
}
