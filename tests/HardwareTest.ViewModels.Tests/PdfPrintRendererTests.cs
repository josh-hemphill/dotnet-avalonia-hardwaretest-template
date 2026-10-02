using System.Globalization;
using System.Text;
using HardwareTest.Reporting.NativePrinting;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class PdfPrintRendererTests
{
    [Fact]
    public void Real_pdf_renders_page_after_preview_limit_as_tightly_packed_300dpi_BGRA()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), $"print-render-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, CreatePdf(12, 144, 72));
            using var document = new PdfPrintRenderer().Open(path);
            Assert.Equal(12, document.PageSizes.Count);
            Assert.Equal(new PdfPageSize(144, 72), document.PageSizes[11]);
            using var page = document.RenderPage(11);
            Assert.Equal(600, page.Width);
            Assert.Equal(300, page.Height);
            Assert.Equal(2400, page.Stride);
            Assert.NotEqual(0, page.Pixels);
            var pixel = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(page.Pixels, pixel, 0, 4);
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Real_pdf_oversized_page_is_rejected_before_rendering()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), $"print-limit-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, CreatePdf(1, 7200, 7200));
            using var document = new PdfPrintRenderer().Open(path);
            Assert.Throws<InvalidDataException>(() => document.RenderPage(0));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Rotated_pdf_metadata_matches_raster_orientation_for_physical_fit()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), $"print-rotation-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, CreatePdf(1, 144, 72, rotation: 90));
            using var document = new PdfPrintRenderer().Open(path);
            using var page = document.RenderPage(0);
            var expected = PrintGeometry.ValidateRasterSize(document.PageSizes[0]);
            Assert.Equal(expected.Width, page.Width);
            Assert.Equal(expected.Height, page.Height);
            Assert.Equal(300, page.Width);
            Assert.Equal(600, page.Height);
        }
        finally { File.Delete(path); }
    }

    private static byte[] CreatePdf(int pages, int width, int height, int rotation = 0)
    {
        // A valid small PDF with independent blank pages; no external PDF generator or printer.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages} /Kids [{string.Join(' ', Enumerable.Range(0, pages).Select(i => $"{i + 3} 0 R"))}] >>"
        };
        objects.AddRange(Enumerable.Range(0, pages).Select(_ =>
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Rotate {rotation} /Resources << >> >>"));
        var content = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(content.Length);
            content.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = content.Length;
        content.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) content.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        content.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(content.ToString());
    }
}
