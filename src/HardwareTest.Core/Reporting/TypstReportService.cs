using System.Globalization;
using System.Text;
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
        return await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pdfBytes = CompileTemplateCore(run, cancellationToken);
                var dir = _runStore.GetRunDirectory(run.RunId);
                var path = Path.Combine(dir, "report.pdf");
                File.WriteAllBytes(path, pdfBytes);
                run.ReportPdfPath = path;
                _runStore.SaveAsync(run, cancellationToken).GetAwaiter().GetResult();
                _logger.Information("Wrote report PDF for run {RunId} to {Path}", run.RunId, path);
                return path;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GenerateSuitePdfAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
    {
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
        => Task.Run(() => CompileTemplateCore(run, cancellationToken), cancellationToken);

    private byte[] CompileTemplateCore(TestRunRecord run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = LoadEmbeddedResource("test-report.typ");
        var chartLib = LoadEmbeddedResource("sample-chart.typ");
        var resultJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId);
        var libDir = Path.Combine(workDir, "lib");
        Directory.CreateDirectory(libDir);

        File.WriteAllText(Path.Combine(workDir, "main.typ"), template, Encoding.UTF8);
        File.WriteAllText(Path.Combine(libDir, "sample-chart.typ"), chartLib, Encoding.UTF8);
        File.WriteAllText(Path.Combine(workDir, "run.json"), resultJson, Encoding.UTF8);

        var includePlots = _settings.EmbedPlotsInReport && run.Samples.Count > 0;
        return CompileOnce(workDir, template, run, resultJson, chartLib, includePlots);
    }

    private byte[] CompileOnce(
        string workDir,
        string template,
        TestRunRecord run,
        string resultJson,
        string chartLib,
        bool includePlots)
    {
        var result = _compiler.Value.Compile(c =>
        {
            var builder = c
                .WithRoot(workDir)
                .WithSource(template)
                .WithFile("run.json", Encoding.UTF8.GetBytes(resultJson))
                .WithFile("lib/sample-chart.typ", Encoding.UTF8.GetBytes(chartLib))
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
                        ? $"Generated by HardwareTest. Samples={run.Samples.Count}. Trace={run.TraceId}. Charts from sample data."
                        : $"Generated by HardwareTest. Samples={run.Samples.Count}. Trace={run.TraceId}.")
                .WithInput("attemptSummary", FormatAttemptSummary(run))
                .WithInput("runJson", resultJson)
                .WithInput("includePlots", includePlots ? "true" : "false");

            return builder;
        });

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Typst compilation failed: {result.ErrorMessage}");
        }

        return result.Output.ToArray();
    }

    private static string FormatAttemptSummary(TestRunRecord run)
    {
        if (run.StepAttempts.Count == 0)
        {
            return string.Empty;
        }

        var lines = run.StepAttempts
            .OrderBy(a => a.StepPath)
            .Select(a =>
                $"- {a.StepName}: {a.AttemptCount} attempts ({a.FailedCount} failed / {a.PassedCount} passed); latest={(a.LatestPassed == true ? "PASS" : "FAIL")}");
        return string.Join("\n", lines);
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

    private static string LoadEmbeddedResource(string fileName)
    {
        var assembly = typeof(TypstReportService).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded Typst resource '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Unable to open embedded resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
