using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace HardwareTest.Core.Reporting;

/// Reads trailer, xref (table or stream), and indirect objects from a PDF.
internal sealed class PdfDocumentReader
{
    private readonly byte[] _data;
    private readonly Dictionary<int, XRefEntry> _xref = [];
    private readonly Dictionary<int, PdfObject> _cache = [];
    private readonly Dictionary<int, byte[]> _objectStreams = [];
    private readonly HashSet<int> _visitedXrefs = [];

    private PdfDocumentReader(byte[] data)
    {
        _data = data;
    }

    public int Size { get; private set; }
    public int StartXref { get; private set; }
    public PdfObject.Ref Root { get; private set; } = new(0, 0);
    public PdfObject.Ref? Info { get; private set; }
    public PdfObject? Id { get; private set; }

    public static bool TryOpen(byte[] data, out PdfDocumentReader reader, out string? error)
    {
        reader = new PdfDocumentReader(data);
        if (data.Length < 8 || data[0] != (byte)'%' || data[1] != (byte)'P' || data[2] != (byte)'D' || data[3] != (byte)'F')
        {
            error = "Not a PDF document.";
            return false;
        }

        if (!reader.TryReadTrailer(out error))
        {
            return false;
        }

        return true;
    }

    public bool TryGetDict(PdfObject.Ref reference, out PdfObject.Dict dict, out string? error)
    {
        if (!TryGet(reference.Number, out var value, out error) || value.AsDict() is not { } parsed)
        {
            dict = new PdfObject.Dict(new Dictionary<string, PdfObject>());
            error ??= $"Object {reference.Number} is not a dictionary.";
            return false;
        }

        dict = parsed;
        return true;
    }

    public bool TryGet(int number, out PdfObject value, out string? error)
    {
        if (_cache.TryGetValue(number, out var cached))
        {
            value = cached;
            error = null;
            return true;
        }

        if (!_xref.TryGetValue(number, out var entry) || entry.Free)
        {
            value = new PdfObject.Null();
            error = $"Missing object {number}.";
            return false;
        }

        if (entry.Compressed)
        {
            if (!TryReadCompressed(number, entry, out value, out error))
            {
                return false;
            }

            _cache[number] = value;
            return true;
        }

        if (!TryReadIndirect(entry.Offset, out value, out error))
        {
            return false;
        }

        _cache[number] = value;
        return true;
    }

    public PdfObject.Ref? FirstPageRef()
    {
        if (!TryGetDict(Root, out var catalog, out _))
        {
            return null;
        }

        if (catalog.Get("Pages")?.AsRef() is not { } pagesRef || !TryGetDict(pagesRef, out var pages, out _))
        {
            return null;
        }

        return FindFirstPage(pagesRef, pages);
    }

    internal PdfObject.Array? ResolveArray(PdfObject? value)
    {
        if (value?.AsArray() is { } array)
        {
            return array;
        }

        if (value?.AsRef() is { } reference
            && TryGet(reference.Number, out var obj, out _)
            && obj.AsArray() is { } referenced)
        {
            return referenced;
        }

        return null;
    }

    private PdfObject.Ref? FindFirstPage(PdfObject.Ref nodeRef, PdfObject.Dict node)
    {
        if (node.Get("Type")?.NameValue() == "Page")
        {
            return nodeRef;
        }

        var kids = ResolveArray(node.Get("Kids"));
        if (kids is null || kids.Items.Count == 0)
        {
            return null;
        }

        if (kids.Items[0].AsRef() is not { } first)
        {
            return null;
        }

        if (!TryGetDict(first, out var child, out _))
        {
            return first;
        }

        return FindFirstPage(first, child);
    }

    private bool TryReadTrailer(out string? error)
    {
        if (!TryFindStartXref(out var startXref, out error))
        {
            return false;
        }

        StartXref = startXref;
        return TryReadXrefAt(startXref, newest: true, out error);
    }

