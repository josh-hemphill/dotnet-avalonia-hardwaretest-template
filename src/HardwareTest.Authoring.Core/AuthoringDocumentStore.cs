using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HardwareTest.Authoring;

public sealed record AuthoringDocumentLoadResult(
    AuthoringDocumentDto? Document, bool IsReadOnly, byte[] OriginalBytes, string? Error, bool Exists)
{
    public bool IsSuccess => Exists && Document is not null && Error is null;
}

/// Version-aware source persistence. Future and corrupt committed files are never silently replaced.
public sealed partial class AuthoringDocumentStore
{
    private readonly string _root;
    private readonly AuthoringAtomicWriter _writer;
    public AuthoringDocumentStore(string workspaceRoot, AuthoringAtomicWriter? writer = null)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _writer = writer ?? new();
        ValidatePath(_root);
    }

    public string GetWorkspacePath() => ValidatePath(Path.Combine(_root, "authoring-drafts", "workspace.authoring.json"));

    public AuthoringWorkspaceLoadResult LoadWorkspace()
    {
        var path = GetWorkspacePath();
        if (!File.Exists(path)) return new(null, false, [], null, false);
        var bytes = File.ReadAllBytes(path);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Authoring source must be a JSON object.");
            if (!json.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
                throw new InvalidDataException("Workspace source has no valid schema version.");
            if (version > AuthoringDocumentDto.CurrentSchemaVersion)
                return new(null, true, bytes, null, true);
            if (version != AuthoringDocumentDto.CurrentSchemaVersion) throw new InvalidDataException("Unsupported workspace source schema.");
            var document = JsonSerializer.Deserialize(bytes, AuthoringDocumentJsonContext.Default.AuthoringWorkspaceDto)
                ?? throw new InvalidDataException("Workspace source is empty.");
            if (document.Manifest is null || document.Manifest.ExcludedProgramIds is null
                || document.Manifest.ExcludedProgramIds.Any(string.IsNullOrWhiteSpace) || document.Revision < 0) throw new InvalidDataException("Workspace source is incomplete.");
            if (document.Manifest.SchemaVersion < 1)
                throw new InvalidDataException("Workspace source has an unsupported manifest schema version.");
            if (document.Manifest.SchemaVersion > AuthoringSchemaVersions.Manifest)
                return new(document, true, bytes, null, true);
            return new(document, false, bytes, null, true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new(null, true, bytes, $"Cannot read workspace authoring source: {ex.Message} Restore its backup or repair the source.", true);
        }
    }

    public void SaveWorkspace(AuthoringManifest manifest, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion < 1 || manifest.SchemaVersion > AuthoringSchemaVersions.Manifest || revision < 0)
            throw new InvalidOperationException("Cannot write a future or invalid workspace source.");
        var existing = LoadWorkspace();
        if (existing.IsReadOnly) throw new InvalidOperationException(existing.Error ?? "Future authoring schemas are read-only; original bytes are preserved.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new AuthoringWorkspaceDto { Manifest = manifest, Revision = revision },
            AuthoringDocumentJsonContext.Default.AuthoringWorkspaceDto);
        var path = GetWorkspacePath();
        ValidatePath(path + ".bak");
        _writer.Write(ValidatePath(path), bytes);
    }

    public string GetDocumentPath(string id)
    {
        ValidateId(id);
        return ValidatePath(Path.Combine(_root, "authoring-drafts", id + ".authoring.json"));
    }

    public string GetRecoveryPath(string id)
    {
        ValidateId(id);
        return ValidatePath(Path.Combine(_root, ".authoring", "recovery", id + ".authoring.json"));
    }

    public IReadOnlyList<string> ListRecoveryIds()
    {
        var directory = ValidatePath(Path.Combine(_root, ".authoring", "recovery"));
        if (!Directory.Exists(directory)) return [];
        var ids = Directory.EnumerateFiles(directory, "*.authoring.json")
            .Select(path => Path.GetFileName(path)[..^".authoring.json".Length]).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        foreach (var id in ids) { ValidateId(id); GetRecoveryPath(id); }
        return ids;
    }

    public IReadOnlyList<string> ListDocumentIds()
    {
        var directory = ValidatePath(Path.Combine(_root, "authoring-drafts"));
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.authoring.json")
            .Select(path => Path.GetFileName(path)[..^".authoring.json".Length])
            .Where(id => id != "workspace")
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    public AuthoringDocumentLoadResult Load(string id) => LoadAtPath(GetDocumentPath(id), id);
    public AuthoringDocumentLoadResult LoadAtPath(string path) => LoadAtPath(path, Path.GetFileName(path).EndsWith(".authoring.json", StringComparison.Ordinal)
        ? Path.GetFileName(path)[..^".authoring.json".Length] : null);
    private AuthoringDocumentLoadResult LoadAtPath(string path, string? expectedId)
    {
        path = ValidatePath(path);
        if (!File.Exists(path)) return new(null, false, [], null, false);
        var bytes = File.ReadAllBytes(path);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Authoring source must be a JSON object.");
            if (!json.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
                throw new InvalidDataException("The draft has no valid schema version.");
            if (version > AuthoringDocumentDto.CurrentSchemaVersion)
                return new(null, true, bytes, null, true);
            if (version != AuthoringDocumentDto.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported draft schema {version}.");
            var document = JsonSerializer.Deserialize(bytes, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto)
                ?? throw new InvalidDataException("The draft document is empty.");
            ValidateId(document.PlanId);
            if (expectedId is not null && document.PlanId != expectedId)
                throw new InvalidDataException("The draft identity does not match its filename.");
            _ = document.ToDraft();
            return new(document, false, bytes, null, true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or NotSupportedException or NullReferenceException)
        {
            return new(null, true, bytes, $"Cannot read authoring draft '{path}': {ex.Message} Restore its backup or explicitly repair the source.", true);
        }
    }

    public void Save(AuthoringDocumentDto document) => SaveAtPath(GetDocumentPath(document.PlanId), document);
    public void SaveAtPath(string path, AuthoringDocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateId(document.PlanId);
        _ = document.ToDraft();
        path = ValidatePath(path);
        if (!string.Equals(Path.GetFileName(path), document.PlanId + ".authoring.json", StringComparison.Ordinal))
            throw new ArgumentException("The authoring source filename must match its program identity.", nameof(path));
        var existing = LoadAtPath(path);
        if (existing.IsReadOnly) throw new InvalidOperationException(existing.Error ?? "Future authoring schemas are read-only; original bytes are preserved.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto);
        // Recheck every destination including backup immediately before creating or replacing files.
        ValidatePath(path + ".bak");
        ValidatePath(path);
        _writer.Write(path, bytes);
    }

    public void DeleteSource(string id)
    {
        var path = GetDocumentPath(id);
        var existing = Load(id);
        if (existing.IsReadOnly) throw new InvalidOperationException(existing.Error ?? "Future authoring schemas are read-only.");
        if (File.Exists(path)) File.Delete(ValidatePath(path));
    }

    public void DeleteRecovery(string id)
    {
        var path = GetRecoveryPath(id);
        ValidatePath(path);
        if (File.Exists(path)) File.Delete(path);
    }

    public static string? ComputeHash(string? path)
        => path is not null && File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

    public static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !SafeId().IsMatch(id) ||
            id.EndsWith('.') || id.Equals("workspace", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Program IDs must be safe filename tokens (letters, numbers, dash, underscore, or dot), and may not be 'workspace'.", nameof(id));
        var stem = id.Split('.')[0];
        if (ReservedName().IsMatch(stem)) throw new ArgumentException("Program ID is a reserved device name.", nameof(id));
    }

    public string ValidatePath(string path)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(_root, comparison) && !full.StartsWith(_root + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Authoring files must remain inside the workspace.", nameof(path));
        // Include existing ancestors of the workspace itself; symlinked roots cannot redirect writes.
        for (string? cursor = full; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            var info = Directory.Exists(cursor) ? (FileSystemInfo)new DirectoryInfo(cursor) : new FileInfo(cursor);
            if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException($"Authoring paths cannot traverse symbolic links: {cursor}");
        }
        return full;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();
}

public sealed record AuthoringWorkspaceLoadResult(AuthoringWorkspaceDto? Document, bool IsReadOnly, byte[] OriginalBytes, string? Error, bool Exists);
