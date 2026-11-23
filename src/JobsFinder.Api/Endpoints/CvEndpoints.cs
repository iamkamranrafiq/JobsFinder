using JobsFinder.Api.ErrorHandling;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;
using Microsoft.AspNetCore.Http.Features;

namespace JobsFinder.Api.Endpoints;

internal static class CvEndpoints
{
    public const string FileFieldName = "file";

    /// <summary>
    /// Request cap: 1 MB above the 5 MB file limit so that slightly oversized files still reach the handler
    /// and get the specific <c>cv_too_large</c> reason instead of a transport error.
    /// </summary>
    public const long MaxRequestBytes = CvDocument.MaxSizeBytes + (1024 * 1024);

    public static IEndpointRouteBuilder MapCvEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cv").WithTags("Cv");

        group.MapPost("/", UploadAsync)
            .WithName("UploadCv")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<CvDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem()
            // Phase 0 has no cookies or auth, so there is nothing for CSRF to abuse. Revisit with F1.1.
            .DisableAntiforgery();

        // F0.1-BR3/BR5: the active profile, the one read path later features use.
        group.MapGet("/active", async (GetActiveCvHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(Results.Ok))
            .WithName("GetActiveCv")
            .Produces<CvDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // F0.1-AC4: every save of a corrected profile stores a new version, which becomes active.
        group.MapPost("/{id:guid}/profile/versions", async (
                Guid id,
                SaveProfileVersionRequest request,
                SaveProfileVersionHandler handler,
                CancellationToken ct) =>
                (await handler.HandleAsync(new SaveProfileVersionCommand(id, request.BaseVersion, request.Data), ct))
                    .ToHttpResult(Created))
            .WithName("SaveProfileVersion")
            .Produces<CvDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetCvHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetCv");

        group.MapGet("/{id:guid}/file", async (Guid id, DownloadCvFileHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct))
                    .ToHttpResult(file => Results.File(file.Content, file.ContentType, file.FileName)))
            .WithName("DownloadCvFile");

        return app;
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, UploadCvHandler handler, CancellationToken ct)
    {
        if (request.ContentLength > MaxRequestBytes)
        {
            return CvErrors.TooLarge.ToProblem(StatusCodes.Status413PayloadTooLarge);
        }

        // Chunked uploads have no Content-Length; let the server enforce the cap while reading.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = MaxRequestBytes;
        }

        // A missing form or file falls through as an empty command, which the validator rejects (400).
        var file = request.HasFormContentType
            ? (await request.ReadFormAsync(ct)).Files.GetFile(FileFieldName)
            : null;

        byte[] content = [];
        if (file is not null)
        {
            if (file.Length > MaxRequestBytes)
            {
                return CvErrors.TooLarge.ToProblem(StatusCodes.Status413PayloadTooLarge);
            }

            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        var command = new UploadCvCommand(Path.GetFileName(file?.FileName ?? string.Empty), content);
        return (await handler.HandleAsync(command, ct)).ToHttpResult(Created);
    }

    private static IResult Created(CvDto cv) => Results.CreatedAtRoute("GetCv", new { id = cv.Document.Id }, cv);
}