    private bool TryReadXrefAt(int offset, bool newest, out string? error)
    {
        if (offset < 0 || offset >= _data.Length)
        {
            error = "PDF xref offset is out of bounds.";
            return false;
        }

        if (!_visitedXrefs.Add(offset))
        {
            error = null;
            return true;
        }

        var cursor = offset;
        SkipWhitespaceAndComments(ref cursor);
        if (StartsWith(cursor, "xref"u8))
        {
            return TryReadClassicXref(ref cursor, newest, out error);
        }

        return TryReadXrefStreamAt(offset, newest, out error);
    }

    private bool TryWalkPrev(PdfObject.Dict trailer, out string? error)
    {
        if (trailer.Get("Prev") is null)
        {
            error = null;
            return true;
        }

        if (trailer.Get("Prev")?.TryGetInt(out var prev) != true)
        {
            error = "PDF trailer /Prev is invalid.";
            return false;
        }

        return TryReadXrefAt(prev, newest: false, out error);
    }

    private bool TryFindStartXref(out int startXref, out string? error)
    {
        startXref = 0;
        var needle = "startxref"u8;
        var from = _data.Length - 1;
        while (from >= 0)
        {
            var index = LastIndexOf(needle, from);
            if (index < 0)
            {
                error = "PDF is missing startxref.";
                return false;
            }

            var cursor = index + needle.Length;
            SkipWhitespaceAndComments(ref cursor);
            if (TryReadNumberToken(ref cursor, out var value) && value.TryGetInt(out startXref)
                && startXref >= 0 && startXref < _data.Length)
            {
                error = null;
                return true;
            }

            from = index - 1;
        }

        error = "PDF startxref is invalid.";
        return false;
    }

