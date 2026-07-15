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
    public async Task CompileTemplateAsync_embeds_plots_from_workdir_beside_main_typ()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun(sampleCount: 8);
        await runStore.SaveAsync(run);

        var plotsDir = Path.Combine(runStore.GetRunDirectory(run.RunId), "plots");
        run.PlotImagePaths = SamplePlotExporter.ExportAllChannels(run, plotsDir).ToList();
        Assert.NotEmpty(run.PlotImagePaths);
        Assert.All(run.PlotImagePaths, p =>
        {
            Assert.True(File.Exists(p));
            Assert.True(new FileInfo(p).Length > 0);
        });

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = true });
        var pdf = await CompileOrSkipAsync(() => reports.CompileTemplateAsync(run));
        AssertPdfMagic(pdf);

        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId);
        Assert.True(File.Exists(Path.Combine(workDir, "main.typ")));
        Assert.True(File.Exists(Path.Combine(workDir, "plot-0.png")));
        Assert.True(new FileInfo(Path.Combine(workDir, "plot-0.png")).Length > 0);
    }

    private static TestRunRecord CreateRun(int sampleCount = 1)
    {
        var samples = Enumerable.Range(0, sampleCount)
            .Select(i => new StoredSample
            {
                Channel = "VDC",
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
