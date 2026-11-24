namespace JobsFinder.Domain.Sourcing;

/// <summary>Why a fetch from a job source failed (F0.2-AC4).</summary>
public enum FetchFailureReason
{
    /// <summary>Token, actor or field mapping is missing or unusable.</summary>
    NotConfigured = 0,

    /// <summary>The source rejected the token.</summary>
    Unauthorized = 1,

    /// <summary>The source could not be reached, refused the request or answered with something unreadable.</summary>
    Unreachable = 2,

    /// <summary>The run started but ended without success.</summary>
    ActorFailed = 3,

    /// <summary>The run did not finish in time.</summary>
    TimedOut = 4,
}
