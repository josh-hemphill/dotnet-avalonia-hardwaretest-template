using System.Text.Json;

namespace HardwareTest.Core.Serialization;

public enum DocumentSchemaKind
{
    Current = 0,
    Unsupported = 1,
    FutureReadOnly = 2,
}

public sealed class DocumentSchemaStatus
{
    public required string DocumentType { get; init; }
    public required int StoredVersion { get; init; }
    public required int CurrentVersion { get; init; }
    public required DocumentSchemaKind Kind { get; init; }
    public string? WriterAppVersion { get; init; }
    public bool IsReadOnly => Kind == DocumentSchemaKind.FutureReadOnly;

    public string FormatOperatorWarning()
    {
        if (Kind == DocumentSchemaKind.Unsupported)
        {
            return $"Unsupported {DocumentType} schema {StoredVersion}; this app requires schema {CurrentVersion}. "
                   + "Use a current document. The original file has been preserved.";
        }

        if (!IsReadOnly) return string.Empty;
        var writer = string.IsNullOrWhiteSpace(WriterAppVersion) ? "unknown" : WriterAppVersion;
        return $"Read-only: {DocumentType} schema {StoredVersion} is newer than this app ({CurrentVersion}). "
               + $"Written by app version {writer}. Do not overwrite.";
    }
}

/// Checks the persisted header before model defaults can hide a missing version.
public static class DocumentSchemaGate
{
    public static DocumentSchemaStatus Evaluate(string documentType, int storedVersion, int currentVersion, string? writerAppVersion = null)
        => new()
        {
            DocumentType = documentType,
            StoredVersion = storedVersion,
            CurrentVersion = currentVersion,
            Kind = storedVersion == currentVersion ? DocumentSchemaKind.Current
                : storedVersion > currentVersion ? DocumentSchemaKind.FutureReadOnly : DocumentSchemaKind.Unsupported,
            WriterAppVersion = writerAppVersion,
        };

    public static DocumentSchemaStatus ReadHeader(ReadOnlyMemory<byte> json, string documentType, int currentVersion, string? path = null)
    {
        using var document = JsonDocument.Parse(json);
        var status = ReadElementHeader(document.RootElement, documentType, currentVersion, path);
        if (documentType == SchemaDocumentTypes.SuiteRunRecord
            && document.RootElement.TryGetProperty("planRuns", out var children))
        {
            if (children.ValueKind != JsonValueKind.Array) throw new JsonException("planRuns must be an array.");
            foreach (var child in children.EnumerateArray())
                ReadElementHeader(child, SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord, path);
        }
        return status;
    }

    private static DocumentSchemaStatus ReadElementHeader(JsonElement root, string documentType, int currentVersion, string? path)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException($"{documentType} must be a JSON object.");
        var version = 0;
        if (root.TryGetProperty("schemaVersion", out var header)
            && (header.ValueKind != JsonValueKind.Number || !header.TryGetInt32(out version)))
            throw new JsonException($"{documentType} schemaVersion must be an integer.");
        var writer = root.TryGetProperty("appVersion", out var appVersion) && appVersion.ValueKind == JsonValueKind.String
            ? appVersion.GetString() : null;
        var status = Evaluate(documentType, version, currentVersion, writer);
        if (status.Kind == DocumentSchemaKind.Unsupported) throw new UnsupportedDocumentSchemaException(status, path);
        return status;
    }

    public static void RequireWritable(string documentType, int storedVersion, int currentVersion, string? path = null, string? writerAppVersion = null)
    {
        RequireCurrent(Evaluate(documentType, storedVersion, currentVersion, writerAppVersion), path);
        if (path is null) return;
        var destination = File.Exists(path) ? path : path + ".bak";
        if (File.Exists(destination))
            RequireCurrentHeader(File.ReadAllBytes(destination), documentType, currentVersion, destination);
    }

    public static void RequireCurrentHeader(ReadOnlyMemory<byte> json, string documentType, int currentVersion, string? path = null)
    {
        RequireCurrent(ReadHeader(json, documentType, currentVersion, path), path);
        if (documentType != SchemaDocumentTypes.SuiteRunRecord) return;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("planRuns", out var children)) return;
        foreach (var child in children.EnumerateArray())
            RequireCurrent(ReadElementHeader(child, SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord, path), path);
    }

    private static void RequireCurrent(DocumentSchemaStatus status, string? path)
    {
        if (status.Kind == DocumentSchemaKind.Unsupported) throw new UnsupportedDocumentSchemaException(status, path);
        if (status.IsReadOnly) throw new SchemaReadOnlyException(status);
    }
}

public sealed class UnsupportedDocumentSchemaException : InvalidOperationException
{
    public UnsupportedDocumentSchemaException(DocumentSchemaStatus status, string? path = null)
        : base(status.FormatOperatorWarning() + (path is null ? string.Empty : $" File: {path}")) => Status = status;
    public DocumentSchemaStatus Status { get; }
}

public sealed class SchemaReadOnlyException : InvalidOperationException
{
    public SchemaReadOnlyException(DocumentSchemaStatus status) : base(status.FormatOperatorWarning()) => Status = status;
    public DocumentSchemaStatus Status { get; }
}
