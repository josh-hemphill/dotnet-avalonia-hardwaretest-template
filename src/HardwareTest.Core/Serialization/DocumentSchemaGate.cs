using Serilog;

namespace HardwareTest.Core.Serialization;

public enum DocumentSchemaKind
{
    /// Stored version matches the app's current schema.
    Current = 0,
    /// Absent or 0 — pre-versioning document.
    Legacy = 1,
    /// Stored &lt; current; upgrade steps apply (may be identity).
    UpgradeNeeded = 2,
    /// Stored &gt; current — load read-only; never overwrite.
    FutureReadOnly = 3,
}

/// Result of evaluating a document's SchemaVersion against the app's current version.
public sealed class DocumentSchemaStatus
{
    public required string DocumentType { get; init; }
    public required int StoredVersion { get; init; }
    public required int CurrentVersion { get; init; }
    public required DocumentSchemaKind Kind { get; init; }
    public string? WriterAppVersion { get; init; }

    public bool IsLegacy => Kind == DocumentSchemaKind.Legacy;
    public bool IsReadOnly => Kind == DocumentSchemaKind.FutureReadOnly;

    public string FormatOperatorWarning()
    {
        if (Kind != DocumentSchemaKind.FutureReadOnly)
        {
            return string.Empty;
        }

        var writer = string.IsNullOrWhiteSpace(WriterAppVersion) ? "unknown" : WriterAppVersion;
        return $"Read-only: {DocumentType} schema {StoredVersion} is newer than this app ({CurrentVersion}). "
               + $"Written by app version {writer}. Do not overwrite.";
    }
}

/// Shared read-path gate for settings and run stores.
public static class DocumentSchemaGate
{
    public static DocumentSchemaStatus Evaluate(
        string documentType,
        int storedVersion,
        int currentVersion,
        string? writerAppVersion = null)
    {
        DocumentSchemaKind kind;
        if (storedVersion <= 0)
        {
            kind = DocumentSchemaKind.Legacy;
        }
        else if (storedVersion == currentVersion)
        {
            kind = DocumentSchemaKind.Current;
        }
        else if (storedVersion < currentVersion)
        {
            kind = DocumentSchemaKind.UpgradeNeeded;
        }
        else
        {
            kind = DocumentSchemaKind.FutureReadOnly;
        }

        return new DocumentSchemaStatus
        {
            DocumentType = documentType,
            StoredVersion = storedVersion,
            CurrentVersion = currentVersion,
            Kind = kind,
            WriterAppVersion = writerAppVersion,
        };
    }

    /// Applies upgrade steps when needed; logs once for legacy / upgrade / future.
    public static DocumentSchemaStatus Apply(
        string documentType,
        int storedVersion,
        int currentVersion,
        string? path = null,
        string? writerAppVersion = null,
        object? document = null)
    {
        var status = Evaluate(documentType, storedVersion, currentVersion, writerAppVersion);
        var location = string.IsNullOrWhiteSpace(path) ? documentType : path;

        switch (status.Kind)
        {
            case DocumentSchemaKind.Legacy:
                Log.Information(
                    "Loaded legacy {DocumentType} (no SchemaVersion) from {Path}",
                    documentType,
                    location);
                break;
            case DocumentSchemaKind.UpgradeNeeded:
                SchemaUpgradeRegistry.Apply(documentType, storedVersion, currentVersion, document);
                Log.Information(
                    "Upgraded {DocumentType} schema {From} → {To} (identity or registered steps) from {Path}",
                    documentType,
                    storedVersion,
                    currentVersion,
                    location);
                break;
            case DocumentSchemaKind.FutureReadOnly:
                Log.Warning(
                    "Loaded future {DocumentType} schema {Stored} (app supports {Current}) from {Path}; read-only. WriterAppVersion={Writer}",
                    documentType,
                    storedVersion,
                    currentVersion,
                    location,
                    writerAppVersion ?? "unknown");
                break;
        }

        return status;
    }
}

/// Thrown when a write would overwrite a newer-schema document.
public sealed class SchemaReadOnlyException : InvalidOperationException
{
    public SchemaReadOnlyException(DocumentSchemaStatus status)
        : base(status.FormatOperatorWarning())
    {
        Status = status;
    }

    public DocumentSchemaStatus Status { get; }
}
