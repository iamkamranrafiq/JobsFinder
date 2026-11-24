using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

public interface IFetchRunRepository
{
    Task<FetchRunDto?> GetLatestAsync(CancellationToken cancellationToken);

    void Add(FetchRun run);

    /// <summary>Forgets a run that was added but not saved.</summary>
    void DiscardPending(FetchRun run);
}
