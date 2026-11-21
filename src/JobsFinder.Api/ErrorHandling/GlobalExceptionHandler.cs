using JobsFinder.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace JobsFinder.Api.ErrorHandling;

/// <summary>
/// Last-resort handler. DomainException becomes 400; anything else is logged and becomes a generic 500
/// so stack traces never reach clients.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DomainException e => (StatusCodes.Status400BadRequest, e.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "The request was malformed."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
