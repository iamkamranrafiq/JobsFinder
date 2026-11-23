using JobsFinder.Application.Common;

namespace JobsFinder.Application.Cv;

/// <summary>
/// F0.1-BR3, BR5: the active profile is the newest profile version across all CVs (Phase 0 has one user).
/// Later features read the candidate's data through this use case only, never from the raw CV.
/// </summary>
public sealed class GetActiveCvHandler(ICvRepository cvs)
{
    public async Task<Result<CvDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var cv = await cvs.GetActiveAsync(cancellationToken);
        return cv is null ? CvErrors.NoActiveProfile : cv;
    }
}
