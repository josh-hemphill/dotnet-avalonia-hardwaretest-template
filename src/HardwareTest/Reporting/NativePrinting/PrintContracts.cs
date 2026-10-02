namespace HardwareTest.Reporting.NativePrinting;

public enum PrintOutcome { Cancelled, Applied, Submitted, Failed }
public sealed record PrintResult(PrintOutcome Outcome, string Message);
public enum PrintDialogAction { Cancel, Print, Apply }
public readonly record struct PrintPageRange(int From, int To);
public readonly record struct PdfPageSize(double WidthPoints, double HeightPoints);
public readonly record struct PrinterMetrics(int WidthPixels, int HeightPixels, int DpiX, int DpiY,
    int PhysicalOffsetX = 0, int PhysicalOffsetY = 0);
public readonly record struct PrintRectangle(int X, int Y, int Width, int Height);

public interface IPrintPage : IDisposable
{
    int Width { get; }
    int Height { get; }
    int Stride { get; }
    nint Pixels { get; }
}

public interface IPdfPrintDocument : IDisposable
{
    IReadOnlyList<PdfPageSize> PageSizes { get; }
    IPrintPage RenderPage(int zeroBasedPage);
}
public interface IPdfPrintRenderer { IPdfPrintDocument Open(string pdfPath); }
public interface IWindowsPrintBackend
{
    IWindowsPrintSession ShowDialog(nint owner, int pageCount);
}
public interface IWindowsPrintSession : IDisposable
{
    PrintDialogAction Action { get; }
    bool UsesPageRanges { get; }
    IReadOnlyList<PrintPageRange> PageRanges { get; }
    PrinterMetrics Metrics { get; }
    void StartDocument(string title);
    void StartPage();
    void DrawPage(IPrintPage page, PrintRectangle rectangle);
    void EndPage();
    void EndDocument();
    void AbortDocument();
}
public interface IPrintWorker { Task<T> RunAsync<T>(Func<T> action); }

public static class SharedPdfRenderingGate
{
    public static object SyncRoot { get; } = new();
}

public static class PrintGeometry
{
    public const int RenderDpi = 300;
    public const long MaxPixels = 64_000_000;

    public static (int Width, int Height) ValidateRasterSize(PdfPageSize size)
    {
        var width = Math.Ceiling(size.WidthPoints * RenderDpi / 72d);
        var height = Math.Ceiling(size.HeightPoints * RenderDpi / 72d);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1
            || width > int.MaxValue || height > int.MaxValue || width * height > MaxPixels)
            throw new InvalidDataException("PDF page exceeds the 64 million pixel printing limit at 300 dpi.");
        return ((int)width, (int)height);
    }

    /// GDI printer coordinates start at the printable area's origin, already excluding physical margins.
    public static PrintRectangle Fit(PdfPageSize size, PrinterMetrics metrics)
    {
        ValidateRasterSize(size);
        if (metrics.WidthPixels <= 0 || metrics.HeightPixels <= 0 || metrics.DpiX <= 0 || metrics.DpiY <= 0)
            throw new InvalidDataException("The printer returned invalid printable dimensions or resolution.");
        var widthInches = size.WidthPoints / 72d;
        var heightInches = size.HeightPoints / 72d;
        var scale = Math.Min(metrics.WidthPixels / (double)metrics.DpiX / widthInches,
            metrics.HeightPixels / (double)metrics.DpiY / heightInches);
        var width = Math.Clamp((int)Math.Round(widthInches * scale * metrics.DpiX), 1, metrics.WidthPixels);
        var height = Math.Clamp((int)Math.Round(heightInches * scale * metrics.DpiY), 1, metrics.HeightPixels);
        return new((metrics.WidthPixels - width) / 2, (metrics.HeightPixels - height) / 2, width, height);
    }

    public static IReadOnlyList<PrintPageRange> NormalizeRanges(int pageCount, bool selected, IReadOnlyList<PrintPageRange> ranges)
    {
        if (pageCount < 1) throw new InvalidDataException("The PDF has no printable pages.");
        if (!selected) return [new(1, pageCount)];
        if (ranges.Count is < 1 or > 64) throw new InvalidDataException("The printer dialog returned invalid page ranges.");
        var result = new List<PrintPageRange>();
        foreach (var range in ranges.OrderBy(r => r.From).ThenBy(r => r.To))
        {
            if (range.From < 1 || range.To < range.From || range.To > pageCount)
                throw new InvalidDataException("Requested print pages are outside this PDF.");
            if (result.Count > 0 && (long)result[^1].To + 1 >= range.From)
                result[^1] = new(result[^1].From, Math.Max(result[^1].To, range.To));
            else result.Add(range);
        }
        return result;
    }

    public static IEnumerable<int> EnumeratePages(IEnumerable<PrintPageRange> ranges)
    {
        foreach (var range in ranges)
        {
            var page = range.From;
            while (true)
            {
                yield return page - 1;
                if (page == range.To) break;
                page++;
            }
        }
    }
}
