using System.Globalization;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using Serilog;
using TypstInterop;

namespace HardwareTest.Core.Reporting;

public interface IReportService
{
    Task<string> GeneratePdfAsync(TestRunRecord run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunReportArtifact>> GenerateReportsAsync(
        TestRunRecord run,
        IReadOnlyList<string> kinds,
        DutHistoryReport? history = null,
        CancellationToken cancellationToken = default);
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
        var artifacts = await GenerateReportsAsync(run, [ReportKinds.Status], history: null, cancellationToken)
            .ConfigureAwait(false);
        return artifacts.FirstOrDefault()?.PdfPath
               ?? run.ReportPdfPath
               ?? string.Empty;
    }

    public async Task<IReadOnlyList<RunReportArtifact>> GenerateReportsAsync(
        TestRunRecord run,
        IReadOnlyList<string> kinds,
        DutHistoryReport? history = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeKinds(kinds);
        return await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = _runStore.GetRunDirectory(run.RunId);
                var artifacts = new List<RunReportArtifact>();
                var now = DateTimeOffset.UtcNow;
                foreach (var kind in normalized)
                {
                    var templateName = ResolveTemplateName(kind);
                    var fileName = $"{kind}.pdf";
                    var title = KindTitle(kind);
                    var pdfBytes = CompileTemplateCore(run, templateName, kind, history, title, cancellationToken);
                    var path = Path.Combine(dir, fileName);
                    File.WriteAllBytes(path, pdfBytes);
                    artifacts.Add(new RunReportArtifact
                    {
                        Kind = kind,
                        Title = title,
                        PdfPath = path,
                        GeneratedAt = now,
                    });
                    _logger.Information("Wrote {Kind} PDF for run {RunId} to {Path}", kind, run.RunId, path);
                }

                run.Reports = artifacts.ToList();
                run.ReportPdfPath = artifacts.FirstOrDefault(a =>
                                        string.Equals(a.Kind, ReportKinds.Status, StringComparison.OrdinalIgnoreCase))
                                    ?.PdfPath
                                    ?? artifacts.FirstOrDefault()?.PdfPath;
                _runStore.SaveAsync(run, cancellationToken).GetAwaiter().GetResult();
                return (IReadOnlyList<RunReportArtifact>)artifacts;
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
        => Task.Run(
            () => CompileTemplateCore(
                run,
                ResolveTemplateName(ReportKinds.Status),
                ReportKinds.Status,
                history: null,
                KindTitle(ReportKinds.Status),
                cancellationToken),
            cancellationToken);

    private static IReadOnlyList<string> NormalizeKinds(IReadOnlyList<string> kinds)
    {
        if (kinds.Count == 0)
        {
            return [ReportKinds.Status];
        }

        var list = new List<string>();
        foreach (var raw in kinds)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var kind = raw.Trim().ToLowerInvariant();
            if (!list.Contains(kind, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(kind);
            }
        }

        return list.Count == 0 ? [ReportKinds.Status] : list;
    }

    private string ResolveTemplateName(string kind)
    {
        if (string.Equals(kind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase))
        {
            return "certification-report.typ";
        }

        // Honor ReportTemplateName including the default (test-report.typ) so a
        // {DataDirectory}/reports/ override of that filename wins. status-report.typ
        // remains an alias resolved in CompileTemplateCore when needed.
        if (!string.IsNullOrWhiteSpace(_settings.ReportTemplateName))
        {
            return _settings.ReportTemplateName.Trim();
        }

        return "test-report.typ";
    }

    private static string KindTitle(string kind)
        => string.Equals(kind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase)
            ? "Certification Report"
            : "Status Report";

    private byte[] CompileTemplateCore(
        TestRunRecord run,
        string templateName,
        string kind,
        DutHistoryReport? history,
        string title,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string template;
        try
        {
            template = LoadReportFile(templateName);
        }
        catch (InvalidOperationException) when (TryStatusTemplateAlias(templateName, out var alias))
        {
            template = LoadReportFile(alias);
        }

        var chartLib = LoadReportFile("sample-chart.typ", preferLibSubfolder: true);
        var resultJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId, kind);
        var libDir = Path.Combine(workDir, "lib");
        Directory.CreateDirectory(libDir);

        File.WriteAllText(Path.Combine(workDir, "main.typ"), template, Encoding.UTF8);
        File.WriteAllText(Path.Combine(libDir, "sample-chart.typ"), chartLib, Encoding.UTF8);
        File.WriteAllText(Path.Combine(workDir, "run.json"), resultJson, Encoding.UTF8);

        var includePlots = _settings.EmbedPlotsInReport && run.Samples.Count > 0;
        var includeHistory = string.Equals(kind, ReportKinds.Status, StringComparison.OrdinalIgnoreCase)
                             && history is not null
                             && !string.IsNullOrWhiteSpace(history.OperatorSummary);
        return CompileOnce(workDir, template, run, resultJson, chartLib, includePlots, includeHistory, history, title);
    }

