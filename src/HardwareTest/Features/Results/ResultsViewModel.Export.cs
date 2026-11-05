using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Storage;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel
{
    private ExportTarget? _requestedExportTarget;

    private async Task RefreshResultsAndExportTargetsAsync()
    {
        await RefreshAsync().ConfigureAwait(false);
        await RunOnUiAsync(RefreshAvailableExportTargets).ConfigureAwait(false);
    }

    private void RefreshAvailableExportTargets()
    {
        var previous = SelectedExportTarget;
        try
        {
            // A refresh never chooses another destination on the operator's behalf.
            // The target captured for a pending certification request remains independent.
            RefreshExportTargets(previous, selectDefault: false);
            if (previous is not null && SelectedExportTarget is null)
                Status = $"Selected export destination is no longer available: {previous.DisplayName}. Choose a destination and retry.";
        }
        catch (Exception ex)
        {
            ExportTargets.Clear();
            SelectedExportTarget = null;
            HasExportTargets = false;
            Status = $"Cannot refresh export destinations: {ex.Message}. Correct Settings or reconnect the destination, then Refresh and choose a destination.";
        }
    }

    private void RefreshExportTargets(ExportTarget? previousTarget = null, bool selectDefault = true)
    {
        ExportTargets.Clear();
        if (_exportTargets is null)
        {
            HasExportTargets = false;
            SelectedExportTarget = null;
            return;
        }

        foreach (var target in _exportTargets.ListTargets())
        {
            ExportTargets.Add(target);
        }

        HasExportTargets = ExportTargets.Count > 0;
        SelectedExportTarget = previousTarget is null
            ? selectDefault ? ExportTargets.FirstOrDefault() : null
            : ExportTargets.FirstOrDefault(target => SameExportTarget(target, previousTarget));
    }

    private static bool SameExportTarget(ExportTarget left, ExportTarget right)
        => string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && ReportAttestationService.PathEquals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left.RootPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right.RootPath)));

    private Task ExportPackageAsync()
    {
        if (OpenedRun is null)
        {
            Status = "Open a run first.";
            return Task.CompletedTask;
        }

        // Keep this destination with the request while a badge/PIN may be pending.
        _requestedExportTarget = SelectedExportTarget;
        _pendingPrintPath = null;
        if (!TryBeginCertifiedAction(OpenedRun, ReportAttestationService.PackageKind, PendingExport))
        {
            return Task.CompletedTask;
        }

        ExportPackageCore();
        return Task.CompletedTask;
    }

    private void ExportPackageCore(TestRunRecord? requestedRun = null)
    {
        var run = requestedRun ?? OpenedRun;
        if (run is null)
        {
            Status = "Open a run first.";
            return;
        }

        if (!_busyGate.Wait(0))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var requestedTarget = _requestedExportTarget;
            _requestedExportTarget = null;
            RefreshExportTargets(requestedTarget, selectDefault: false);
            var target = SelectedExportTarget;
            if (target is null || _exportTargets is null)
            {
                Status = requestedTarget is not null
                    ? $"Selected export destination is no longer available: {requestedTarget.DisplayName}. Choose a destination and retry."
                    : HasExportTargets
                        ? "Choose an export destination and retry."
                        : "No export target available. Set ExportDirectory or insert removable media.";
                return;
            }

            try
            {
                var runDir = _runStore.GetRunDirectory(run.RunId);
                var files = new List<(string SourcePath, string RelativeName)>();
                var runJson = Path.Combine(runDir, "run.json");
                var incomplete = ExportEvidenceProblem(run, runJson);
                if (incomplete is not null)
                {
                    Status = incomplete;
                    return;
                }

                files.Add((runJson, "run.json"));
                files.AddRange(CollectExportReportFiles(run));

                var csvDir = Path.Combine(runDir, "opentap-results");
                if (Directory.Exists(csvDir))
                {
                    files.AddRange(
                        Directory.EnumerateFiles(csvDir, "*.csv")
                            .Select(csv => (csv, Path.Combine("opentap-results", Path.GetFileName(csv)))));
                }

                var diagnosticsPath = Path.Combine(Path.GetTempPath(), $"hwtest-diag-{run.RunId}.txt");
                try
                {
                    File.WriteAllText(diagnosticsPath, BuildExportDiagnostics(run));
                    files.Add((diagnosticsPath, "diagnostics.txt"));

                    var packageName = $"run-{run.RunId}";
                    var dest = _exportTargets.ExportPackage(target, packageName, files);
                    Status = $"Exported package to {dest}";
                }
                finally
                {
                    TryDeleteTemp(diagnosticsPath);
                }
            }
            catch (Exception ex)
            {
                Status = $"Export failed: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
            _busyGate.Release();
        }
    }

    private static string? ExportEvidenceProblem(TestRunRecord run, string runJson)
    {
        if (!File.Exists(runJson))
        {
            return $"Export blocked: saved run.json is missing for {run.RunId}. Restore this run's saved data or choose another run, then retry export. No package was written.";
        }

        // The package includes declared history as well as the current report. Do not
        // silently replace missing issued evidence with a working PDF or a partial copy.
        foreach (var report in run.Reports)
        {
            var issued = ReportArtifactRoles.IsIssued(report.Role);
            if (string.IsNullOrWhiteSpace(report.PdfPath) || !File.Exists(report.PdfPath))
            {
                return issued
                    ? $"Export blocked: the declared issued {report.Kind} PDF ({report.RevisionId ?? "unrevisioned"}) is missing. Restore the original issued file or choose another run, then retry export. No package was written."
                    : $"Export blocked: the declared working {report.Kind} PDF is missing. Regenerate working reports or restore the missing file, then retry export. No package was written.";
            }

            if (issued && !string.IsNullOrWhiteSpace(report.RunSnapshotPath) && !File.Exists(report.RunSnapshotPath))
            {
                return "Export blocked: a declared issued report snapshot is missing. Restore the original report evidence or choose another run, then retry export. No package was written.";
            }
        }

        if (run.Attestations.Any(stamp => !string.IsNullOrWhiteSpace(stamp.SidecarPath) && !File.Exists(stamp.SidecarPath)))
        {
            return "Export blocked: a declared report attestation sidecar is missing. Restore the original report evidence or choose another run, then retry export. No package was written.";
        }

        return null;
    }

    private string BuildExportDiagnostics(TestRunRecord? requestedRun = null)
    {
        var run = requestedRun ?? OpenedRun;
        var block = _buildInfo?.FormatSupportBlock() ?? "HardwareTest diagnostics";
        var catalog = ProgramCatalog.SelfCheck();
        var catalogBlock = catalog.Count == 0
            ? "Catalog self-check: ok"
            : "Catalog self-check:" + Environment.NewLine + string.Join(Environment.NewLine, catalog);
        return string.Join(
            Environment.NewLine,
            block,
            $"RunId: {run?.RunId}",
            $"PlanId: {run?.PlanId}",
            $"ExecutionScope: {run?.ExecutionScopeText ?? "Not recorded"}",
            $"Result: {run?.Result}",
            $"Backend execution: {run?.ReferenceBackendResult?.ToString() ?? "Not separately recorded"}",
            run is null ? string.Empty : HardwareTest.Core.BenchReferences.BenchReferenceEvidence.Describe(run),
            $"SchemaVersion: {run?.StoredSchemaVersion}",
            $"AppVersion: {run?.AppVersion ?? "unknown"}",
            catalogBlock);
    }

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not delete temp diagnostics '{path}': {ex.Message}");
        }
    }

    /// Latest issued PDFs export as `{kind}.pdf`, working copies under `working/`, and revision evidence under `history/`.
    public static IEnumerable<(string SourcePath, string RelativeName)> CollectExportReportFiles(TestRunRecord run)
    {
        var files = new List<(string SourcePath, string RelativeName)>();
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? source, string destination)
        {
            if (!string.IsNullOrWhiteSpace(source) && File.Exists(source) && destinations.Add(destination))
                files.Add((source, destination));
        }
        foreach (var group in run.Reports.GroupBy(r => r.Kind, StringComparer.OrdinalIgnoreCase))
        {
            var issued = ReportRevisions.Latest(run, group.Key);
            var working = group.FirstOrDefault(r => ReportArtifactRoles.IsWorking(r.Role));
            var kind = Uri.EscapeDataString(group.Key);
            if (issued is not null)
            {
                Add(issued.PdfPath, $"{kind}.pdf");
                Add(working?.PdfPath, Path.Combine("working", $"{kind}.pdf"));
            }
            else
                Add(working?.PdfPath, $"{kind}.pdf");
            foreach (var revision in group.Where(r => ReportArtifactRoles.IsIssued(r.Role)))
            {
                var id = Uri.EscapeDataString(revision.RevisionId ?? Path.GetFileNameWithoutExtension(revision.PdfPath) ?? "unidentified");
                var history = Path.Combine("history", kind, id);
                Add(revision.PdfPath, Path.Combine(history, $"{kind}.pdf"));
                Add(revision.RunSnapshotPath, Path.Combine(history, "run.snapshot.json"));
                var stamp = ReportAttestationService.FindForArtifact(run, revision);
                Add(stamp?.SidecarPath, Path.Combine(history, $"{kind}.attestation.json"));
                if (ReferenceEquals(revision, issued)) Add(stamp?.SidecarPath, $"{kind}.attestation.json");
            }
        }

        return files;
    }
}
