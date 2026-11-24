using System.Linq.Expressions;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Application.Sourcing;

/// <summary>Manual mapping only (no AutoMapper). Keep both forms in sync.</summary>
public static class SourcingMappings
{
    /// <summary>For EF Core queries: translated to SQL so only the needed columns are selected.</summary>
    public static readonly Expression<Func<FetchRun, FetchRunDto>> RunProjection = r =>
        new FetchRunDto(
            r.Id,
            r.Source,
            r.Status,
            r.FailureReason,
            r.StartedAt,
            r.CompletedAt,
            r.FetchedCount,
            r.InsertedCount,
            r.DuplicateCount,
            r.MalformedCount);

    private static readonly Func<FetchRun, FetchRunDto> CompiledRun = RunProjection.Compile();

    /// <summary>For in-memory entities (e.g. right after a fetch).</summary>
    public static FetchRunDto ToDto(this FetchRun run) => CompiledRun(run);
}
