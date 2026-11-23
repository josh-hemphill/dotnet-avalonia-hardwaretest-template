using System.Text.Json;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    private void WritePlanAndSidecar(Action<Stream> writePlan, string tapPlanPath, ProgramSidecar sidecar,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var tapFull = Path.GetFullPath(tapPlanPath);
        var files = new List<(string Path, Action<Stream> Write)>
        {
            (tapFull, writePlan),
            (SidecarPath(tapFull), stream => JsonSerializer.Serialize(stream, sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar))
        };
        if (data is not null)
            foreach (var (relative, contents) in data.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var path = AuthoringProgramAssets.ResolvePath(Path.GetDirectoryName(tapFull)!, relative);
                if (!File.Exists(path) || File.ReadAllText(path) != contents)
                    files.Add((path, stream => stream.Write(System.Text.Encoding.UTF8.GetBytes(contents))));
            }
        WriteCompilation(files, data is not null);
    }

    // Replacement failures roll back all replaced files. Source hashes detect an incomplete
    // compilation on the next open if a process is killed between replacements.
    private void WriteCompilation(IReadOnlyList<(string Path, Action<Stream> Write)> files, bool importedProcedure)
    {
        var staged = new List<string>();
        var backups = new List<string?>();
        var touched = 0;
        try
        {
            foreach (var (path, write) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = importedProcedure ? path + "." + Guid.NewGuid().ToString("N") + ".saving" : path + ".saving";
                using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                staged.Add(temporary);
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            foreach (var (path, _) in files) backups.Add(CreatePreimageBackup(path));
            for (var index = 0; index < files.Count; index++)
            {
                touched++;
                _replaceFile(staged[index], files[index].Path);
            }
        }
        catch (Exception originalError)
        {
            var rollbackErrors = new List<Exception>();
            for (var index = touched - 1; index >= 0; index--)
                TryRestoreFile(files[index].Path, backups[index], rollbackErrors);
            if (rollbackErrors.Count > 0)
            {
                originalError.Data["AuthoringRecoveryBackups"] = backups.OfType<string>().ToArray();
                originalError.Data["AuthoringRecoveryAction"] = "Restore the retained .preimage files to their original paths before exporting again.";
                originalError.Data["AuthoringRollbackErrors"] = rollbackErrors.ToArray();
                backups.Clear();
            }
            throw;
        }
        finally
        {
            foreach (var temporary in staged) TryDeleteFile(temporary);
            foreach (var backup in backups.OfType<string>()) TryDeleteFile(backup);
        }
    }

    private void WriteSidecar(string tapPlanPath, ProgramSidecar sidecar)
    {
        var sidecarFull = SidecarPath(Path.GetFullPath(tapPlanPath));
        var sidecarTemp = sidecarFull + ".saving";
        var sidecarTempOwned = false;
        try
        {
            using (var stream = new FileStream(sidecarTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                sidecarTempOwned = true;
                JsonSerializer.Serialize(stream, sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar);
                stream.Flush(flushToDisk: true);
            }
            _replaceFile(sidecarTemp, sidecarFull);
        }
        finally
        {
            if (sidecarTempOwned) TryDeleteFile(sidecarTemp);
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
