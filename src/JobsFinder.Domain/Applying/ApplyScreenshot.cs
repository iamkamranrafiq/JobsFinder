using JobsFinder.Domain.Common;

namespace JobsFinder.Domain.Applying;

/// <summary>
/// A full-page picture of one filled form step (F0.3-AC1). Personal data: kept in its own table so that reading a run
/// never loads it, and deleted together with the run.
/// </summary>
public sealed class ApplyScreenshot
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;
    public const int ContentTypeMaxLength = 50;
    public const string JpegContentType = "image/jpeg";

    // Required by EF Core; real instances go through Create.
    private ApplyScreenshot()
    {
    }

    public Guid Id { get; private set; }
    public Guid ApplyRunId { get; private set; }
    public int Step { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public int SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = [];

    public static ApplyScreenshot Create(Guid applyRunId, int step, byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        if (applyRunId == Guid.Empty || step < 1)
        {
            throw new DomainException("A screenshot belongs to a step of an apply run.");
        }

        if (jpeg.Length is 0 or > MaxSizeBytes)
        {
            throw new DomainException("A screenshot must have content, at most 5 MB.");
        }

        return new ApplyScreenshot
        {
            Id = Guid.CreateVersion7(),
            ApplyRunId = applyRunId,
            Step = step,
            ContentType = JpegContentType,
            SizeBytes = jpeg.Length,
            Content = jpeg,
        };
    }
}
