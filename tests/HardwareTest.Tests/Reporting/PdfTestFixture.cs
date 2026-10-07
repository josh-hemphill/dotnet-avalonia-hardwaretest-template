using System.Globalization;
using System.Text;

namespace HardwareTest.Tests.Reporting;

internal static class PdfTestFixture
{
    internal static byte[] CreateMinimalPdf(string title = "Certification", bool indirectKids = false)
    {
        var content = $"BT /F1 12 Tf 72 720 Td ({SanitizeAscii(title)}) Tj ET";
        var contentBytes = Encoding.ASCII.GetBytes(content);
        var pagesKids = indirectKids ? "5 0 R" : "[3 0 R]";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids {pagesKids} /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents 4 0 R >>",
            $"<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{content}\nendstream",
        };
        if (indirectKids)
        {
            objects.Add("[3 0 R]");
        }

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new int[objects.Count + 1];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = builder.Length;
            builder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(" 0 obj\n");
            builder.Append(objects[i]);
            builder.Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ");
        builder.Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append('\n');
        builder.Append("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Count; i++)
        {
            builder.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture));
            builder.Append(" 00000 n \n");
        }

        builder.Append("trailer << /Size ");
        builder.Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(" /Root 1 0 R >>\nstartxref\n");
        builder.Append(xref.ToString(CultureInfo.InvariantCulture));
        builder.Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string SanitizeAscii(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= ' ' and <= '~' && ch is not '(' and not ')' and not '\\')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

}
