using System.Text.Json;
using HardwareTest.OpenTap.Host;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    // Rollback handles replacement exceptions; the two files are not a crash-safe transaction.
    private void WritePlanAndSidecar(TestPlan plan, string tapPlanPath, ProgramSidecar sidecar)
    {
        var tapFull = Path.GetFullPath(tapPlanPath);
        var sidecarFull = SidecarPath(tapFull);
        var tapTemp = tapFull + ".saving";
        var sidecarTemp = sidecarFull + ".saving";
        try
        {
            plan.Save(tapTemp);
            File.WriteAllText(
                sidecarTemp,
                JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));
            var tapBackup = CreatePreimageBackup(tapFull);
            var sidecarBackup = CreatePreimageBackup(sidecarFull);
            try
            {
                _replaceFile(tapTemp, tapFull);
                _replaceFile(sidecarTemp, sidecarFull);
            }
            catch (Exception originalError)
            {
                var rollbackErrors = new List<Exception>();
                TryRestoreFile(tapFull, tapBackup, rollbackErrors);
                TryRestoreFile(sidecarFull, sidecarBackup, rollbackErrors);
                if (rollbackErrors.Count > 0)
                {
                    originalError.Data["AuthoringRecoveryBackups"] = new[] { tapBackup, sidecarBackup }
                        .Where(path => path is not null).ToArray();
                    originalError.Data["AuthoringRecoveryAction"] =
                        "Restore the retained .preimage files to their original paths before exporting again.";
                    originalError.Data["AuthoringRollbackErrors"] = rollbackErrors.ToArray();
                }
                else
                {
                    if (tapBackup is not null) TryDeleteFile(tapBackup);
                    if (sidecarBackup is not null) TryDeleteFile(sidecarBackup);
                }
                throw;
            }
            if (tapBackup is not null) TryDeleteFile(tapBackup);
            if (sidecarBackup is not null) TryDeleteFile(sidecarBackup);
        }
        finally
        {
            TryDeleteFile(tapTemp);
            TryDeleteFile(sidecarTemp);
        }
    }

    private void WriteSidecar(string tapPlanPath, ProgramSidecar sidecar)
    {
        var sidecarFull = SidecarPath(Path.GetFullPath(tapPlanPath));
        var sidecarTemp = sidecarFull + ".saving";
        try
        {
            var json = JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar);
            File.WriteAllText(sidecarTemp, json);
            _replaceFile(sidecarTemp, sidecarFull);
        }
        finally
        {
            TryDeleteFile(sidecarTemp);
        }
    }

    private static string? CreatePreimageBackup(string path)
    {
        if (!File.Exists(path)) return null;
        var backup = path + "." + Guid.NewGuid().ToString("N") + ".preimage";
        using var original = File.OpenRead(path);
        using var durable = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        original.CopyTo(durable);
        durable.Flush(flushToDisk: true);
        return backup;
    }

    private static void TryRestoreFile(string path, string? backup, List<Exception> errors)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".restoring";
        try
        {
            if (backup is null)
            {
                File.Delete(path);
                return;
            }

            using (var original = File.OpenRead(backup))
            using (var restored = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                original.CopyTo(restored);
                restored.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
