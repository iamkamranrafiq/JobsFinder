using JobsFinder.Application.Cv;
using JobsFinder.Domain.Common;
using JobsFinder.Domain.Cv;
using JobsFinder.UnitTests.Application.Cv;

namespace JobsFinder.UnitTests.Domain;

public class CandidateProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly CandidateProfileData Data = CvSamples.Profile().ToDomain();

    [Fact]
    public void CreateNextVersion_IncrementsVersionAndKeepsDocument_F0_1_AC4()
    {
        var documentId = Guid.CreateVersion7();
        var first = CandidateProfile.CreateFromParse(documentId, Data, Now);

        var next = CandidateProfile.CreateNextVersion(documentId, first.Version, Data with { Summary = "Edited" }, Now.AddMinutes(1));

        Assert.Equal(2, next.Version);
        Assert.Equal(documentId, next.CvDocumentId);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal("Edited", next.Data.Summary);
        Assert.Equal(CandidateProfile.FirstVersion, first.Version);
    }

    [Fact]
    public void CreateNextVersion_PreviousVersionBelowOne_Throws_F0_1_BR3()
    {
        Assert.Throws<DomainException>(() => CandidateProfile.CreateNextVersion(Guid.CreateVersion7(), 0, Data, Now));
    }

    [Fact]
    public void CreateNextVersion_EmptyDocumentId_Throws_F0_1_BR3()
    {
        Assert.Throws<DomainException>(() => CandidateProfile.CreateNextVersion(Guid.Empty, 1, Data, Now));
    }

    [Fact]
    public void CreateNextVersion_TimestampTruncatedToMicroseconds_F0_1_BR3()
    {
        var now = Now.AddTicks(7);

        var next = CandidateProfile.CreateNextVersion(Guid.CreateVersion7(), 1, Data, now);

        Assert.Equal(Now, next.CreatedAt);
    }
}
