using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HardwareTest.Core.Credentials;

namespace HardwareTest.Core.Reporting;

/// ISO 32000 incremental PAdES-B-B (`Adobe.PPKLite` / `adbe.pkcs7.detached`).
public static class PdfPadesSignature
{
    public const string Filter = "Adobe.PPKLite";
    public const string SubFilter = "adbe.pkcs7.detached";
    public const string Reason = "Certification of hardware test report";
    public const string FieldName = "HardwareTestCertification";

    internal const int ReservedCmsBytes = 8192;
    private const int ByteRangeWidth = 10;

    /// Builds an incremental signature placeholder; Contents is zero-filled until Embed.
    public static bool TryPrepare(
        ReadOnlySpan<byte> pdf,
        string signerName,
        DateTimeOffset signingTime,
        out PreparedPdfSignature prepared,
        out string? error)
    {
        prepared = null!;
        var original = pdf.ToArray();
        if (!PdfDocumentReader.TryOpen(original, out var reader, out error))
        {
            return false;
        }

        var pageRef = reader.FirstPageRef();
        if (pageRef is null || !reader.TryGetDict(pageRef, out var page, out error))
        {
            error ??= "PDF has no page to attach a signature field.";
            return false;
        }

        if (!reader.TryGetDict(reader.Root, out var catalog, out error))
        {
            return false;
        }

        var size = reader.Size;
        var sigNum = size;
        var fieldNum = size + 1;
        var acroNum = catalog.Get("AcroForm")?.AsRef()?.Number ?? size + 2;
        var added = catalog.Get("AcroForm")?.AsRef() is null ? 3 : 2;
        var newSize = size + added;

        var pageEntries = new Dictionary<string, PdfObject>(page.Entries, StringComparer.Ordinal);
        var annots = new List<PdfObject>();
        if (page.Get("Annots")?.AsArray() is { } existing)
        {
            annots.AddRange(existing.Items);
        }
        else if (page.Get("Annots")?.AsRef() is { } annotsRef
                 && reader.TryGet(annotsRef.Number, out var annotsObj, out _)
                 && annotsObj.AsArray() is { } referenced)
        {
            annots.AddRange(referenced.Items);
        }

        annots.Add(new PdfObject.Ref(fieldNum, 0));
        pageEntries["Annots"] = new PdfObject.Array(annots);
        var newPage = new PdfObject.Dict(pageEntries);

        PdfObject.Dict acroForm;
        if (catalog.Get("AcroForm")?.AsRef() is { } acroRef
            && reader.TryGetDict(acroRef, out var existingAcro, out _))
        {
            var acroEntries = new Dictionary<string, PdfObject>(existingAcro.Entries, StringComparer.Ordinal);
            var fields = new List<PdfObject>();
            if (existingAcro.Get("Fields")?.AsArray() is { } existingFields)
            {
                fields.AddRange(existingFields.Items);
            }

            fields.Add(new PdfObject.Ref(fieldNum, 0));
            acroEntries["Fields"] = new PdfObject.Array(fields);
            acroEntries["SigFlags"] = PdfObject.Int(3);
            acroForm = new PdfObject.Dict(acroEntries);
            acroNum = acroRef.Number;
        }
        else
        {
            acroForm = new PdfObject.Dict(new Dictionary<string, PdfObject>(StringComparer.Ordinal)
            {
                ["Fields"] = new PdfObject.Array([new PdfObject.Ref(fieldNum, 0)]),
                ["SigFlags"] = PdfObject.Int(3),
            });
        }

        var catalogEntries = new Dictionary<string, PdfObject>(catalog.Entries, StringComparer.Ordinal)
        {
            ["AcroForm"] = new PdfObject.Ref(acroNum, 0),
        };
        var newCatalog = new PdfObject.Dict(catalogEntries);

        var field = new PdfObject.Dict(new Dictionary<string, PdfObject>(StringComparer.Ordinal)
        {
            ["Type"] = new PdfObject.Name("Annot"),
            ["Subtype"] = new PdfObject.Name("Widget"),
            ["FT"] = new PdfObject.Name("Sig"),
            ["T"] = new PdfObject.String(FieldName, Hex: false),
            ["F"] = PdfObject.Int(4),
            ["P"] = new PdfObject.Ref(pageRef.Number, pageRef.Generation),
            ["Rect"] = new PdfObject.Array(
            [
                PdfObject.Int(0),
                PdfObject.Int(0),
                PdfObject.Int(0),
                PdfObject.Int(0),
            ]),
            ["V"] = new PdfObject.Ref(sigNum, 0),
        });

        var hexContents = new string('0', ReservedCmsBytes * 2);
        var pdfDate = $"(D:{signingTime.UtcDateTime:yyyyMMddHHmmss}Z)";
        var nameLiteral = ToPdfText(signerName);

        using var buffer = new MemoryStream();
        buffer.Write(original);
        if (original.Length == 0 || original[^1] is not (byte)'\n' and not (byte)'\r')
        {
            buffer.WriteByte((byte)'\n');
        }

        var offsets = new Dictionary<int, int>();
        WriteObj(buffer, offsets, sigNum, null);
        var sigStart = (int)buffer.Length;
        var sigPrefix =
            $"<< /Type /Sig /Filter /{Filter} /SubFilter /{SubFilter} /ByteRange [0 0000000000 0000000000 0000000000] /Contents <";
        var prefixBytes = Encoding.ASCII.GetBytes(sigPrefix);
        buffer.Write(prefixBytes);
        var contentsHexStart = (int)buffer.Length;
        buffer.Write(Encoding.ASCII.GetBytes(hexContents));
        var contentsHexEnd = (int)buffer.Length;
        var sigSuffix =
            $"> /M {pdfDate} /Name {nameLiteral} /Reason ({Reason}) >>\nendobj\n";
        buffer.Write(Encoding.ASCII.GetBytes(sigSuffix));
        _ = sigStart;

        WriteObj(buffer, offsets, fieldNum, field);
        WriteObj(buffer, offsets, acroNum, acroForm);
        WriteObj(buffer, offsets, pageRef.Number, newPage);
        WriteObj(buffer, offsets, reader.Root.Number, newCatalog);

        var xrefPos = (int)buffer.Length;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        AppendXrefSection(xref, offsets);
        xref.Append("trailer\n<< /Size ");
        xref.Append(newSize.ToString(CultureInfo.InvariantCulture));
        xref.Append(" /Root ");
        xref.Append(reader.Root.Number.ToString(CultureInfo.InvariantCulture));
        xref.Append(' ');
        xref.Append(reader.Root.Generation.ToString(CultureInfo.InvariantCulture));
        xref.Append(" R /Prev ");
        xref.Append(reader.StartXref.ToString(CultureInfo.InvariantCulture));
        if (reader.Info is { } info)
        {
            xref.Append(" /Info ");
            xref.Append(info.Number.ToString(CultureInfo.InvariantCulture));
            xref.Append(' ');
            xref.Append(info.Generation.ToString(CultureInfo.InvariantCulture));
            xref.Append(" R");
        }

        if (reader.Id is not null)
        {
            xref.Append(" /ID ");
            PdfObjectWriter.Write(xref, reader.Id);
        }

        xref.Append(" >>\nstartxref\n");
        xref.Append(xrefPos.ToString(CultureInfo.InvariantCulture));
        xref.Append("\n%%EOF\n");
        buffer.Write(Encoding.ASCII.GetBytes(xref.ToString()));

        var document = buffer.ToArray();
        var length1 = contentsHexStart - 1;
        var start2 = contentsHexEnd + 1;
        var length2 = document.Length - start2;
        PatchByteRange(document, length1, start2, length2);
        prepared = new PreparedPdfSignature(document, contentsHexStart, ReservedCmsBytes * 2, length1, start2, length2);
        error = null;
        return true;
    }

