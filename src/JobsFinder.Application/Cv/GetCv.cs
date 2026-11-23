using JobsFinder.Application.Common;

namespace JobsFinder.Application.Cv;

public sealed class GetCvHandler(ICvRepository cvs)
{
    public async Task<Result<CvDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var cv = await cvs.GetAsync(id, cancellationToken);
        return cv is null ? CvErrors.NotFound(id) : cv;
    }
}
