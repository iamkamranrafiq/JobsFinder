using JobsFinder.Application.Common;

namespace JobsFinder.Application.Cv;

/// <summary>Expected CV upload failures. Each has a specific code and a user-facing title (F0.1-AC3, BR4).</summary>
public static class CvErrors
{
    public static readonly Error TooLarge =
        Error.Unprocessable("cv_too_large", "The file is larger than 5 MB.");

    public static readonly Error UnsupportedType =
        Error.Unprocessable("cv_unsupported_type", "Only PDF or DOCX files are accepted.");

    public static readonly Error PasswordProtected = Error.Unprocessable(
        "cv_password_protected",
        "This file is password-protected or in an old Word format. Save it as an unprotected DOCX or PDF and upload it again.");

    public static readonly Error NoText = Error.Unprocessable(
        "cv_no_text",
        "No text could be found in this file. It looks like a scanned or image-only document; upload a text-based PDF or DOCX.");

    public static readonly Error Corrupt =
        Error.Unprocessable("cv_corrupt", "This file is damaged and could not be opened. Try another copy or format.");

    /// <summary>Exact wording from the F0.1 edge case for repeated invalid model output.</summary>
    public static readonly Error Unreadable =
        Error.Unprocessable("cv_unreadable", "Could not read this CV; try another format");

    public static readonly Error ParseTimeout =
        Error.Unprocessable("cv_parse_timeout", "Reading this CV took too long. Try again or upload a shorter file.");

    public static readonly Error AIUnavailable =
        Error.Unprocessable("cv_ai_unavailable", "The CV reader is unavailable right now. Try again later.");

    /// <summary>F0.1-BR3: the profile gained a newer version after the candidate opened it.</summary>
    public static readonly Error ProfileVersionConflict = Error.Conflict(
        "profile_version_conflict",
        "This profile was changed since you opened it. Reload it and make your changes again.");

    public static readonly Error NoActiveProfile =
        Error.NotFound("profile_not_found", "No CV has been uploaded yet.");

    public static Error NotFound(Guid id) => Error.NotFound("cv_not_found", $"CV '{id}' was not found.");

    public static Error FromRejection(CvRejection rejection) => rejection switch
    {
        CvRejection.UnsupportedType => UnsupportedType,
        CvRejection.PasswordProtected => PasswordProtected,
        CvRejection.NoText => NoText,
        CvRejection.Corrupt => Corrupt,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, null),
    };
}
