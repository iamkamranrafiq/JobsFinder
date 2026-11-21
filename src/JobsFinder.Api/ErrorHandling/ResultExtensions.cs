using JobsFinder.Application.Common;

namespace JobsFinder.Api.ErrorHandling;

internal static class ResultExtensions
{
    /// <summary>Maps an Application Result to an HTTP response (RFC 9457 problem details on failure).</summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value!);
        }

        var error = result.Error!;
        return error.Type switch
        {
            ErrorType.Validation => Results.ValidationProblem(
                error.Details!.ToDictionary(),
                title: error.Message,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
            ErrorType.NotFound => Problem(StatusCodes.Status404NotFound, error),
            ErrorType.Conflict => Problem(StatusCodes.Status409Conflict, error),
            _ => throw new InvalidOperationException($"Unhandled error type {error.Type}."),
        };
    }

    private static IResult Problem(int status, Error error) =>
        Results.Problem(
            statusCode: status,
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
