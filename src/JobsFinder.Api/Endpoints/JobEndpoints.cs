using JobsFinder.Api.ErrorHandling;
using JobsFinder.Application.Jobs;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.Api.Endpoints;

internal static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("/", async (
                string? text,
                RemotePolicy? remotePolicy,
                int? page,
                int? pageSize,
                SearchJobsHandler handler,
                CancellationToken ct) =>
            (await handler.HandleAsync(new SearchJobsQuery(text, remotePolicy, page ?? 1, pageSize ?? 20), ct))
                .ToHttpResult(Results.Ok))
            .WithName("SearchJobs");

        group.MapGet("/{id:guid}", async (Guid id, GetJobHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetJob");

        group.MapPost("/", async (CreateJobCommand command, CreateJobHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct))
                    .ToHttpResult(job => Results.CreatedAtRoute("GetJob", new { id = job.Id }, job)))
            .WithName("CreateJob");

        return app;
    }
}
