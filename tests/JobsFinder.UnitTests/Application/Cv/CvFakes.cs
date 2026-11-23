using System.Text.Json;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;

namespace JobsFinder.UnitTests.Application.Cv;

internal static class CvSamples
{
    public static CandidateProfileDataDto Profile() => new(
        new ProfileIdentityDto("Ada Lovelace", "Senior .NET Engineer"),
        new ProfileContactDto("ada@example.com", "+44 20 7946 0000", "London, UK"),
        "Backend engineer with 10 years of .NET.",
        [new ProfileSkillDto("C#", SkillProficiency.Expert, 10), new ProfileSkillDto("Angular", SkillProficiency.Advanced, 4)],
        [new ProfileExperienceDto("Senior Engineer", "Analytical Engines Ltd", "2019-03", null, true, ["Cut build times by 40%"])],
        [new ProfileEducationDto("University of London", "BSc", "Mathematics", "2010", "2013")],
        [new ProfileCertificationDto("AZ-204", "Microsoft", 2022)],
        [new ProfileLinkDto("GitHub", "https://github.com/ada")],
        "UK citizen",
        "Analytical Engines Ltd",
        new ProfilePreferencesDto(["Staff Engineer"], ["London", "Remote"], RemotePreference.Hybrid, 90000, 120000, "GBP", "3 months"));

    public static string ProfileJson(CandidateProfileDataDto? profile = null) =>
        JsonSerializer.Serialize(profile ?? Profile(), CandidateProfileSchema.SerializerOptions);

    public static byte[] Bytes(int length = 1024) => [.. Enumerable.Repeat((byte)'x', length)];
}

/// <summary>Returns scripted answers in order and records every request.</summary>
internal sealed class FakeAIProvider : IAIProvider
{
    private readonly Queue<Func<CancellationToken, Task<string>>> _responses = new();

    public List<AIJsonRequest> Requests { get; } = [];

    public FakeAIProvider Returns(params string[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(_ => Task.FromResult(response));
        }

        return this;
    }

    public FakeAIProvider Hangs()
    {
        _responses.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return string.Empty;
        });
        return this;
    }

    public FakeAIProvider Throws(Exception exception)
    {
        _responses.Enqueue(_ => Task.FromException<string>(exception));
        return this;
    }

    public Task<string> CompleteJsonAsync(AIJsonRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _responses.Count == 0
            ? throw new InvalidOperationException("No scripted AI response left.")
            : _responses.Dequeue()(cancellationToken);
    }
}

internal sealed class FakeCvTextExtractor(CvTextExtraction result) : ICvTextExtractor
{
    public int Calls { get; private set; }

    public Task<CvTextExtraction> ExtractAsync(byte[] content, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(result);
    }
}

internal sealed class FakeCvRepository : ICvRepository
{
    public List<CvDocument> Documents { get; } = [];
    public List<CandidateProfile> Profiles { get; } = [];

    public Task<CvDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = Documents.FirstOrDefault(d => d.Id == id);
        var profile = Profiles.FirstOrDefault(p => p.CvDocumentId == id);
        return Task.FromResult(document is null || profile is null ? null : new CvDto(document.ToDto(), profile.ToDto()));
    }

    public Task<CvFileDto?> GetFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = Documents.FirstOrDefault(d => d.Id == id);
        return Task.FromResult(document is null ? null : new CvFileDto(document.FileName, document.ContentType, document.Content));
    }

    public void Add(CvDocument document) => Documents.Add(document);

    public void Add(CandidateProfile profile) => Profiles.Add(profile);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
