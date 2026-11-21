using JobsFinder.Domain.Common;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.UnitTests.Domain;

public class JobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Url = new("https://example.com/jobs/1");

    [Fact]
    public void Create_ValidInput_TrimsAndSetsFields()
    {
        var job = Job.Create("  .NET Developer ", " Acme ", "  ", RemotePolicy.Remote, Url, Now.AddDays(-1), Now);

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal(".NET Developer", job.Title);
        Assert.Equal("Acme", job.Company);
        Assert.Null(job.Location);
        Assert.Equal(Now, job.CreatedAt);
    }

    [Fact]
    public void Create_PostedInFuture_Throws()
    {
        Assert.Throws<DomainException>(() =>
            Job.Create("Dev", "Acme", null, RemotePolicy.Remote, Url, Now.AddMinutes(1), Now));
    }

    [Theory]
    [InlineData("", "Acme")]
    [InlineData("Dev", " ")]
    public void Create_MissingRequiredText_Throws(string title, string company)
    {
        Assert.Throws<DomainException>(() =>
            Job.Create(title, company, null, RemotePolicy.Remote, Url, Now, Now));
    }
}
