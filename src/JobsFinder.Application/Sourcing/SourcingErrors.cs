using JobsFinder.Application.Common;

namespace JobsFinder.Application.Sourcing;

public static class SourcingErrors
{
    public static readonly Error SourceUnavailable = Error.Unavailable("source_unavailable", "Source unavailable");

    public static readonly Error FetchInProgress = Error.Conflict("fetch_in_progress", "A job fetch is already running.");
}
