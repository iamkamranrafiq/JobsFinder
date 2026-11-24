using JobsFinder.Application.Common;

namespace JobsFinder.Application.Sourcing;

/// <summary>
/// Reports the outcome of the latest finished fetch, so the UI can show "Source unavailable" (F0.2-AC4), and the
/// fetch that is running, if any (F0.2-AC1).
/// </summary>
public sealed class GetSourceStatusHandler(IFetchRunRepository runs, IFetchRunner runner)
{
    public async Task<Result<SourceStatusDto>> HandleAsync(CancellationToken cancellationToken)
    {
        // Before the run is read: CurrentFetchId is cleared only after the run is stored, so "idle" here means that
        // the latest run is already visible.
        var currentFetchId = runner.CurrentFetchId;
        var run = await runs.GetLatestAsync(cancellationToken);

        return SourcingMappings.ToStatus(run, currentFetchId);
    }
}
