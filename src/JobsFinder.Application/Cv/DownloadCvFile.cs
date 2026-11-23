using JobsFinder.Application.Common;

namespace JobsFinder.Application.Cv;

/// <summary>F0.1-AC6: the raw CV file can be downloaded again.</summary>
public sealed class DownloadCvFileHandler(ICvRepository cvs)
{
    public async Task<Result<CvFileDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await cvs.GetFileAsync(id, cancellationToken);
        return file is null ? CvErrors.NotFound(id) : file;
    }
}
