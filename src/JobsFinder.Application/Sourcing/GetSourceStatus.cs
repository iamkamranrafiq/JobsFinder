using JobsFinder.Application.Common;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

/// <summary>Reports the outcome of the latest fetch, so the UI can show "Source unavailable" (F0.2-AC4).</summary>
public sealed class GetSourceStatusHandler(IFetchRunRepository runs)
{
    public async Task<Result<SourceStatusDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var run = await runs.GetLatestAsync(cancellationToken);
        var state = run switch
        {
            null => SourceState.NeverRun,
            { Status: FetchRunStatus.Failed } => SourceState.Unavailable,
            _ => SourceState.Available,
        };

        return new SourceStatusDto(state, run);
    }
}
