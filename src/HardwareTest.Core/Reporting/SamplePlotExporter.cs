using HardwareTest.Core.Runs;

namespace HardwareTest.Core.Reporting;

/// Builds simple PNG plots from stored samples without Avalonia (CI-safe).
public static class SamplePlotExporter
{
    /// Writes a grayscale PNG height map / strip chart under the run plots folder. Returns path or null.
    public static string? ExportChannelPng(TestRunRecord run, string channel, string outputDirectory)
    {
        var samples = run.Samples
            .Where(s => string.Equals(s.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Timestamp)
            .Select(s => s.Value)
            .ToArray();
        if (samples.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(outputDirectory);
        var safePlan = Sanitize(string.IsNullOrWhiteSpace(run.PlanId) ? run.PlanName : run.PlanId);
        var path = Path.Combine(outputDirectory, $"{safePlan}-{Sanitize(channel)}.png");
        WriteStripChartPng(samples, path);
        return path;
    }

    public static IReadOnlyList<string> ExportAllChannels(TestRunRecord run, string outputDirectory)
    {
        var channels = run.Samples.Select(s => s.Channel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var paths = new List<string>();
        foreach (var channel in channels)
        {
            var path = ExportChannelPng(run, channel, outputDirectory);
            if (path is not null)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static void WriteStripChartPng(double[] ys, string path)
    {
        const int width = 640;
        const int height = 240;
        var min = ys.Min();
        var max = ys.Max();
        var range = Math.Max(1e-9, max - min);

        // Minimal uncompressed PNG (RGBA) via raw DEFLATE-free approach: use BMP-like then... 
        // Prefer writing a simple PPM converted isn't PDF friendly. Use built-in PNG via Skia-free bit packing.
        // Encode as 8-bit grayscale PNG with zlib-less store blocks for simplicity.
        var pixels = new byte[width * height];
        for (var x = 0; x < width; x++)
        {
            var idx = (int)((long)x * (ys.Length - 1) / Math.Max(1, width - 1));
            var yNorm = (ys[idx] - min) / range;
            var yPix = (int)((1.0 - yNorm) * (height - 1));
            yPix = Math.Clamp(yPix, 0, height - 1);
            for (var t = Math.Max(0, yPix - 1); t <= Math.Min(height - 1, yPix + 1); t++)
            {
                pixels[t * width + x] = 0;
            }
        }

        // Fill background white where unset (0 means plotted; use 255 bg then draw 0)
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == 0)
            {
                // keep black line — but unmarked cells are also 0. Rebuild properly.
            }
        }

        var canvas = new byte[width * height];
        Array.Fill(canvas, (byte)255);
        for (var x = 0; x < width; x++)
        {
            var idx = (int)((long)x * (ys.Length - 1) / Math.Max(1, width - 1));
            var yNorm = (ys[idx] - min) / range;
            var yPix = (int)((1.0 - yNorm) * (height - 1));
            yPix = Math.Clamp(yPix, 0, height - 1);
            for (var t = Math.Max(0, yPix - 1); t <= Math.Min(height - 1, yPix + 1); t++)
            {
                canvas[t * width + x] = 0;
            }
        }

        File.WriteAllBytes(path, EncodeGrayPng(canvas, width, height));
    }

    private static byte[] EncodeGrayPng(byte[] gray, int width, int height)
    {
        using var ms = new MemoryStream();
        // PNG signature
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(ms, "IHDR", writer =>
        {
            WriteInt(writer, width);
            WriteInt(writer, height);
            writer.WriteByte(8); // bit depth
            writer.WriteByte(0); // grayscale
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
        });

        // Raw image data with filter byte 0 per row, zlib-wrapped (stored)
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(gray, y * width, width);
        }

        var deflated = ZlibStore(raw.ToArray());
        WriteChunk(ms, "IDAT", w => w.Write(deflated));
        WriteChunk(ms, "IEND", _ => { });
        return ms.ToArray();
    }

    private static byte[] ZlibStore(byte[] data)
    {
        // zlib header + uncompressed deflate blocks + adler32
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x01);
        var offset = 0;
        while (offset < data.Length)
        {
            var len = Math.Min(65535, data.Length - offset);
            var last = offset + len >= data.Length;
            ms.WriteByte((byte)(last ? 0x01 : 0x00));
            ms.WriteByte((byte)(len & 0xff));
            ms.WriteByte((byte)((len >> 8) & 0xff));
            var nlen = ~len;
            ms.WriteByte((byte)(nlen & 0xff));
            ms.WriteByte((byte)((nlen >> 8) & 0xff));
            ms.Write(data, offset, len);
            offset += len;
        }

        var adler = Adler32(data);
        WriteInt(ms, (int)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var t in data)
        {
            a = (a + t) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static void WriteChunk(Stream stream, string type, Action<Stream> writeData)
    {
        using var content = new MemoryStream();
        foreach (var c in type)
        {
            content.WriteByte((byte)c);
        }

        writeData(content);
        var bytes = content.ToArray();
        var dataLen = bytes.Length - 4;
        WriteInt(stream, dataLen);
        stream.Write(bytes);
        var crc = Crc32(bytes);
        WriteInt(stream, (int)crc);
    }

    private static void WriteInt(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)(value & 0xff));
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var mask = (uint)-(crc & 1);
                crc = (crc >> 1) ^ (0xedb88320 & mask);
            }
        }

        return ~crc;
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "plot" : value;
    }
}
