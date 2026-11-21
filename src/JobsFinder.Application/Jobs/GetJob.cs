using JobsFinder.Application.Common;

namespace JobsFinder.Application.Jobs;

public sealed class GetJobHandler(IJobRepository jobs)
{
    public async Task<Result<JobDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await jobs.GetDtoByIdAsync(id, cancellationToken);
        return job is null
            ? Error.NotFound("job_not_found", $"Job '{id}' was not found.")
            : job;
    }
}
