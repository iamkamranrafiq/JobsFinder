namespace JobsFinder.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when a save breaks a unique key, e.g. two saves racing
/// for the same profile version. Keeps Application free of database-specific exception types. Handlers that
/// expect such a race catch it and return a conflict <c>Result</c>.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException()
    {
    }

    public UniqueConstraintViolationException(string message)
        : base(message)
    {
    }

    public UniqueConstraintViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? ConstraintName { get; init; }
}
