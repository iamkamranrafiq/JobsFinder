using FluentValidation;
using JobsFinder.Application.Common;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.Application.Jobs;

public sealed record SearchJobsQuery(string? Text, RemotePolicy? RemotePolicy, int Page = 1, int PageSize = 20);

public sealed class SearchJobsValidator : AbstractValidator<SearchJobsQuery>
{
    public const int MaxPageSize = 100;

    public SearchJobsValidator()
    {
        RuleFor(x => x.Text).MaximumLength(200);
        RuleFor(x => x.RemotePolicy).IsInEnum();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

public sealed class SearchJobsHandler(IJobRepository jobs, IValidator<SearchJobsQuery> validator)
{
    public async Task<Result<PagedResult<JobDto>>> HandleAsync(SearchJobsQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        return await jobs.SearchAsync(query, cancellationToken);
    }
}
