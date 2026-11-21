using JobsFinder.Application.Common;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.Application.Jobs;

/// <summary>
/// Implemented in Infrastructure. Read methods return DTOs projected in the database
/// (no tracking); write methods work on tracked entities.
/// </summary>
public interface IJobRepository
{
    Task<JobDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<JobDto>> SearchAsync(SearchJobsQuery query, CancellationToken cancellationToken);

    Task<bool> ExistsByUrlAsync(Uri url, CancellationToken cancellationToken);

    void Add(Job job);
}