    /// True when the last PDF signature verifies as detached CMS over its ByteRange.
    public static bool TryVerify(ReadOnlySpan<byte> pdf, out string? error)
    {
        if (!TryReadByteRange(pdf, out var signedBytes, out var cms, out error))
        {
            return false;
        }

        if (!PivCmsSigner.Verify(signedBytes, cms))
        {
            error = "Embedded CMS signature did not verify.";
            return false;
        }

        error = null;
        return true;
    }

    /// Concatenated ByteRange bytes and CMS from the last `/Contents` hex string.
    public static bool TryReadByteRange(
        ReadOnlySpan<byte> pdf,
        out byte[] signedBytes,
        out byte[] cms,
        out string? error)
    {
        signedBytes = [];
        cms = [];
        var data = pdf.ToArray();
        var marker = Encoding.ASCII.GetBytes("/ByteRange");
        var index = LastIndexOf(data, marker);
        if (index < 0)
        {
            error = "PDF has no signature ByteRange.";
            return false;
        }

        var cursor = index + marker.Length;
        while (cursor < data.Length && data[cursor] <= 32)
        {
            cursor++;
        }

        if (cursor >= data.Length || data[cursor] != (byte)'[')
        {
            error = "PDF ByteRange is not an array.";
            return false;
        }

        cursor++;
        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            while (cursor < data.Length && data[cursor] <= 32)
            {
                cursor++;
            }

            var start = cursor;
            while (cursor < data.Length && data[cursor] > 32 && data[cursor] != (byte)']')
            {
                cursor++;
            }

            if (!int.TryParse(
                    Encoding.ASCII.GetString(data, start, cursor - start),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out values[i]))
            {
                error = "PDF ByteRange values are invalid.";
                return false;
            }
        }

