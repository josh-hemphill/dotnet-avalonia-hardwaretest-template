using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.IO;

/// Atomic persistence and recovery for explicitly versioned application documents.
public static class CurrentDocumentFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    public static (T? Document, DocumentSchemaStatus Status) Read<T>(string path, JsonTypeInfo<T> typeInfo, string documentType, int currentVersion)
        => ReadAsync(path, typeInfo, documentType, currentVersion).GetAwaiter().GetResult();

    public static async Task<(T? Document, DocumentSchemaStatus Status)> ReadAsync<T>(
        string path, JsonTypeInfo<T> typeInfo, string documentType, int currentVersion, CancellationToken cancellationToken = default)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[]? primary = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null;
            if (primary is not null)
            {
                DocumentSchemaStatus status;
                try { status = DocumentSchemaGate.ReadHeader(primary, documentType, currentVersion, path); }
                catch (JsonException) { return await RecoverAsync(path, typeInfo, documentType, currentVersion, cancellationToken).ConfigureAwait(false); }
                // A valid unsupported/future primary is never replaced from a backup.
                var containsFutureDocument = status.IsReadOnly;
                if (!containsFutureDocument && documentType == SchemaDocumentTypes.SuiteRunRecord)
                {
                    try { DocumentSchemaGate.RequireCurrentHeader(primary, documentType, currentVersion, path); }
                    catch (SchemaReadOnlyException) { containsFutureDocument = true; }
                }
                if (containsFutureDocument) return Decode(primary, typeInfo, status);
                try { return Decode(primary, typeInfo, status); }
                catch (JsonException) { return await RecoverAsync(path, typeInfo, documentType, currentVersion, cancellationToken).ConfigureAwait(false); }
            }
            if (File.Exists(path + ".bak")) return await RecoverAsync(path, typeInfo, documentType, currentVersion, cancellationToken).ConfigureAwait(false);
            return (default, DocumentSchemaGate.Evaluate(documentType, currentVersion, currentVersion));
        }
        finally { gate.Release(); }
    }

    private static (T? Document, DocumentSchemaStatus Status) Decode<T>(byte[] bytes, JsonTypeInfo<T> typeInfo, DocumentSchemaStatus status)
    {
        var document = JsonSerializer.Deserialize(bytes, typeInfo);
        if (document is null) throw new JsonException($"{status.DocumentType} must contain a document.");
        return (document, status);
    }

    private static async Task<(T? Document, DocumentSchemaStatus Status)> RecoverAsync<T>(
        string path, JsonTypeInfo<T> typeInfo, string documentType, int currentVersion, CancellationToken cancellationToken)
    {
        if (!File.Exists(path + ".bak")) throw new JsonException($"Invalid {documentType} at {path}; no current backup is available.");
        var backup = await File.ReadAllBytesAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
        DocumentSchemaGate.RequireCurrentHeader(backup, documentType, currentVersion, path + ".bak");
        var result = Decode(backup, typeInfo, DocumentSchemaGate.ReadHeader(backup, documentType, currentVersion, path + ".bak"));
        await AtomicFile.WriteAllBytesAsync(path, backup, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// Checks an existing primary or sole backup without restoring or changing either file.
    public static async Task ValidateWriteDestinationAsync<T>(string path, JsonTypeInfo<T> typeInfo,
        string documentType, int currentVersion, CancellationToken cancellationToken = default)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ValidateWriteDestinationCoreAsync(path, typeInfo, documentType, currentVersion, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static async Task ValidateWriteDestinationCoreAsync<T>(string path, JsonTypeInfo<T> typeInfo,
        string documentType, int currentVersion, CancellationToken cancellationToken)
    {
        var destination = File.Exists(path) ? path : path + ".bak";
        if (!File.Exists(destination)) return;
        var previous = await File.ReadAllBytesAsync(destination, cancellationToken).ConfigureAwait(false);
        DocumentSchemaGate.RequireCurrentHeader(previous, documentType, currentVersion, destination);
        Decode(previous, typeInfo, DocumentSchemaGate.ReadHeader(previous, documentType, currentVersion, destination));
    }

    public static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, string documentType,
        int storedVersion, int currentVersion, CancellationToken cancellationToken = default)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DocumentSchemaGate.RequireWritable(documentType, storedVersion, currentVersion, path);
            await ValidateWriteDestinationCoreAsync(path, typeInfo, documentType, currentVersion, cancellationToken).ConfigureAwait(false);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            DocumentSchemaGate.RequireCurrentHeader(bytes, documentType, currentVersion, path);
            // Serialize/validate before changing either file. Only a valid current preimage becomes the backup.
            if (File.Exists(path))
            {
                var previous = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                DocumentSchemaGate.RequireCurrentHeader(previous, documentType, currentVersion, path);
                Decode(previous, typeInfo, DocumentSchemaGate.ReadHeader(previous, documentType, currentVersion, path));
                await AtomicFile.WriteAllBytesAsync(path + ".bak", previous, cancellationToken).ConfigureAwait(false);
            }
            await AtomicFile.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
