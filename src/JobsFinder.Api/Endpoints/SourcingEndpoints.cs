using JobsFinder.Api.ErrorHandling;
using JobsFinder.Application.Sourcing;

namespace JobsFinder.Api.Endpoints;

internal static class SourcingEndpoints
{
    public static IEndpointRouteBuilder MapSourcingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sourcing").WithTags("Sourcing");

        // F0.2-AC4: read-only. The fetch trigger and the postings list arrive with F0.2-AC1.
        group.MapGet("/status", async (GetSourceStatusHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(Results.Ok))
            .WithName("GetSourceStatus")
            .Produces<SourceStatusDto>();

        return app;
    }
}
