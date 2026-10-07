using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Storage;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel
{
    private void RefreshExportTargets()
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
        SelectedExportTarget = ExportTargets.FirstOrDefault();
    }

    private Task ExportPackageAsync()
    {
        if (OpenedRun is null)
        {
            Status = "Open a run first.";
            return Task.CompletedTask;
        }

        if (!TryBeginCertifiedAction(OpenedRun, ReportAttestationService.PackageKind, PendingExport))
        {
            return Task.CompletedTask;
        }

        ExportPackageCore();
        return Task.CompletedTask;
    }

    private void ExportPackageCore()
    {
        if (OpenedRun is null)
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
            RefreshExportTargets();
            var target = SelectedExportTarget ?? ExportTargets.FirstOrDefault();
            if (target is null || _exportTargets is null)
            {
                Status = "No export target available. Set ExportDirectory or insert removable media.";
                return;
            }

            try
            {
                var runDir = _runStore.GetRunDirectory(OpenedRun.RunId);
                var files = new List<(string SourcePath, string RelativeName)>();
                var runJson = Path.Combine(runDir, "run.json");
                if (File.Exists(runJson))
                {
                    files.Add((runJson, "run.json"));
                }

                files.AddRange(CollectExportReportFiles(OpenedRun));

                var csvDir = Path.Combine(runDir, "opentap-results");
                if (Directory.Exists(csvDir))
                {
                    files.AddRange(
                        Directory.EnumerateFiles(csvDir, "*.csv")
                            .Select(csv => (csv, Path.Combine("opentap-results", Path.GetFileName(csv)))));
                }

                var diagnosticsPath = Path.Combine(Path.GetTempPath(), $"hwtest-diag-{OpenedRun.RunId}.txt");
                try
                {
                    File.WriteAllText(diagnosticsPath, BuildExportDiagnostics());
                    files.Add((diagnosticsPath, "diagnostics.txt"));

                    if (files.Count == 0)
                    {
                        Status = "Nothing to export for this run.";
                        return;
                    }

                    var packageName = $"run-{OpenedRun.RunId}";
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

    private string BuildExportDiagnostics()
    {
        var block = _buildInfo?.FormatSupportBlock() ?? "HardwareTest diagnostics";
        var catalog = ProgramCatalog.SelfCheck();
        var catalogBlock = catalog.Count == 0
            ? "Catalog self-check: ok"
            : "Catalog self-check:" + Environment.NewLine + string.Join(Environment.NewLine, catalog);
        return string.Join(
            Environment.NewLine,
            block,
            $"RunId: {OpenedRun?.RunId}",
            $"PlanId: {OpenedRun?.PlanId}",
            $"Result: {OpenedRun?.Result}",
            $"SchemaVersion: {OpenedRun?.StoredSchemaVersion}",
            $"AppVersion: {OpenedRun?.AppVersion ?? "unknown"}",
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
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