        var contentsMarker = Encoding.ASCII.GetBytes("/Contents");
        var contentsAt = IndexOf(data, contentsMarker, index);
        if (contentsAt < 0)
        {
            error = "PDF signature Contents is missing.";
            return false;
        }

        var hexAt = contentsAt + contentsMarker.Length;
        while (hexAt < data.Length && data[hexAt] <= 32)
        {
            hexAt++;
        }

        if (hexAt >= data.Length || data[hexAt] != (byte)'<')
        {
            error = "PDF signature Contents is not a hex string.";
            return false;
        }

        hexAt++;
        var hexEnd = hexAt;
        while (hexEnd < data.Length && data[hexEnd] != (byte)'>')
        {
            hexEnd++;
        }

        var hex = Encoding.ASCII.GetString(data, hexAt, hexEnd - hexAt).Replace(" ", "", StringComparison.Ordinal);
        if (hex.Length % 2 == 1)
        {
            hex += "0";
        }

        if (hex.Length < 2)
        {
            error = "PDF signature Contents is empty.";
            return false;
        }

        byte[] padded;
        try
        {
            padded = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            error = "PDF signature Contents is not hex.";
            return false;
        }

        try
        {
            AsnDecoder.ReadEncodedValue(padded, AsnEncodingRules.BER, out _, out _, out var consumed);
            cms = padded[..consumed];
        }
        catch (AsnContentException)
        {
            error = "PDF signature Contents is not a CMS object.";
            return false;
        }

        var length1 = values[1];
        var start2 = values[2];
        var length2 = values[3];
        if (length1 < 0 || start2 < 0 || length2 < 0
            || length1 > data.Length
            || start2 + length2 > data.Length)
        {
            error = "PDF ByteRange is out of bounds.";
            return false;
        }