    private byte[] CompileOnce(
        string workDir,
        string template,
        TestRunRecord run,
        string resultJson,
        string chartLib,
        bool includePlots,
        bool includeHistory,
        DutHistoryReport? history,
        string title)
    {
        var historySummary = includeHistory ? history!.OperatorSummary : string.Empty;
        var historySeverity = includeHistory ? history!.OverallSeverity.ToString() : string.Empty;
        var historyMetrics = includeHistory ? FormatHistoryMetrics(history!) : string.Empty;

        var result = _compiler.Value.Compile(c =>
        {
            var builder = c
                .WithRoot(workDir)
                .WithSource(template)
                .WithFile("run.json", Encoding.UTF8.GetBytes(resultJson))
                .WithFile("lib/sample-chart.typ", Encoding.UTF8.GetBytes(chartLib))
                .WithInput("title", title)
                .WithInput("runId", run.RunId)
                .WithInput("planName", run.PlanName)
                .WithInput("dutSerial", run.DutSerial ?? "n/a")
                .WithInput("result", run.Result.ToString())
                .WithInput("startedAt", run.StartedAt.ToString("u", CultureInfo.InvariantCulture))
                .WithInput("completedAt", run.CompletedAt?.ToString("u", CultureInfo.InvariantCulture) ?? "n/a")
                .WithInput("sampleCount", run.Samples.Count.ToString(CultureInfo.InvariantCulture))
                .WithInput("appVersion", run.AppVersion ?? "unknown")
                .WithInput("appCommit", run.AppCommitSha ?? "unknown")
                .WithInput(
                    "notes",
                    includePlots
                        ? $"Generated by HardwareTest {run.AppVersion ?? "unknown"} (commit {run.AppCommitSha ?? "unknown"}). Samples={run.Samples.Count}. Trace={run.TraceId}. Charts from sample data."
                        : $"Generated by HardwareTest {run.AppVersion ?? "unknown"} (commit {run.AppCommitSha ?? "unknown"}). Samples={run.Samples.Count}. Trace={run.TraceId}.")
                .WithInput("attemptSummary", FormatAttemptSummary(run))
                .WithInput("runJson", resultJson)
                .WithInput("includePlots", includePlots ? "true" : "false")
                .WithInput("includeHistory", includeHistory ? "true" : "false")
                .WithInput("historySummary", historySummary)
                .WithInput("historySeverity", historySeverity)
                .WithInput("historyMetrics", historyMetrics);

            return builder;
        });

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Typst compilation failed: {result.ErrorMessage}");
        }

        return result.Output.ToArray();
    }

    private static string FormatHistoryMetrics(DutHistoryReport history)
    {
        if (history.Metrics.Count == 0)
        {
            return string.Empty;
        }

        var lines = history.Metrics.Select(m =>
        {
            var prior = m.PriorMean?.ToString("G6", CultureInfo.InvariantCulture) ?? "n/a";
            var pct = m.PercentDelta is { } d
                ? d.ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : "n/a";
            return $"- {m.Channel}: current={m.CurrentMean.ToString("G6", CultureInfo.InvariantCulture)}, prior={prior}, delta={pct}, {m.Severity}";
        });
        return string.Join("\n", lines);
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

    private static bool TryStatusTemplateAlias(string templateName, out string alias)
    {
        if (string.Equals(templateName, "test-report.typ", StringComparison.OrdinalIgnoreCase))
        {
            alias = "status-report.typ";
            return true;
        }

        if (string.Equals(templateName, "status-report.typ", StringComparison.OrdinalIgnoreCase))
        {
            alias = "test-report.typ";
            return true;
        }

        alias = string.Empty;
        return false;
    }

    private string LoadReportFile(string fileName, bool preferLibSubfolder = false)
    {
        // Template names are single file names only — reject traversal before combining.
        var safeName = Path.GetFileName(fileName.Replace('\\', '/').Trim());
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
        {
            throw new InvalidOperationException($"Invalid report template name '{fileName}'.");
        }

        if (!string.IsNullOrWhiteSpace(_settings.DataDirectory))
        {
            var reportsRoot = Path.Combine(_settings.DataDirectory, "reports");
            string[] candidates = preferLibSubfolder
                ?
                [
                    Path.Combine(reportsRoot, "lib", safeName),
                    Path.Combine(reportsRoot, safeName),
                ]
                :
                [
                    Path.Combine(reportsRoot, safeName),
                    Path.Combine(reportsRoot, "lib", safeName),
                ];
            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (!PathContainment.IsUnderRoot(reportsRoot, full))
                {
                    continue;
                }

                if (File.Exists(full))
                {
                    return File.ReadAllText(full, Encoding.UTF8);
                }
            }
        }

        return LoadEmbeddedResource(safeName);
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
