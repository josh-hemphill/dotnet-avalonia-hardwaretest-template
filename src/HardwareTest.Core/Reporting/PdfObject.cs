using System.Globalization;
using System.Text;

namespace HardwareTest.Core.Reporting;

/// PDF value reconstructed when rewriting catalog/page objects.
internal abstract record PdfObject
{
    public sealed record Null : PdfObject;
    public sealed record Bool(bool Value) : PdfObject;
    public sealed record Numeric(double Value) : PdfObject;
    public sealed record Name(string Value) : PdfObject;
    public sealed record String(string Value, bool Hex) : PdfObject;
    public sealed record Ref(int Number, int Generation) : PdfObject;
    public sealed record Array(IReadOnlyList<PdfObject> Items) : PdfObject;
    public sealed record Dict(IReadOnlyDictionary<string, PdfObject> Entries) : PdfObject;

    public static PdfObject.Numeric Int(int value) => new(value);

    public bool TryGetInt(out int value)
    {
        if (this is Numeric number && number.Value is >= int.MinValue and <= int.MaxValue
            && Math.Abs(number.Value - Math.Round(number.Value)) < 0.0000001)
        {
            value = (int)Math.Round(number.Value);
            return true;
        }

        value = 0;
        return false;
    }

    public PdfObject.Ref? AsRef() => this as Ref;

    public PdfObject.Dict? AsDict() => this as Dict;

    public PdfObject.Array? AsArray() => this as Array;

    public PdfObject? Get(string name)
        => this is Dict dict && dict.Entries.TryGetValue(name, out var value) ? value : null;

    public string? NameValue() => this is Name name ? name.Value : null;
}

/// Writes reconstructed PDF objects as ASCII.
internal static class PdfObjectWriter
{
    public static void Write(StringBuilder builder, PdfObject value)
    {
        switch (value)
        {
            case PdfObject.Null:
                builder.Append("null");
                return;
            case PdfObject.Bool flag:
                builder.Append(flag.Value ? "true" : "false");
                return;
            case PdfObject.Numeric number:
                if (number.Value is >= long.MinValue and <= long.MaxValue
                    && Math.Abs(number.Value - Math.Round(number.Value)) < 0.0000001)
                {
                    builder.Append(((long)Math.Round(number.Value)).ToString(CultureInfo.InvariantCulture));
                    return;
                }

                builder.Append(number.Value.ToString("G15", CultureInfo.InvariantCulture));
                return;
            case PdfObject.Name name:
                builder.Append('/');
                builder.Append(name.Value);
                return;
            case PdfObject.String text when text.Hex:
                builder.Append('<');
                builder.Append(text.Value);
                builder.Append('>');
                return;
            case PdfObject.String text:
                builder.Append('(');
                builder.Append(text.Value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("(", "\\(", StringComparison.Ordinal)
                    .Replace(")", "\\)", StringComparison.Ordinal));
                builder.Append(')');
                return;
            case PdfObject.Ref reference:
                builder.Append(reference.Number.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
                builder.Append(reference.Generation.ToString(CultureInfo.InvariantCulture));
                builder.Append(" R");
                return;
            case PdfObject.Array array:
                builder.Append('[');
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(' ');
                    }

                    Write(builder, array.Items[i]);
                }

                builder.Append(']');
                return;
            case PdfObject.Dict dict:
                builder.Append("<<");
                foreach (var (key, entry) in dict.Entries)
                {
                    builder.Append('/');
                    builder.Append(key);
                    builder.Append(' ');
                    Write(builder, entry);
                }

                builder.Append(">>");
                return;
            default:
                throw new InvalidOperationException("Unsupported PDF object.");
        }
    }
}
