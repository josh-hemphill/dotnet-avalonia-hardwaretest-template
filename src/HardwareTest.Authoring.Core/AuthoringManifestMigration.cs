using System.Text.Json.Nodes;

namespace HardwareTest.Authoring;

/// Explicit, repeatable migration of supported workspace manifests; loading is read-only.
public static class AuthoringManifestMigration
{
    /// Returns true only when an older supported manifest was migrated.
    public static bool Migrate(string root)
        => Migrate(root, (source, destination) => File.Move(source, destination, overwrite: true));

    internal static bool Migrate(string root, Action<string, string> replaceFile)
    {
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var version = workspace.Manifest.SchemaVersion;
        if (workspace.IsReadOnly || version == AuthoringSchemaVersions.Manifest) return false;

        var paths = new AuthoringDocumentStore(workspace.Root);
        var path = paths.ValidatePath(Path.Combine(workspace.Root, AuthoringWorkspaceLoader.ManifestFileName));
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        EnsureBackup(path, version);
        // Version 2 introduces separate source documents. Existing fields retain their meaning.
        json["schemaVersion"] = AuthoringSchemaVersions.Manifest;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".saving";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(json.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            replaceFile(temporary, path);
            return true;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void EnsureBackup(string manifestPath, int schemaVersion)
    {
        var paths = new AuthoringDocumentStore(Path.GetDirectoryName(manifestPath)!);
        paths.ValidatePath(manifestPath);
        var backup = paths.ValidatePath(manifestPath + $".schema-{schemaVersion}.bak");
        if (File.Exists(backup)) return;
        var temporary = backup + "." + Guid.NewGuid().ToString("N") + ".saving";
        try
        {
            using (var source = File.OpenRead(manifestPath))
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            File.Move(temporary, backup);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
