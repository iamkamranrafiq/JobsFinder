using JobsFinder.Api.ErrorHandling;
using JobsFinder.Application.Common;
using JobsFinder.Application.Sourcing;

namespace JobsFinder.Api.Endpoints;

internal static class SourcingEndpoints
{
    public static IEndpointRouteBuilder MapSourcingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sourcing").WithTags("Sourcing");

        // F0.2-AC1: 202, because an actor run can take minutes. The JSON body is required on purpose: it forces a CORS
        // preflight, so another site cannot start a fetch (which costs credit) from the user's browser.
        group.MapPost("/fetches", async (StartFetchCommand command, StartFetchHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct))
                    .ToHttpResult(status => Results.Accepted("/api/sourcing/status", status)))
            .WithName("StartFetch")
            .Produces<SourceStatusDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapGet("/postings", async (int? page, int? pageSize, SearchPostingsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SearchPostingsQuery(page ?? 1, pageSize ?? 20), ct)).ToHttpResult(Results.Ok))
            .WithName("SearchPostings")
            .Produces<PagedResult<JobPostingDto>>()
            .ProducesValidationProblem();

        // F0.2-AC4: also what the page polls while a fetch runs.
        group.MapGet("/status", async (GetSourceStatusHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(Results.Ok))
            .WithName("GetSourceStatus")
            .Produces<SourceStatusDto>();

        return app;
    }
}
