using System.Linq.Expressions;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.Application.Jobs;

public sealed record JobDto(
    Guid Id,
    string Title,
    string Company,
    string? Location,
    RemotePolicy RemotePolicy,
    Uri Url,
    DateTimeOffset PostedAt);

/// <summary>Manual mapping only (no AutoMapper). Keep both forms in sync.</summary>
public static class JobMappings
{
    /// <summary>For EF Core queries: translated to SQL so only the needed columns are selected.</summary>
    public static readonly Expression<Func<Job, JobDto>> Projection = j =>
        new JobDto(j.Id, j.Title, j.Company, j.Location, j.RemotePolicy, j.Url, j.PostedAt);

    private static readonly Func<Job, JobDto> Compiled = Projection.Compile();

    /// <summary>For in-memory entities (e.g. right after create).</summary>
    public static JobDto ToDto(this Job job) => Compiled(job);
}
