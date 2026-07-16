using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class SamplePlotExporterTests
{
    [Fact]
    public void ExportChannelPng_writes_real_png_with_axes_sized_image()
    {
        var run = new TestRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanId = "sample",
            PlanName = "Sample",
            Samples = Enumerable.Range(0, 32)
                .Select(i => new StoredSample
                {
                    Channel = "VDC",
                    Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(i * 5),
                    Value = Math.Sin(i / 5.0) + 1.25,
                })
                .ToList(),
        };

        var dir = Path.Combine(Path.GetTempPath(), "ht-plots-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = SamplePlotExporter.ExportChannelPng(run, "VDC", dir);
            Assert.NotNull(path);
            Assert.True(File.Exists(path));

            var bytes = File.ReadAllBytes(path!);
            Assert.True(bytes.Length > 5_000, $"Expected ScottPlot PNG, got {bytes.Length} bytes");
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
