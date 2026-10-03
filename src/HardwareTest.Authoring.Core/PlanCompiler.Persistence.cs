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
            var tapBackup = File.Exists(tapFull) ? File.ReadAllBytes(tapFull) : null;
            _replaceFile(tapTemp, tapFull);
            try
            {
                _replaceFile(sidecarTemp, sidecarFull);
            }
            catch
            {
                RestoreFile(tapFull, tapBackup);
                throw;
            }
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

    private static void RestoreFile(string path, byte[]? backup)
    {
        if (backup is null)
        {
            File.Delete(path);
            return;
        }

        File.WriteAllBytes(path, backup);
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
