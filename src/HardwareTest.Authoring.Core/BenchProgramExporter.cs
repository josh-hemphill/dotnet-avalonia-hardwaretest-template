using System.Text.Json;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed record BenchProgramExportResult(string Directory, string PlanPath, BenchProgramReceipt Receipt);

/// Publishes one current compiled pair as a self-contained, checked bench folder.
public static class BenchProgramExporter
{
    public static BenchProgramExportResult Export(AuthoringWorkspace workspace, string planId, string destinationRoot,
        Func<bool>? ownerIsCurrent = null, CancellationToken cancellationToken = default)
    {
        BenchProgramHandoff.RequireCustomPlanId(planId);
        var included = new HashSet<string>([planId], StringComparer.OrdinalIgnoreCase);
        void RequireCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ownerIsCurrent is not null && !ownerIsCurrent())
                throw new AuthoringWorkspaceException("The selected authoring context changed. Reopen Prepare bench files for the intended program.");
            var issues = AuthoringSourceExportGuard.GetIssues(workspace, included);
            if (issues.Count > 0) throw new AuthoringWorkspaceException(string.Join(Environment.NewLine, issues.Select(issue => issue.DisplayText)));
        }
        RequireCurrent();
        var sourcePath = workspace.TapPlanPaths.SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == planId)
            ?? throw new AuthoringWorkspaceException("Save and compile the selected program before preparing bench files.");
        var sidecarPath = PlanCompiler.SidecarPath(sourcePath);
        if (!File.Exists(sidecarPath)) throw new AuthoringWorkspaceException("Save and compile the program's operator settings before preparing bench files.");
        var planBytes = File.ReadAllBytes(sourcePath);
        var sidecarBytes = File.ReadAllBytes(sidecarPath);
        var sidecar = JsonSerializer.Deserialize(sidecarBytes, ProgramCatalogJsonContext.Default.ProgramSidecar)
            ?? throw new AuthoringWorkspaceException("The selected program's operator settings are empty. Save and compile them before preparing bench files.");
        using var checks = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        if (PlanContractCli.Run([sourcePath], checks, new PlanContractOptions { Strict = true, EnablePhysicalExecution = false }) != 0)
            throw new AuthoringWorkspaceException("Resolve the selected program's validation issues before preparing bench files.\n" + checks);
        var source = new AuthoringDocumentStore(workspace.Root).Load(planId);
        var receipt = new BenchProgramReceipt
        {
            PlanId = planId,
            ExportId = Guid.NewGuid().ToString("N"),
            ExportedAt = DateTimeOffset.UtcNow,
            SourceRevision = source.Document?.Revision,
            PlanSha256 = BenchProgramHandoff.Hash(planBytes),
            SidecarSha256 = BenchProgramHandoff.Hash(sidecarBytes),
            RequiredOperatorCapability = sidecar.ReferenceRules is { Count: > 0 } ? "bench-references-v1" : string.Empty,
        };
        var root = Path.GetFullPath(destinationRoot);
        var destination = Path.Combine(root, planId);
        if (Path.GetFullPath(Path.GetDirectoryName(sourcePath)!) == destination)
            throw new AuthoringWorkspaceException("Choose the bench Programs folder, not the authoring source directory.");
        Directory.CreateDirectory(root);
        using var publicationLock = new FileStream(Path.Combine(root, $".bench-export-{planId}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Directory.Exists(destination))
        {
            var priorPlan = Path.Combine(destination, planId + ".TapPlan");
            if (!File.Exists(Path.Combine(destination, BenchProgramHandoff.ReceiptFileName)) || BenchProgramHandoff.ValidateProgram(priorPlan)?.PlanId != planId)
                throw new AuthoringWorkspaceException("The destination contains unrelated or changed files. Choose another bench Programs folder; existing files were preserved.");
            var expected = new HashSet<string>([planId + ".TapPlan", planId + ".program.json", BenchProgramHandoff.ReceiptFileName], StringComparer.OrdinalIgnoreCase);
            if (Directory.EnumerateFileSystemEntries(destination).Any(path => !expected.Contains(Path.GetFileName(path))))
                throw new AuthoringWorkspaceException("The destination contains additional files. Choose another folder before preparing a replacement.");
        }
        var staging = Path.Combine(root, ".bench-stage-" + receipt.ExportId);
        var backup = Path.Combine(root, ".bench-backup-" + receipt.ExportId);
        Directory.CreateDirectory(staging);
        var published = false;
        try
        {
            File.WriteAllBytes(Path.Combine(staging, planId + ".TapPlan"), planBytes);
            File.WriteAllBytes(Path.Combine(staging, planId + ".program.json"), sidecarBytes);
            File.WriteAllText(Path.Combine(staging, BenchProgramHandoff.ReceiptFileName), JsonSerializer.Serialize(receipt, BenchHandoffJsonContext.Default.BenchProgramReceipt));
            BenchProgramHandoff.ValidateProgram(Path.Combine(staging, planId + ".TapPlan"));
            RequireCurrent();
            if (BenchProgramHandoff.Hash(File.ReadAllBytes(sourcePath)) != receipt.PlanSha256
                || BenchProgramHandoff.Hash(File.ReadAllBytes(sidecarPath)) != receipt.SidecarSha256)
                throw new AuthoringWorkspaceException("Compiled files changed while preparing the bench copy. Save/check them and retry.");
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); }
            catch (Exception error)
            {
                if (Directory.Exists(backup))
                {
                    try { Directory.Move(backup, destination); }
                    catch (Exception restoreError)
                    {
                        throw new AuthoringWorkspaceException($"Bench copy failed; the previous copy remains at '{backup}'. Restore it before retrying. {error.Message} {restoreError.Message}");
                    }
                }
                throw;
            }
            published = true;
            return new(destination, Path.Combine(destination, planId + ".TapPlan"), receipt);
        }
        finally
        {
            TryDeleteOwnedDirectory(staging);
            if (published) TryDeleteOwnedDirectory(backup);
        }
    }

    private static void TryDeleteOwnedDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { /* Preserve hidden recovery data if cleanup is unavailable. */ }
        catch (UnauthorizedAccessException) { /* Publication outcome is not changed by cleanup. */ }
    }
}
