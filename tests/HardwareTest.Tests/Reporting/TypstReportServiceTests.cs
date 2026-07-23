using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class TypstReportServiceTests
{
    [Fact]
    public async Task GeneratePdfAsync_writes_pdf_and_updates_run()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = false });
        var path = await CompileOrSkipAsync(() => reports.GeneratePdfAsync(run));

        Assert.True(File.Exists(path));
        AssertPdfMagic(await File.ReadAllBytesAsync(path));

        var reloaded = await runStore.LoadAsync(run.RunId);
        Assert.Equal(path, reloaded!.ReportPdfPath);
    }

    [Fact]
    public async Task CompileTemplateAsync_charts_from_samples_without_png_paths()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun(sampleCount: 8);
        await runStore.SaveAsync(run);
        Assert.Empty(run.PlotImagePaths);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = true });
        var pdf = await CompileOrSkipAsync(() => reports.CompileTemplateAsync(run));
        AssertPdfMagic(pdf);

        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId);
        Assert.True(File.Exists(Path.Combine(workDir, "main.typ")));
        Assert.True(File.Exists(Path.Combine(workDir, "run.json")));
        Assert.True(File.Exists(Path.Combine(workDir, "lib", "sample-chart.typ")));

        var json = await File.ReadAllTextAsync(Path.Combine(workDir, "run.json"));
        Assert.Contains("\"samples\"", json, StringComparison.Ordinal);
        Assert.Contains("\"channel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Samples\"", json, StringComparison.Ordinal);

        var chartLib = await File.ReadAllTextAsync(Path.Combine(workDir, "lib", "sample-chart.typ"));
        Assert.Contains("samples", chartLib, StringComparison.Ordinal);
        Assert.Contains("channel", chartLib, StringComparison.Ordinal);
    }

    [Fact]
    public void SamplePlotExporter_still_writes_png_when_called_directly()
    {
        using var temp = new TempDataDirectory();
        var run = CreateRun(sampleCount: 4);
        var plotsDir = Path.Combine(temp.Path, "plots");
        var paths = SamplePlotExporter.ExportAllChannels(run, plotsDir);
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(File.Exists(p) && new FileInfo(p).Length > 0));
    }

    [Fact]
    public async Task CompileTemplateAsync_uses_DataDirectory_reports_override()
    {
        using var temp = new TempDataDirectory();
        var reportsDir = Path.Combine(temp.Path, "reports");
        Directory.CreateDirectory(reportsDir);
        File.WriteAllText(
            Path.Combine(reportsDir, "test-report.typ"),
            """
            #set page(width: 100mm, height: 50mm)
            = Override template
            Run: #sys.inputs.runId
            """);

        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(
            runStore,
            new AppSettings
            {
                DataDirectory = temp.Path,
                EmbedPlotsInReport = false,
                ReportTemplateName = "test-report.typ",
            });
        var pdf = await CompileOrSkipAsync(() => reports.CompileTemplateAsync(run));
        AssertPdfMagic(pdf);

        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId);
        var main = await File.ReadAllTextAsync(Path.Combine(workDir, "main.typ"));
        Assert.Contains("Override template", main, StringComparison.Ordinal);
    }

    private static TestRunRecord CreateRun(int sampleCount = 1)
    {
        var samples = Enumerable.Range(0, sampleCount)
            .Select(i => new StoredSample
            {
                Channel = "VDC",
                StepPath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
                Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(i),
                Value = 1.0 + (i * 0.1),
            })
            .ToList();

        return new TestRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "DUT-1",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = samples,
        };
    }

    private static async Task<T> CompileOrSkipAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Typst native library was not restored. Run tests with `-r win-x64` (or another supported RID).",
                ex);
        }
    }

    private static void AssertPdfMagic(byte[] bytes)
    {
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
}
