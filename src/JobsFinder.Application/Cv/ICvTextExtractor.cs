using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <summary>
/// Detects the file type from its content (never the extension) and extracts plain text.
/// Implemented in Infrastructure.
/// </summary>
public interface ICvTextExtractor
{
    Task<CvTextExtraction> ExtractAsync(byte[] content, CancellationToken cancellationToken);
}

/// <summary>Why a file was rejected before parsing (F0.1-AC3).</summary>
public enum CvRejection
{
    UnsupportedType,
    PasswordProtected,
    NoText,
    Corrupt,
}

public sealed record CvTextExtraction
{
    private CvTextExtraction(CvFormat? format, string? text, CvRejection? rejection)
    {
        Format = format;
        Text = text;
        Rejection = rejection;
    }

    public CvFormat? Format { get; }
    public string? Text { get; }
    public CvRejection? Rejection { get; }
    public bool IsSuccess => Rejection is null;

    public static CvTextExtraction Success(CvFormat format, string text) => new(format, text, null);

    public static CvTextExtraction Rejected(CvRejection reason) => new(null, null, reason);
}
