using System.Text.Json;

namespace HardwareTest.Authoring;

/// Load/save a planning directory against versioned authoring.json.
public static class AuthoringWorkspaceLoader
{
    public const string ManifestFileName = "authoring.json";

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
    {
        "$schema",
        "schemaVersion",
        "displayName",
        "plansDirectory",
        "package",
        "dependencies",
        "optionalDependencies",
        "instrumentComponentsPackage",
        "pluginProjects",
        "shellAppProjects",
        "includeTui",
        "recordingsDirectory",
        "catalogs",
    };

    public static AuthoringWorkspace Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Invalid workspace root '{root}'.", ex);
        }

        if (!Directory.Exists(fullRoot))
        {
            throw new AuthoringWorkspaceException($"Workspace root not found: {fullRoot}");
        }

        var manifestPath = Path.Combine(fullRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new AuthoringWorkspaceException(
                $"Missing {ManifestFileName} in '{fullRoot}'. Do not invent a product pack.");
        }

        string raw;
        try
        {
            raw = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Failed to read {manifestPath}.", ex);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new AuthoringWorkspaceException($"Empty {ManifestFileName} at {manifestPath}.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Invalid JSON in {manifestPath}: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AuthoringWorkspaceException($"{ManifestFileName} must be a JSON object.");
            }

            var schemaVersion = ReadSchemaVersion(document.RootElement, manifestPath);
            var isFuture = schemaVersion > AuthoringSchemaVersions.Manifest;
            if (!isFuture)
            {
                RejectUnknownProperties(document.RootElement, manifestPath);
            }

            AuthoringManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(raw, AuthoringJsonContext.Default.AuthoringManifest)
                    ?? throw new AuthoringWorkspaceException($"Failed to deserialize {manifestPath}.");
            }
            catch (AuthoringWorkspaceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AuthoringWorkspaceException($"Invalid {ManifestFileName}: {ex.Message}", ex);
            }

            manifest.SchemaVersion = schemaVersion;
            var plansDirectory = ResolvePlansDirectory(fullRoot, manifest.PlansDirectory);
            var tapPlans = EnumerateTapPlans(plansDirectory);
            return new AuthoringWorkspace(fullRoot, manifest, tapPlans) { IsReadOnly = isFuture };
        }
    }

    public static void SaveManifest(string root, AuthoringManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(manifest);

        var fullRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(fullRoot);
        var manifestPath = Path.Combine(fullRoot, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var existing = Load(fullRoot);
            if (existing.IsReadOnly)
            {
                throw new AuthoringWorkspaceException(
                    $"Refusing to overwrite future-schema {ManifestFileName} (schema {existing.Manifest.SchemaVersion} > {AuthoringSchemaVersions.Manifest}).");
            }
        }

        if (manifest.SchemaVersion > AuthoringSchemaVersions.Manifest)
        {
            throw new AuthoringWorkspaceException(
                $"Cannot write {ManifestFileName} schema {manifest.SchemaVersion}; this app supports {AuthoringSchemaVersions.Manifest}.");
        }

        if (manifest.SchemaVersion <= 0)
        {
            manifest.SchemaVersion = AuthoringSchemaVersions.Manifest;
        }

        var json = JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest);
        File.WriteAllText(manifestPath, json + Environment.NewLine);
    }

    private static int ReadSchemaVersion(JsonElement root, string manifestPath)
    {
        if (!root.TryGetProperty("schemaVersion", out var versionElement)
            || versionElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new AuthoringWorkspaceException(
                $"{ManifestFileName} at {manifestPath} is missing required schemaVersion.");
        }

        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
        {
            throw new AuthoringWorkspaceException(
                $"{ManifestFileName} schemaVersion must be a positive integer.");
        }

        if (version <= 0)
        {
            throw new AuthoringWorkspaceException(
                $"{ManifestFileName} schemaVersion must be a positive integer (got {version}).");
        }

        return version;
    }

    private static void RejectUnknownProperties(JsonElement root, string manifestPath)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!KnownProperties.Contains(property.Name))
            {
                throw new AuthoringWorkspaceException(
                    $"Unknown property '{property.Name}' in {manifestPath}.");
            }
        }
    }

    private static string ResolvePlansDirectory(string root, string? plansDirectory)
    {
        var relative = AuthoringManifest.RelativePlansDirectory(plansDirectory);
        var combined = Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(root, relative));
        if (!Directory.Exists(combined))
        {
            throw new AuthoringWorkspaceException($"Plans directory not found: {combined}");
        }

        return combined;
    }

    private static IReadOnlyList<string> EnumerateTapPlans(string plansDirectory)
        => Directory.EnumerateFiles(plansDirectory, "*.TapPlan", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
