using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.IntegrationTests.Sourcing;

/// <summary>Replaces the Apify source in <see cref="ApiFactory"/>: tests never call a real service.</summary>
public sealed class ScriptedJobSource : IJobSource
{
    private Func<JobSourceResult> _next = () => new JobSourceResult([], 0);

    public string Name => "scripted";

    public void Returns(IReadOnlyList<SourcedPosting> postings, int malformed = 0) =>
        _next = () => new JobSourceResult(postings, malformed);

    public void Fails(FetchFailureReason reason) => _next = () => throw new JobSourceException(reason, "scripted failure");

    public async Task<JobSourceResult> FetchAsync(CancellationToken cancellationToken)
    {
        // Lets concurrent fetches overlap.
        await Task.Yield();
        return _next();
    }
}
