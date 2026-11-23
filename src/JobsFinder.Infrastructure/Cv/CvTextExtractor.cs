using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace JobsFinder.Infrastructure.Cv;

/// <summary>
/// Detects PDF/DOCX by magic bytes (never by extension or client content type) and extracts plain text
/// with PdfPig and the Open XML SDK. Extracted text is never logged (it is personal data).
/// </summary>
internal sealed class CvTextExtractor : ICvTextExtractor
{
    /// <summary>Below this many non-whitespace characters the file is treated as scanned/image-only.</summary>
    public const int MinTextCharacters = 50;

    /// <summary>Guards against zip bombs: a DOCX main part larger than this when inflated is refused.</summary>
    private const long MaxDocumentXmlBytes = 50L * 1024 * 1024;

    private const string DocumentXmlEntry = "word/document.xml";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    // OLE compound file: Office stores encrypted DOCX this way (legacy .doc shares it).
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public Task<CvTextExtraction> ExtractAsync(byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        // Parsing is CPU-bound on an in-memory buffer; the async signature lets an out-of-process
        // extractor replace this one later without changing callers.
        var result = Detect(content) switch
        {
            DetectedType.Pdf => ExtractPdf(content, cancellationToken),
            DetectedType.Zip => ExtractDocx(content, cancellationToken),
            DetectedType.Ole => CvTextExtraction.Rejected(CvRejection.PasswordProtected),
            _ => CvTextExtraction.Rejected(CvRejection.UnsupportedType),
        };

        return Task.FromResult(result);
    }

    private static DetectedType Detect(byte[] content)
    {
        var span = content.AsSpan();
        if (span.StartsWith(OleSignature))
        {
            return DetectedType.Ole;
        }

        if (span.StartsWith(ZipSignature))
        {
            return DetectedType.Zip;
        }

        // The PDF spec tolerates a little junk before the header; look in the first 1 KB.
        var head = span[..Math.Min(span.Length, 1024)];
        return head.IndexOf(PdfSignature) >= 0 ? DetectedType.Pdf : DetectedType.Unknown;
    }

    private static CvTextExtraction ExtractPdf(byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.AppendLine(page.Text);
            }

            return ToResult(CvFormat.Pdf, text.ToString());
        }
        catch (PdfDocumentEncryptedException)
        {
            return CvTextExtraction.Rejected(CvRejection.PasswordProtected);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // PdfPig throws a range of exception types for damaged files.
            return CvTextExtraction.Rejected(CvRejection.Corrupt);
        }
    }

    private static CvTextExtraction ExtractDocx(byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using (var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry(DocumentXmlEntry);
                if (entry is null)
                {
                    // A zip, but not a Word document (e.g. an .xlsx or a plain archive).
                    return CvTextExtraction.Rejected(CvRejection.UnsupportedType);
                }

                if (entry.Length > MaxDocumentXmlBytes)
                {
                    return CvTextExtraction.Rejected(CvRejection.Corrupt);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new MemoryStream(content, writable: false);
            using var document = WordprocessingDocument.Open(stream, isEditable: false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return CvTextExtraction.Rejected(CvRejection.NoText);
            }

            var text = new StringBuilder();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                text.AppendLine(paragraph.InnerText);
            }

            return ToResult(CvFormat.Docx, text.ToString());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return CvTextExtraction.Rejected(CvRejection.Corrupt);
        }
    }

    private static CvTextExtraction ToResult(CvFormat format, string text)
    {
        var meaningful = 0;
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && ++meaningful >= MinTextCharacters)
            {
                return CvTextExtraction.Success(format, text.Trim());
            }
        }

        return CvTextExtraction.Rejected(CvRejection.NoText);
    }

    private enum DetectedType
    {
        Unknown,
        Pdf,
        Zip,
        Ole,
    }
}