        signedBytes = new byte[length1 + length2];
        data.AsSpan(0, length1).CopyTo(signedBytes);
        data.AsSpan(start2, length2).CopyTo(signedBytes.AsSpan(length1));
        error = null;
        return true;
    }

    /// Minimal one-page PDF used by tests and attestation seeds.
    internal static byte[] CreateMinimalPdf(string title = "Certification")
    {
        var content = $"BT /F1 12 Tf 72 720 Td ({SanitizeAscii(title)}) Tj ET";
        var contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents 4 0 R >>",
            $"<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{content}\nendstream",
        };

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new int[objects.Length + 1];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = builder.Length;
            builder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(" 0 obj\n");
            builder.Append(objects[i]);
            builder.Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ");
        builder.Append((objects.Length + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append('\n');
        builder.Append("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Length; i++)
        {
            builder.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture));
            builder.Append(" 00000 n \n");
        }

        builder.Append("trailer << /Size ");
        builder.Append((objects.Length + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(" /Root 1 0 R >>\nstartxref\n");
        builder.Append(xref.ToString(CultureInfo.InvariantCulture));
        builder.Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void WriteObj(MemoryStream buffer, Dictionary<int, int> offsets, int number, PdfObject.Dict? dict)
    {
        offsets[number] = (int)buffer.Length;
        if (dict is null)
        {
            var header = Encoding.ASCII.GetBytes($"{number.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
            buffer.Write(header);
            return;
        }

        var builder = new StringBuilder();
        builder.Append(number.ToString(CultureInfo.InvariantCulture));
        builder.Append(" 0 obj\n");
        PdfObjectWriter.Write(builder, dict);
        builder.Append("\nendobj\n");
        buffer.Write(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    private static void AppendXrefSection(StringBuilder xref, Dictionary<int, int> offsets)
    {
        var numbers = offsets.Keys.OrderBy(n => n).ToArray();
        var i = 0;
        while (i < numbers.Length)
        {
            var start = numbers[i];
            var count = 1;
            while (i + count < numbers.Length && numbers[i + count] == start + count)
            {
                count++;
            }

            xref.Append(start.ToString(CultureInfo.InvariantCulture));
            xref.Append(' ');
            xref.Append(count.ToString(CultureInfo.InvariantCulture));
            xref.Append('\n');
            for (var n = 0; n < count; n++)
            {
                xref.Append(offsets[start + n].ToString("D10", CultureInfo.InvariantCulture));
                xref.Append(" 00000 n \n");
            }

            i += count;
        }
    }

    private static void PatchByteRange(byte[] document, int length1, int start2, int length2)
    {
        var marker = Encoding.ASCII.GetBytes("/ByteRange [0 ");
        var at = IndexOf(document, marker, 0);
        while (true)
        {
            var next = IndexOf(document, marker, at + 1);
            if (next < 0)
            {
                break;
            }

            at = next;
        }

        if (at < 0)
        {
            throw new InvalidOperationException("Prepared PDF is missing /ByteRange.");
        }

        var fieldStart = at + marker.Length;
        var formatted = string.Join(
            " ",
            length1.ToString().PadLeft(ByteRangeWidth),
            start2.ToString().PadLeft(ByteRangeWidth),
            length2.ToString().PadLeft(ByteRangeWidth));
        var field = Encoding.ASCII.GetBytes(formatted);
        if (field.Length != (ByteRangeWidth * 3) + 2)
        {
            throw new InvalidOperationException("ByteRange patch width mismatch.");
        }

        field.CopyTo(document.AsSpan(fieldStart));
    }

    private static string ToPdfText(string value)
    {
        var ascii = SanitizeAscii(value);
        return ascii.Length == 0 ? "(PIV)" : $"({ascii})";
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

    private static int IndexOf(byte[] data, byte[] needle, int start)
    {
        var last = data.Length - needle.Length;
        for (var i = Math.Max(0, start); i <= last; i++)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastIndexOf(byte[] data, byte[] needle)
    {
        for (var i = data.Length - needle.Length; i >= 0; i--)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}

/// Placeholder PDF plus Contents/ByteRange offsets for CMS injection.
public sealed class PreparedPdfSignature
{
    internal PreparedPdfSignature(
        byte[] document,
        int contentsHexStart,
        int contentsHexLength,
        int length1,
        int start2,
        int length2)
    {
        Document = document;
        ContentsHexStart = contentsHexStart;
        ContentsHexLength = contentsHexLength;
        SignedBytes = new byte[length1 + length2];
        document.AsSpan(0, length1).CopyTo(SignedBytes);
        document.AsSpan(start2, length2).CopyTo(SignedBytes.AsSpan(length1));
    }

    public byte[] Document { get; }
    public byte[] SignedBytes { get; }
    internal int ContentsHexStart { get; }
    internal int ContentsHexLength { get; }

    /// Writes CMS into Contents (zero-padded) without changing ByteRange coverage.
    public bool TryEmbed(byte[] cms, out byte[] signedPdf, out string? error)
    {
        signedPdf = [];
        if (cms.Length == 0)
        {
            error = "CMS signature is empty.";
            return false;
        }

        if (cms.Length > PdfPadesSignature.ReservedCmsBytes)
        {
            error = "CMS signature is larger than the reserved PDF Contents slot.";
            return false;
        }

        var hex = Convert.ToHexString(cms);
        if (hex.Length > ContentsHexLength)
        {
            error = "CMS hex does not fit in the reserved PDF Contents slot.";
            return false;
        }

        signedPdf = (byte[])Document.Clone();
        var padded = hex.PadRight(ContentsHexLength, '0');
        Encoding.ASCII.GetBytes(padded).CopyTo(signedPdf.AsSpan(ContentsHexStart));
        error = null;
        return true;
    }
}
