namespace JobsFinder.Domain.Common;

/// <summary>Thrown when an entity invariant is violated. The API maps it to 400.</summary>
public sealed class DomainException(string message) : Exception(message);
