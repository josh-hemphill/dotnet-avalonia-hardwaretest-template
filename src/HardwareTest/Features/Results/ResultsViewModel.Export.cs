using HardwareTest.Core.Credentials;
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

                files.AddRange(
                    OpenedRun.Reports
                        .Where(r => !string.IsNullOrWhiteSpace(r.PdfPath) && File.Exists(r.PdfPath))
                        .Select(r => (r.PdfPath, Path.GetFileName(r.PdfPath))));

                if (!string.IsNullOrWhiteSpace(OpenedRun.ReportPdfPath)
                    && File.Exists(OpenedRun.ReportPdfPath)
                    && files.All(f => !string.Equals(f.SourcePath, OpenedRun.ReportPdfPath, StringComparison.OrdinalIgnoreCase)))
                {
                    files.Add((OpenedRun.ReportPdfPath!, Path.GetFileName(OpenedRun.ReportPdfPath)));
                }

                if (Directory.Exists(runDir))
                {
                    foreach (var sidecar in Directory.EnumerateFiles(runDir, "*.attestation.json"))
                    {
                        files.Add((sidecar, Path.GetFileName(sidecar)));
                    }
                }

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
}
