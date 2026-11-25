namespace JobsFinder.Domain.Applying;

/// <summary>Why a run is not <see cref="ApplyRunStatus.Prepared"/>.</summary>
public enum ApplyReason
{
    /// <summary>NeedsYou: a required field has no answer in the profile.</summary>
    UnknownField = 0,

    /// <summary>NeedsYou: every required field is answered but the CV could not be attached.</summary>
    CvNotAttached = 1,

    /// <summary>The page did not load, after all retries where retrying makes sense.</summary>
    PageLoad = 2,

    /// <summary>The apply URL, or where it redirects to, is not a public Greenhouse board.</summary>
    UrlNotAllowed = 3,

    /// <summary>The page loaded but has no application form the adapter understands.</summary>
    FormNotRecognised = 4,

    /// <summary>The next step of a multi-step form could not be reached.</summary>
    StepNotReached = 5,

    /// <summary>The screenshot could not be taken or is too large.</summary>
    Screenshot = 6,

    TimedOut = 7,

    /// <summary>No browser could be started.</summary>
    BrowserUnavailable = 8,

    /// <summary>The browser stopped working during the run.</summary>
    BrowserError = 9,
}
