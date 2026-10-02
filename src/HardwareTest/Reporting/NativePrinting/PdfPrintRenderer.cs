using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace HardwareTest.Reporting.NativePrinting;

[SupportedOSPlatform("windows")]
public sealed class PdfPrintRenderer : IPdfPrintRenderer
{
    public IPdfPrintDocument Open(string pdfPath) => new Document(pdfPath);

    private sealed class Document : IPdfPrintDocument
    {
        private readonly FileStream _stream;
        public IReadOnlyList<PdfPageSize> PageSizes { get; }
        public Document(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            try
            {
                lock (SharedPdfRenderingGate.SyncRoot)
                {
                    var count = Conversion.GetPageCount(_stream, leaveOpen: true);
                    _stream.Position = 0;
                    PageSizes = Conversion.GetPageSizes(_stream, leaveOpen: true)
                        .Select(size => new PdfPageSize(size.Width, size.Height)).ToArray();
                    if (count < 1 || PageSizes.Count != count) throw new InvalidDataException("Could not inspect all PDF page sizes.");
                }
            }
            catch { _stream.Dispose(); throw; }
        }
        public IPrintPage RenderPage(int zeroBasedPage)
        {
            PrintGeometry.ValidateRasterSize(PageSizes[zeroBasedPage]);
            lock (SharedPdfRenderingGate.SyncRoot)
            {
                _stream.Position = 0;
                using var rendered = Conversion.ToImage(_stream, new Index(zeroBasedPage), leaveOpen: true,
                    options: new RenderOptions(Dpi: PrintGeometry.RenderDpi, WithAspectRatio: true));
                if (rendered.Width < 1 || rendered.Height < 1 || (long)rendered.Width * rendered.Height > PrintGeometry.MaxPixels)
                    throw new InvalidDataException("Rendered PDF page exceeds the printing limit.");
                var bitmap = new SKBitmap(new SKImageInfo(rendered.Width, rendered.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                try
                {
                    if (bitmap.GetPixels() == 0 || bitmap.RowBytes != checked(bitmap.Width * 4))
                        throw new InvalidDataException("Could not allocate a tightly packed BGRA print bitmap.");
                    using var canvas = new SKCanvas(bitmap);
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(rendered, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
                    canvas.Flush();
                    return new Page(bitmap);
                }
                catch { bitmap.Dispose(); throw; }
            }
        }
        public void Dispose() => _stream.Dispose();
    }
    private sealed class Page(SKBitmap bitmap) : IPrintPage
    {
        public int Width => bitmap.Width;
        public int Height => bitmap.Height;
        public int Stride => bitmap.RowBytes;
        public nint Pixels => bitmap.GetPixels();
        public void Dispose() => bitmap.Dispose();
    }
}
