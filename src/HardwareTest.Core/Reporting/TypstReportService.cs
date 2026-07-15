using System.Globalization;
using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using Serilog;
using TypstInterop;

namespace HardwareTest.Core.Reporting;

public interface IReportService
{
    Task<string> GeneratePdfAsync(TestRunRecord run, CancellationToken cancellationToken = default);
    Task<string> GenerateSuitePdfAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default);
    Task<byte[]> CompileTemplateAsync(TestRunRecord run, CancellationToken cancellationToken = default);
}

/// Compiles Typst templates to PDF and writes them beside run folders.
public sealed class TypstReportService : IReportService, IDisposable
{
    private readonly IRunStore _runStore;
    private readonly ISuiteRunStore? _suiteRunStore;
    private readonly AppSettings _settings;
    private readonly Lazy<TypstCompiler> _compiler;
    private readonly ILogger _logger;
    private bool _disposed;

    public TypstReportService(
        IRunStore runStore,
        AppSettings settings,
        ISuiteRunStore? suiteRunStore = null,
        ILogger? logger = null)
    {
        _runStore = runStore;
        _suiteRunStore = suiteRunStore;
        _settings = settings;
        _logger = logger ?? Log.ForContext<TypstReportService>();
        _compiler = new Lazy<TypstCompiler>(() => new TypstCompiler());
    }

    public async Task<string> GeneratePdfAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        if (_settings.EmbedPlotsInReport && run.Samples.Count > 0 && run.PlotImagePaths.Count == 0)
        {
            var plotsDir = Path.Combine(_runStore.GetRunDirectory(run.RunId), "plots");
            run.PlotImagePaths = SamplePlotExporter.ExportAllChannels(run, plotsDir).ToList();
        }

        var pdfBytes = await CompileTemplateAsync(run, cancellationToken).ConfigureAwait(false);
        var dir = _runStore.GetRunDirectory(run.RunId);
        var path = Path.Combine(dir, "report.pdf");
        await File.WriteAllBytesAsync(path, pdfBytes, cancellationToken).ConfigureAwait(false);
        run.ReportPdfPath = path;
        await _runStore.SaveAsync(run, cancellationToken).ConfigureAwait(false);
        _logger.Information("Wrote report PDF for run {RunId} to {Path}", run.RunId, path);
        return path;
    }

    public async Task<string> GenerateSuitePdfAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
    {
        var root = _suiteRunStore?.GetSuiteRunDirectory(suiteRun.SuiteRunId)
                   ?? _runStore.GetRunDirectory(suiteRun.SuiteRunId);
        var plotsDir = Path.Combine(root, "plots");
        var paths = new List<string>();
        if (_settings.EmbedPlotsInReport)
        {
            foreach (var planRun in suiteRun.PlanRuns)
            {
                paths.AddRange(SamplePlotExporter.ExportAllChannels(planRun, plotsDir));
            }
        }

        var aggregate = new TestRunRecord
        {
            RunId = suiteRun.SuiteRunId,
            PlanId = suiteRun.SuiteId,
            PlanName = suiteRun.SuiteName,
            StartedAt = suiteRun.StartedAt,
            CompletedAt = suiteRun.CompletedAt,
            Result = suiteRun.Result,
            ErrorMessage = suiteRun.ErrorMessage,
            Samples = suiteRun.PlanRuns.SelectMany(p => p.Samples).ToList(),
            Steps = suiteRun.PlanRuns.SelectMany(p => p.Steps).ToList(),
            PlotImagePaths = paths,
        };

        var path = await GeneratePdfAsync(aggregate, cancellationToken).ConfigureAwait(false);
        suiteRun.ReportPdfPath = path;
        if (_suiteRunStore is not null)
        {
            await _suiteRunStore.SaveAsync(suiteRun, CancellationToken.None).ConfigureAwait(false);
        }

        return path;
    }

    public Task<byte[]> CompileTemplateAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = LoadEmbeddedTemplate();
        var resultJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId);
        Directory.CreateDirectory(workDir);

        var plotFiles = new List<(string Name, byte[] Bytes)>();
        if (_settings.EmbedPlotsInReport)
        {
            var plotPaths = run.PlotImagePaths ?? [];
            for (var i = 0; i < plotPaths.Count && i < 3; i++)
            {
                var src = plotPaths[i];
                if (!File.Exists(src))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(src);
                if (bytes.Length == 0)
                {
                    continue;
                }

                var name = $"plot-{i}.png";
                File.WriteAllBytes(Path.Combine(workDir, name), bytes);
                plotFiles.Add((name, bytes));
            }
        }

        var mainTypPath = Path.Combine(workDir, "main.typ");
        File.WriteAllText(mainTypPath, template);

        try
        {
            return Task.FromResult(CompileOnce(workDir, template, run, resultJson, plotFiles, includePlots: plotFiles.Count > 0));
        }
        catch (InvalidOperationException ex) when (plotFiles.Count > 0)
        {
            _logger.Warning(ex, "Typst could not embed plot images; compiling report without embeds.");
            return Task.FromResult(CompileOnce(workDir, template, run, resultJson, plotFiles: [], includePlots: false));
        }
    }

    private byte[] CompileOnce(
        string workDir,
        string template,
        TestRunRecord run,
        string resultJson,
        IReadOnlyList<(string Name, byte[] Bytes)> plotFiles,
        bool includePlots)
    {
        var result = _compiler.Value.Compile(c =>
        {
            var builder = c
                .WithRoot(workDir)
                .WithSource(template)
                .WithInput("title", "Hardware Test Report")
                .WithInput("runId", run.RunId)
                .WithInput("planName", run.PlanName)
                .WithInput("dutSerial", run.DutSerial ?? "n/a")
                .WithInput("result", run.Result.ToString())
                .WithInput("startedAt", run.StartedAt.ToString("u", CultureInfo.InvariantCulture))
                .WithInput("completedAt", run.CompletedAt?.ToString("u", CultureInfo.InvariantCulture) ?? "n/a")
                .WithInput("sampleCount", run.Samples.Count.ToString(CultureInfo.InvariantCulture))
                .WithInput(
                    "notes",
                    includePlots
                        ? $"Generated by HardwareTest. Samples={run.Samples.Count}. Trace={run.TraceId}. Plots embedded."
                        : $"Generated by HardwareTest. Samples={run.Samples.Count}. Trace={run.TraceId}. Plot PNGs saved beside the run when available.")
                .WithInput("runJson", resultJson)
                .WithInput("includePlots", includePlots ? "true" : "false")
                .WithInput("plotCount", plotFiles.Count.ToString(CultureInfo.InvariantCulture));

            foreach (var (name, bytes) in plotFiles)
            {
                builder = builder.WithFile(name, bytes);
            }

            if (plotFiles.Count > 0)
            {
                builder = builder.WithInput("plot0", plotFiles[0].Name);
            }

            if (plotFiles.Count > 1)
            {
                builder = builder.WithInput("plot1", plotFiles[1].Name);
            }

            if (plotFiles.Count > 2)
            {
                builder = builder.WithInput("plot2", plotFiles[2].Name);
            }

            return builder;
        });

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Typst compilation failed: {result.ErrorMessage}");
        }

        return result.Output.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_compiler.IsValueCreated)
        {
            _compiler.Value.Dispose();
        }

        _disposed = true;
    }

    private static string LoadEmbeddedTemplate()
    {
        var assembly = typeof(TypstReportService).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("test-report.typ", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded Typst template 'test-report.typ' was not found.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Unable to open embedded resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
