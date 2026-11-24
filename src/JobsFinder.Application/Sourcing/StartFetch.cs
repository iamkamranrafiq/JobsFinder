using JobsFinder.Application.Common;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

/// <summary>Has no fields: there is one source, set up by the operator.</summary>
public sealed record StartFetchCommand;

/// <summary>Starts a fetch in the background and returns at once (F0.2-AC1).</summary>
public sealed class StartFetchHandler(IFetchRunRepository runs, IFetchRunner runner)
{
    public async Task<Result<SourceStatusDto>> HandleAsync(StartFetchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Read first, so a database error cannot hide a fetch that was started.
        var lastRun = await runs.GetLatestAsync(cancellationToken);

        var fetchId = FetchRun.NewId();
        return runner.TryStart(fetchId)
            ? SourcingMappings.ToStatus(lastRun, fetchId)
            : SourcingErrors.FetchInProgress;
    }
}
