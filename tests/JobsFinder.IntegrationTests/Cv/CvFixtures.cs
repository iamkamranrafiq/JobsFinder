using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PageSize = UglyToad.PdfPig.Content.PageSize;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>Builds CV test files in memory so no binary fixtures need to be committed.</summary>
internal static class CvFixtures
{
    public static readonly string[] CvLines =
    [
        "Ada Lovelace",
        "Senior .NET Engineer - London, UK",
        "ada@example.com | +44 20 7946 0000 | https://github.com/ada",
        "Experience: Analytical Engines Ltd, Senior Engineer, 2019 - present",
        "Skills: C#, ASP.NET Core, Angular, PostgreSQL",
    ];

    // 1x1 transparent PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static byte[] PngImage() => [.. Png];

    public static byte[] TextPdf()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 780d;
        foreach (var line in CvLines)
        {
            page.AddText(line, 11, new PdfPoint(40, y), font);
            y -= 20;
        }

        return builder.Build();
    }

    /// <summary>A PDF whose only content is an image, like a scanned CV.</summary>
    public static byte[] ImageOnlyPdf()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddPng(Png, new PdfRectangle(40, 40, 500, 760));
        return builder.Build();
    }

    public static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(CvLines.Select(l => new Paragraph(new Run(new Text(l))))));
        }

        return stream.ToArray();
    }

    /// <summary>The OLE compound-file header Office uses for encrypted DOCX files.</summary>
    public static byte[] EncryptedDocx()
    {
        var bytes = new byte[4096];
        byte[] header = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        header.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>A zip that is not a Word document.</summary>
    public static byte[] PlainZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open());
            writer.Write("hello");
        }

        return stream.ToArray();
    }

    public static byte[] PlainText() => Encoding.UTF8.GetBytes(string.Join('\n', CvLines));

    /// <summary>
    /// A minimal PDF with a Standard security handler whose user password is not empty, so it cannot be
    /// opened without a password. Offsets are computed so the cross-reference table is valid.
    /// </summary>
    public static byte[] PasswordProtectedPdf()
    {
        var hash = new string('A', 64);
        var id = new string('B', 32);
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            $"<< /Filter /Standard /V 1 /R 2 /O <{hash}> /U <{hash}> /P -44 >>",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Encrypt 4 0 R /ID [<{id}><{id}>] >>\n");
        pdf.Append(CultureInfo.InvariantCulture, $"startxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>A PDF-signed payload of the given size (content is irrelevant for size checks).</summary>
    public static byte[] OfSize(int size)
    {
        var bytes = new byte[size];
        "%PDF-1.4\n"u8.CopyTo(bytes);
        return bytes;
    }
}
