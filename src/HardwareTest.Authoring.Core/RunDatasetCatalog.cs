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
                     .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(segment => segment.StartsWith(".import-", StringComparison.Ordinal)))
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

        if (run.Samples is null || run.Samples.Any(sample => sample is null))
            throw new AuthoringWorkspaceException($"Invalid run.json at {fullPath}: samples must be an array without null entries.");
        if (run.Events is null || run.Events.Any(mark => mark is null))
            throw new AuthoringWorkspaceException($"Invalid run.json at {fullPath}: events must be an array without null entries.");
        ApplySchemaGate(run, fullPath);
        return new RunDataset(fullPath, run);
    }

    public static string ResolveRecordingsRoot(AuthoringWorkspace workspace)
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

    /// Validate before copying, then publish a new contained directory atomically.
    public static RunDataset Import(AuthoringWorkspace workspace, string sourcePath, string destinationName, string expectedPlanId)
        => Import(workspace, sourcePath, destinationName, expectedPlanId, beforeCopy: null);

    // Narrow filesystem boundary seam permits deterministic source-replacement regression checks.
    internal static RunDataset Import(AuthoringWorkspace workspace, string sourcePath, string destinationName, string expectedPlanId, Action? beforeCopy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPlanId);
        var source = Load(sourcePath);
        RequireExpectedPlan(source.Run, expectedPlanId);
        if (source.Run.IsSchemaReadOnly) throw new AuthoringWorkspaceException("Unsupported recording schema cannot be imported.");
        if (string.IsNullOrWhiteSpace(source.Run.PlanId) || source.Run.Samples.Any(sample => !double.IsFinite(sample.Value) || string.IsNullOrWhiteSpace(sample.EffectiveMetricKey)))
            throw new AuthoringWorkspaceException("Recording requires a plan id and finite sample values.");
        if (string.IsNullOrWhiteSpace(destinationName) || destinationName.StartsWith(".import-", StringComparison.Ordinal) || destinationName != Path.GetFileName(destinationName)
            || destinationName is "." or ".." || destinationName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new AuthoringWorkspaceException("Recording destination must be a single folder name.");
        var root = ResolveRecordingsRoot(workspace);
        if (!PathContainment.IsUnderRoot(workspace.Root, root))
            throw new AuthoringWorkspaceException("Recording imports must remain inside the workspace.");
        for (var directory = new DirectoryInfo(root); directory is not null && PathContainment.IsUnderRoot(workspace.Root, directory.FullName); directory = directory.Parent)
            if (directory.Exists && directory.LinkTarget is not null) throw new AuthoringWorkspaceException("Recording imports cannot traverse linked folders.");
        var destination = PathContainment.CombineUnderRoot(root, destinationName);
        Directory.CreateDirectory(root);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new AuthoringWorkspaceException($"Recording destination already exists: {destinationName}");
        var temporary = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);
            beforeCopy?.Invoke();
            File.Copy(source.Path, Path.Combine(temporary, "run.json"));
            var validated = Load(Path.Combine(temporary, "run.json"));
            RequireExpectedPlan(validated.Run, expectedPlanId);
            if (validated.Run.PlanId != source.Run.PlanId || validated.Run.IsSchemaReadOnly || validated.Run.Samples.Any(sample => !double.IsFinite(sample.Value) || string.IsNullOrWhiteSpace(sample.EffectiveMetricKey)))
                throw new AuthoringWorkspaceException("Recording changed during import; retry.");
            Directory.Move(temporary, destination);
            return new RunDataset(Path.Combine(destination, "run.json"), validated.Run);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static void RequireExpectedPlan(TestRunRecord run, string expectedPlanId)
    {
        if (!string.Equals(run.PlanId, expectedPlanId, StringComparison.OrdinalIgnoreCase))
            throw new AuthoringWorkspaceException("Recording plan id does not match the selected program.");
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