    private bool TryReadClassicXref(ref int cursor, bool newest, out string? error)
    {
        cursor += 4;
        SkipWhitespaceAndComments(ref cursor);
        while (cursor < _data.Length && char.IsDigit((char)_data[cursor]))
        {
            if (!TryReadNumberToken(ref cursor, out var firstObj) || !firstObj.TryGetInt(out var start)
                || !TryReadNumberToken(ref cursor, out var countObj) || !countObj.TryGetInt(out var count))
            {
                error = "PDF xref subsection is invalid.";
                return false;
            }

            SkipWhitespaceAndComments(ref cursor);
            for (var i = 0; i < count; i++)
            {
                if (cursor + 20 > _data.Length)
                {
                    error = "PDF xref entry is truncated.";
                    return false;
                }

                var line = Encoding.ASCII.GetString(_data, cursor, 20);
                cursor += 20;
                if (!int.TryParse(line.AsSpan(0, 10), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
                    || !int.TryParse(line.AsSpan(11, 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
                {
                    error = "PDF xref entry could not be parsed.";
                    return false;
                }

                var isFree = line.Length > 17 && line[17] == 'f';
                var number = start + i;
                if (!_xref.ContainsKey(number))
                {
                    _xref[number] = new XRefEntry(offset, generation, isFree, false, 0, 0);
                }
            }

            SkipWhitespaceAndComments(ref cursor);
        }

        SkipWhitespaceAndComments(ref cursor);
        if (!StartsWith(cursor, "trailer"u8))
        {
            error = "PDF trailer is missing.";
            return false;
        }

        cursor += 7;
        SkipWhitespaceAndComments(ref cursor);
        if (!TryReadValue(ref cursor, out var trailerValue, out error) || trailerValue.AsDict() is not { } trailer)
        {
            error ??= "PDF trailer is not a dictionary.";
            return false;
        }

        return ApplyTrailer(trailer, newest, out error) && TryWalkPrev(trailer, out error);
    }

    private bool TryReadXrefStreamAt(int offset, bool newest, out string? error)
    {
        if (!TryReadIndirectObject(offset, out var number, out var dict, out var stream, out error))
        {
            return false;
        }

        _ = number;
        if (dict.Get("Type")?.NameValue() != "XRef" || stream is null)
        {
            error = "PDF xref stream is missing.";
            return false;
        }

        if (!TryDecodeStream(dict, stream, out var decoded, out error))
        {
            return false;
        }

        if (!ApplyTrailer(dict, newest, out error))
        {
            return false;
        }

        var w = dict.Get("W")?.AsArray();
        if (w is null || w.Items.Count < 3
            || !w.Items[0].TryGetInt(out var w1)
            || !w.Items[1].TryGetInt(out var w2)
            || !w.Items[2].TryGetInt(out var w3))
        {
            error = "PDF xref stream /W is invalid.";
            return false;
        }

        var columns = w1 + w2 + w3;
        var indexPairs = new List<(int Start, int Count)>();
        if (dict.Get("Index")?.AsArray() is { } index)
        {
            for (var i = 0; i + 1 < index.Items.Count; i += 2)
            {
                if (index.Items[i].TryGetInt(out var start) && index.Items[i + 1].TryGetInt(out var count))
                {
                    indexPairs.Add((start, count));
                }
            }
        }
        else
        {
            indexPairs.Add((0, Size));
        }

        var offsetDecoded = 0;
        foreach (var (start, count) in indexPairs)
        {
            for (var i = 0; i < count; i++)
            {
                if (offsetDecoded + columns > decoded.Length)
                {
                    error = "PDF xref stream is truncated.";
                    return false;
                }

                var type = w1 == 0 ? 1 : ReadBigEndian(decoded.AsSpan(offsetDecoded, w1));
                offsetDecoded += w1;
                var field2 = w2 == 0 ? 0 : ReadBigEndian(decoded.AsSpan(offsetDecoded, w2));
                offsetDecoded += w2;
                var field3 = w3 == 0 ? 0 : ReadBigEndian(decoded.AsSpan(offsetDecoded, w3));
                offsetDecoded += w3;
                var objNum = start + i;
                if (_xref.ContainsKey(objNum))
                {
                    continue;
                }

                _xref[objNum] = type switch
                {
                    0 => new XRefEntry(0, field3, true, false, 0, 0),
                    1 => new XRefEntry(field2, field3, false, false, 0, 0),
                    2 => new XRefEntry(0, 0, false, true, field2, field3),
                    _ => new XRefEntry(0, 0, true, false, 0, 0),
                };
            }
        }

        return TryWalkPrev(dict, out error);
    }

    private bool ApplyTrailer(PdfObject.Dict trailer, bool newest, out string? error)
    {
        if (trailer.Get("Size")?.TryGetInt(out var size) == true && size > 0)
        {
            Size = Math.Max(Size, size);
        }
        else if (newest)
        {
            error = "PDF trailer /Size is missing.";
            return false;
        }

        if (trailer.Get("Root")?.AsRef() is { } root)
        {
            if (newest)
            {
                Root = root;
            }
        }
        else if (newest)
        {
            error = "PDF trailer /Root is missing.";
            return false;
        }

        if (newest)
        {
            Info = trailer.Get("Info")?.AsRef();
            Id = trailer.Get("ID");
        }

        error = null;
        return true;
    }

    private bool TryReadCompressed(int number, XRefEntry entry, out PdfObject value, out string? error)
    {
        if (!TryGetObjectStream(entry.ObjectStream, out var decoded, out var first, out var offsets, out error))
        {
            value = new PdfObject.Null();
            return false;
        }

        if (entry.Index < 0 || entry.Index >= offsets.Count)
        {
            value = new PdfObject.Null();
            error = $"Object {number} index is outside its object stream.";
            return false;
        }

        var local = first + offsets[entry.Index].Offset;
        if (local < 0 || local >= decoded.Length)
        {
            value = new PdfObject.Null();
            error = $"Object {number} is truncated in its object stream.";
            return false;
        }

        var cursor = local;
        return TryReadValueFrom(decoded, ref cursor, out value, out error);
    }

    private bool TryGetObjectStream(
        int streamNumber,
        out byte[] decoded,
        out int first,
        out List<(int Number, int Offset)> offsets,
        out string? error)
    {
        offsets = [];
        first = 0;
        if (_objectStreams.TryGetValue(streamNumber, out var cached))
        {
            decoded = cached;
            if (!TryGet(streamNumber, out var streamObj, out error) || streamObj.AsDict() is not { } dict)
            {
                error ??= "Object stream dictionary is missing.";
                return false;
            }

            return TryParseObjectStreamIndex(dict, decoded, out first, out offsets, out error);
        }

        if (!_xref.TryGetValue(streamNumber, out var streamEntry) || streamEntry.Compressed || streamEntry.Free)
        {
            decoded = [];
            error = $"Object stream {streamNumber} is missing.";
            return false;
        }

        if (!TryReadIndirectObject(streamEntry.Offset, out _, out var streamDict, out var stream, out error)
            || stream is null)
        {
            decoded = [];
            error ??= "Object stream could not be read.";
            return false;
        }

        if (!TryDecodeStream(streamDict, stream, out decoded, out error))
        {
            return false;
        }

        _objectStreams[streamNumber] = decoded;
        _cache[streamNumber] = streamDict;
        return TryParseObjectStreamIndex(streamDict, decoded, out first, out offsets, out error);
    }

    private static bool TryParseObjectStreamIndex(
        PdfObject.Dict dict,
        byte[] decoded,
        out int first,
        out List<(int Number, int Offset)> offsets,
        out string? error)
    {
        offsets = [];
        first = 0;
        if (dict.Get("First")?.TryGetInt(out first) != true
            || dict.Get("N")?.TryGetInt(out var count) != true)
        {
            error = "Object stream /N or /First is missing.";
            return false;
        }

        var cursor = 0;
        for (var i = 0; i < count; i++)
        {
            if (!TryReadNumberFrom(decoded, ref cursor, out var num)
                || !TryReadNumberFrom(decoded, ref cursor, out var off)
                || !num.TryGetInt(out var objectNumber)
                || !off.TryGetInt(out var objectOffset))
            {
                error = "Object stream index is invalid.";
                return false;
            }

            offsets.Add((objectNumber, objectOffset));
        }

        error = null;
        return true;
    }

    private bool TryReadIndirect(int offset, out PdfObject value, out string? error)
    {
        if (!TryReadIndirectObject(offset, out var number, out var dict, out var stream, out error))
        {
            return Fail(out value, error);
        }

        if (_cache.TryGetValue(number, out var cached) && cached is not PdfObject.Dict)
        {
            value = cached;
            return true;
        }

        return AssignIndirect(dict, stream, out value);
    }

    private static bool AssignIndirect(PdfObject.Dict dict, byte[]? stream, out PdfObject value)
    {
        _ = stream;
        value = dict;
        return true;
    }

    private static bool Fail(out PdfObject value, string? error)
    {
        _ = error;
        value = new PdfObject.Null();
        return false;
    }

    private bool TryReadIndirectObject(
        int offset,
        out int number,
        out PdfObject.Dict dict,
        out byte[]? stream,
        out string? error)
    {
        number = 0;
        dict = new PdfObject.Dict(new Dictionary<string, PdfObject>());
        stream = null;
        var cursor = offset;
        SkipWhitespaceAndComments(ref cursor);
        if (!TryReadNumberToken(ref cursor, out var numObj) || !numObj.TryGetInt(out number)
            || !TryReadNumberToken(ref cursor, out _)
            || !TryReadKeyword(ref cursor, "obj"))
        {
            error = "Indirect object header is invalid.";
            return false;
        }

        SkipWhitespaceAndComments(ref cursor);
        if (!TryReadValue(ref cursor, out var value, out error) || value.AsDict() is not { } parsed)
        {
            // Non-dict objects (for completeness) wrap as a synthetic dict only when needed.
            if (error is not null)
            {
                return false;
            }

            _cache[number] = value;
            dict = new PdfObject.Dict(new Dictionary<string, PdfObject>());
            error = null;
            return true;
        }

        dict = parsed;
        _cache[number] = parsed;
        SkipWhitespaceAndComments(ref cursor);
        if (!StartsWith(cursor, "stream"u8))
        {
            error = null;
            return true;
        }

        cursor += 6;
        if (cursor < _data.Length && _data[cursor] == (byte)'\r')
        {
            cursor++;
        }

        if (cursor < _data.Length && _data[cursor] == (byte)'\n')
        {
            cursor++;
        }

        if (parsed.Get("Length")?.TryGetInt(out var length) != true)
        {
            if (parsed.Get("Length")?.AsRef() is { } lengthRef
                && TryGet(lengthRef.Number, out var lengthObj, out _)
                && lengthObj.TryGetInt(out length))
            {
                // resolved
            }
            else
            {
                error = "Stream /Length is missing.";
                return false;
            }
        }

        if (cursor + length > _data.Length)
        {
            error = "Stream is truncated.";
            return false;
        }

        stream = _data.AsSpan(cursor, length).ToArray();
        error = null;
        return true;
    }

    private bool TryDecodeStream(PdfObject.Dict dict, byte[] stream, out byte[] decoded, out string? error)
    {
        decoded = stream;
        error = null;
        var filter = dict.Get("Filter");
        var filters = new List<string>();
        if (filter is PdfObject.Name name)
        {
            filters.Add(name.Value);
        }
        else if (filter?.AsArray() is { } array)
        {
            foreach (var item in array.Items)
            {
                if (item.NameValue() is { } filterName)
                {
                    filters.Add(filterName);
                }
            }
        }

        foreach (var item in filters)
        {
            if (item is not ("FlateDecode" or "Fl"))
            {
                error = $"Unsupported PDF filter /{item}.";
                return false;
            }

            if (!TryInflate(decoded, out decoded, out error))
            {
                return false;
            }
        }

        var decodeParms = dict.Get("DecodeParms")?.AsDict() ?? dict.Get("DP")?.AsDict();
        if (decodeParms?.Get("Predictor")?.TryGetInt(out var predictor) == true && predictor >= 10)
        {
            var columns = 1;
            _ = decodeParms.Get("Columns")?.TryGetInt(out columns);
            decoded = PngUp(decoded, Math.Max(columns, 1));
        }

        return true;
    }

    private bool TryReadValue(ref int cursor, out PdfObject value, out string? error)
        => TryReadValueFrom(_data, ref cursor, out value, out error);

    private static bool TryReadValueFrom(byte[] data, ref int cursor, out PdfObject value, out string? error)
    {
        SkipWhitespaceAndComments(data, ref cursor);
        error = null;
        if (cursor >= data.Length)
        {
            value = new PdfObject.Null();
            error = "Unexpected end of PDF.";
            return false;
        }

        var b = data[cursor];
        if (b == (byte)'<')
        {
            if (cursor + 1 < data.Length && data[cursor + 1] == (byte)'<')
            {
                cursor += 2;
                var entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
                while (true)
                {
                    SkipWhitespaceAndComments(data, ref cursor);
                    if (cursor + 1 < data.Length && data[cursor] == (byte)'>' && data[cursor + 1] == (byte)'>')
                    {
                        cursor += 2;
                        value = new PdfObject.Dict(entries);
                        return true;
                    }

                    if (!TryReadValueFrom(data, ref cursor, out var keyObj, out error) || keyObj is not PdfObject.Name key)
                    {
                        value = new PdfObject.Null();
                        error ??= "Dictionary key is not a name.";
                        return false;
                    }

                    if (!TryReadValueFrom(data, ref cursor, out var entry, out error))
                    {
                        value = new PdfObject.Null();
                        return false;
                    }

                    entries[key.Value] = entry;
                }
            }

            return TryReadHexString(data, ref cursor, out value, out error);
        }

        if (b == (byte)'[')
        {
            cursor++;
            var items = new List<PdfObject>();
            while (true)
            {
                SkipWhitespaceAndComments(data, ref cursor);
                if (cursor < data.Length && data[cursor] == (byte)']')
                {
                    cursor++;
                    value = new PdfObject.Array(items);
                    return true;
                }

                if (!TryReadValueFrom(data, ref cursor, out var item, out error))
                {
                    value = new PdfObject.Null();
                    return false;
                }

                items.Add(item);
            }
        }

        if (b == (byte)'(')
        {
            return TryReadLiteral(data, ref cursor, out value, out error);
        }

        if (b == (byte)'/')
        {
            cursor++;
            var start = cursor;
            while (cursor < data.Length && !IsDelimiter(data[cursor]))
            {
                cursor++;
            }

            value = new PdfObject.Name(Encoding.ASCII.GetString(data, start, cursor - start));
            return true;
        }

        if (b is (byte)'t' or (byte)'f' or (byte)'n')
        {
            if (StartsWith(data, cursor, "true"u8))
            {
                cursor += 4;
                value = new PdfObject.Bool(true);
                return true;
            }

            if (StartsWith(data, cursor, "false"u8))
            {
                cursor += 5;
                value = new PdfObject.Bool(false);
                return true;
            }

            if (StartsWith(data, cursor, "null"u8))
            {
                cursor += 4;
                value = new PdfObject.Null();
                return true;
            }
        }

        if (TryReadNumberFrom(data, ref cursor, out var number))
        {
            var look = cursor;
            SkipWhitespaceAndComments(data, ref look);
            if (TryReadNumberFrom(data, ref look, out var generation))
            {
                var after = look;
                SkipWhitespaceAndComments(data, ref after);
                if (after < data.Length && data[after] == (byte)'R')
                {
                    cursor = after + 1;
                    if (number.TryGetInt(out var objNum) && generation.TryGetInt(out var gen))
                    {
                        value = new PdfObject.Ref(objNum, gen);
                        return true;
                    }
                }
            }

            value = number;
            return true;
        }

        value = new PdfObject.Null();
        error = "Unsupported PDF token.";
        return false;
    }

    private static bool TryReadLiteral(byte[] data, ref int cursor, out PdfObject value, out string? error)
    {
        error = null;
        cursor++;
        var depth = 1;
        var builder = new StringBuilder();
        while (cursor < data.Length && depth > 0)
        {
            var c = (char)data[cursor++];
            if (c == '\\' && cursor < data.Length)
            {
                builder.Append('\\');
                builder.Append((char)data[cursor++]);
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }

            builder.Append(c);
        }

        value = new PdfObject.String(builder.ToString(), Hex: false);
        return true;
    }

    private static bool TryReadHexString(byte[] data, ref int cursor, out PdfObject value, out string? error)
    {
        error = null;
        cursor++;
        var start = cursor;
        while (cursor < data.Length && data[cursor] != (byte)'>')
        {
            cursor++;
        }

        var hex = Encoding.ASCII.GetString(data, start, Math.Max(0, cursor - start)).Replace(" ", "", StringComparison.Ordinal);
        if (cursor < data.Length)
        {
            cursor++;
        }

        value = new PdfObject.String(hex, Hex: true);
        return true;
    }

    private bool TryReadNumberToken(ref int cursor, out PdfObject.Numeric number)
        => TryReadNumberFrom(_data, ref cursor, out number);

    private static bool TryReadNumberFrom(byte[] data, ref int cursor, out PdfObject.Numeric number)
    {
        SkipWhitespaceAndComments(data, ref cursor);
        var start = cursor;
        if (cursor < data.Length && data[cursor] is (byte)'+' or (byte)'-')
        {
            cursor++;
        }

        var digits = 0;
        while (cursor < data.Length && data[cursor] is >= (byte)'0' and <= (byte)'9')
        {
            cursor++;
            digits++;
        }

        if (cursor < data.Length && data[cursor] == (byte)'.')
        {
            cursor++;
            while (cursor < data.Length && data[cursor] is >= (byte)'0' and <= (byte)'9')
            {
                cursor++;
                digits++;
            }
        }

        if (digits == 0
            || !double.TryParse(
                Encoding.ASCII.GetString(data, start, cursor - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            cursor = start;
            number = new PdfObject.Numeric(0);
            return false;
        }

        number = new PdfObject.Numeric(value);
        return true;
    }

    private bool TryReadKeyword(ref int cursor, string keyword)
    {
        SkipWhitespaceAndComments(ref cursor);
        var bytes = Encoding.ASCII.GetBytes(keyword);
        if (!StartsWith(cursor, bytes))
        {
            return false;
        }

        cursor += bytes.Length;
        return true;
    }

    private void SkipWhitespaceAndComments(ref int cursor)
        => SkipWhitespaceAndComments(_data, ref cursor);

    private static void SkipWhitespaceAndComments(byte[] data, ref int cursor)
    {
        while (cursor < data.Length)
        {
            var b = data[cursor];
            if (b is 0 or 9 or 10 or 12 or 13 or 32)
            {
                cursor++;
                continue;
            }

            if (b != (byte)'%')
            {
                return;
            }

            cursor++;
            while (cursor < data.Length && data[cursor] is not 10 and not 13)
            {
                cursor++;
            }
        }
    }

    private bool StartsWith(int cursor, ReadOnlySpan<byte> text)
        => StartsWith(_data, cursor, text);

    private static bool StartsWith(byte[] data, int cursor, ReadOnlySpan<byte> text)
    {
        if (cursor + text.Length > data.Length)
        {
            return false;
        }

        return data.AsSpan(cursor, text.Length).SequenceEqual(text);
    }

    private int LastIndexOf(ReadOnlySpan<byte> needle, int from)
    {
        for (var i = Math.Min(from, _data.Length - needle.Length); i >= 0; i--)
        {
            if (_data.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsDelimiter(byte b)
        => b is 0 or 9 or 10 or 12 or 13 or 32
            or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
            or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'
            or (byte)'/' or (byte)'%';

    private static int ReadBigEndian(ReadOnlySpan<byte> data)
    {
        var value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
        }

        return value;
    }

    private static bool TryInflate(byte[] data, out byte[] decoded, out string? error)
    {
        error = null;
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            decoded = output.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            try
            {
                using var input = new MemoryStream(data);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                decoded = output.ToArray();
                return true;
            }
            catch (InvalidDataException ex)
            {
                decoded = [];
                error = "FlateDecode failed. " + ex.Message;
                return false;
            }
        }
    }

    private static byte[] PngUp(byte[] data, int columns)
    {
        var rowSize = columns + 1;
        if (rowSize <= 1 || data.Length < rowSize || data.Length % rowSize != 0)
        {
            return data;
        }

        var rows = data.Length / rowSize;
        var output = new byte[rows * columns];
        var prev = new byte[columns];
        for (var r = 0; r < rows; r++)
        {
            var tag = data[r * rowSize];
            for (var c = 0; c < columns; c++)
            {
                var raw = data[(r * rowSize) + 1 + c];
                var decoded = tag == 2 ? (byte)(raw + prev[c]) : raw;
                output[(r * columns) + c] = decoded;
                prev[c] = decoded;
            }
        }

        return output;
    }

    private readonly record struct XRefEntry(
        int Offset,
        int Generation,
        bool Free,
        bool Compressed,
        int ObjectStream,
        int Index);
}
