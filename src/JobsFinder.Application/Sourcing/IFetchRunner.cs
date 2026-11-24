namespace JobsFinder.Application.Sourcing;

/// <summary>
/// Runs fetches in the background, one at a time (F0.2-AC1). Per process and not durable: the durable queue is F1.3.
/// </summary>
public interface IFetchRunner
{
    /// <summary>The running fetch, or null. It is cleared only after the run of the fetch has been stored.</summary>
    Guid? CurrentFetchId { get; }

    /// <summary>Starts a fetch whose run will be stored under this id. False when one is already running.</summary>
    bool TryStart(Guid fetchId);
}
