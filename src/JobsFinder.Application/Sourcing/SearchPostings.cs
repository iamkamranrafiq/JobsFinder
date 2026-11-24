using FluentValidation;
using JobsFinder.Application.Common;

namespace JobsFinder.Application.Sourcing;

public sealed record SearchPostingsQuery(int Page = 1, int PageSize = 20);

public sealed class SearchPostingsValidator : AbstractValidator<SearchPostingsQuery>
{
    public const int MaxPageSize = 100;

    /// <summary>Keeps the row offset far below the integer limit.</summary>
    public const int MaxPage = 100_000;

    public SearchPostingsValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

/// <summary>Lists the stored postings, the most recently fetched first (F0.2-AC1).</summary>
public sealed class SearchPostingsHandler(IJobPostingRepository postings, IValidator<SearchPostingsQuery> validator)
{
    public async Task<Result<PagedResult<JobPostingDto>>> HandleAsync(SearchPostingsQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        return await postings.SearchAsync(query, cancellationToken);
    }
}
