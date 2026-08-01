using HardwareTest.Core.Storage;

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

    private void ExportPackage()
    {
        if (OpenedRun is null)
        {
            Status = "Open a run first.";
            return;
        }

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

            var csvDir = Path.Combine(runDir, "opentap-results");
            if (Directory.Exists(csvDir))
            {
                files.AddRange(
                    Directory.EnumerateFiles(csvDir, "*.csv")
                        .Select(csv => (csv, Path.Combine("opentap-results", Path.GetFileName(csv)))));
            }

            if (files.Count == 0)
            {
                Status = "Nothing to export for this run.";
                return;
            }

            var packageName = $"run-{OpenedRun.RunId}";
            var dest = _exportTargets.ExportPackage(target, packageName, files);
            Status = $"Exported package to {dest}";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }
}
