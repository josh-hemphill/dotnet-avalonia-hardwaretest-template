using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Authoring;

/// Operator run.json golden bound to an on-disk path.
public sealed record RunDataset(string Path, TestRunRecord Run);

/// Discovers and loads operator run.json goldens. OpenTAP-free ingest via Core.
public static class RunDatasetCatalog
{
    /// Lists `{recordingsDirectory}/**/run.json`. Missing folder is empty, not an error.
    public static IReadOnlyList<RunDataset> List(AuthoringWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var root = ResolveRecordingsRoot(workspace);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var datasets = new List<RunDataset>();
        foreach (var path in Directory.EnumerateFiles(root, "run.json", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                datasets.Add(Load(path));
            }
            catch (Exception ex) when (ex is AuthoringWorkspaceException or JsonException or IOException)
            {
                continue;
            }
        }

        return datasets;
    }

    /// Loads one run.json through Core's JSON context and schema gate. Does not overwrite.
    public static RunDataset Load(string runJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runJsonPath);
        var fullPath = Path.GetFullPath(runJsonPath);
        if (!File.Exists(fullPath))
        {
            throw new AuthoringWorkspaceException($"run.json not found: {fullPath}");
        }

        string raw;
        try
        {
            raw = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Failed to read {fullPath}.", ex);
        }

        TestRunRecord? run;
        try
        {
            run = JsonSerializer.Deserialize(raw, AppJsonContext.Default.TestRunRecord);
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Invalid run.json at {fullPath}: {ex.Message}", ex);
        }

        if (run is null)
        {
            throw new AuthoringWorkspaceException($"Empty run.json at {fullPath}.");
        }

        ApplySchemaGate(run, fullPath);
        return new RunDataset(fullPath, run);
    }

    private static string ResolveRecordingsRoot(AuthoringWorkspace workspace)
    {
        var relative = string.IsNullOrWhiteSpace(workspace.Manifest.RecordingsDirectory)
            ? "recordings"
            : workspace.Manifest.RecordingsDirectory.Trim();
        if (Path.IsPathRooted(relative))
        {
            return Path.GetFullPath(relative);
        }

        try
        {
            return PathContainment.CombineUnderRoot(workspace.Root, relative);
        }
        catch (InvalidOperationException ex)
        {
            throw new AuthoringWorkspaceException(
                $"recordingsDirectory '{relative}' escapes workspace '{workspace.Root}'.",
                ex);
        }
    }

    private static void ApplySchemaGate(TestRunRecord run, string path)
    {
        var status = DocumentSchemaGate.Apply(
            SchemaDocumentTypes.TestRunRecord,
            run.SchemaVersion,
            SchemaVersions.TestRunRecord,
            path,
            run.AppVersion,
            run);
        run.StoredSchemaVersion = status.StoredVersion;
        run.IsLegacy = status.IsLegacy;
        run.IsSchemaReadOnly = status.IsReadOnly;
        if (status.Kind is DocumentSchemaKind.Current or DocumentSchemaKind.UpgradeNeeded)
        {
            run.SchemaVersion = SchemaVersions.TestRunRecord;
        }
    }
}
