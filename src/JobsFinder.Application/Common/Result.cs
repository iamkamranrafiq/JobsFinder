namespace JobsFinder.Application.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,

    /// <summary>The request was well-formed but its content cannot be processed (HTTP 422).</summary>
    Unprocessable,

    /// <summary>Something the application depends on is not available right now (HTTP 503).</summary>
    Unavailable,
}

public sealed record Error(string Code, string Message, ErrorType Type, IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unprocessable(string code, string message) => new(code, message, ErrorType.Unprocessable);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);

    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation_failed", "One or more validation errors occurred.", ErrorType.Validation, details);
}

/// <summary>
/// Handlers return Result for expected failures (not found, validation, conflict).
/// Exceptions are reserved for bugs and infrastructure failures.
/// </summary>
public sealed class Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
