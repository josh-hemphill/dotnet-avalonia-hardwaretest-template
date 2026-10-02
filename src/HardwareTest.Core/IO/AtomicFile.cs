using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HardwareTest.Core.IO;

/// Temp → flush-to-disk → rename writes so power loss cannot leave truncated destination files.
public static class AtomicFile
{
    private static readonly ConcurrentDictionary<string, object> SnapshotGates = new(StringComparer.OrdinalIgnoreCase);
    private static object SnapshotGate(string path) => SnapshotGates.GetOrAdd(Path.GetFullPath(path), _ => new object());

    // A reader keeps a stable open-file snapshot while an atomic writer replaces the directory entry.
    internal static FileStream OpenReadSnapshot(string path)
    {
        lock (SnapshotGate(path)) return OpenSnapshot(path);
    }

    internal static FileStream? TryOpenReadSnapshot(string path)
    {
        lock (SnapshotGate(path)) return File.Exists(path) ? OpenSnapshot(path) : null;
    }

    private static FileStream OpenSnapshot(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// Writes bytes atomically to <paramref name="destinationPath"/>.
    public static async Task WriteAllBytesAsync(
        string destinationPath,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temp, content, cancellationToken).ConfigureAwait(false);
            await FlushToDiskAsync(temp, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Windows replacement can briefly remove the name. Serialize name lookup/open with replacement;
            // existing open snapshots continue to read the previous file without holding this lock.
            lock (SnapshotGate(destinationPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReplaceDestination(temp, destinationPath);
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// Serializes <paramref name="value"/> with source-gen metadata and writes atomically as UTF-8 JSON.
    public static async Task WriteJsonAsync<T>(
        string destinationPath,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        await using var buffer = new MemoryStream();
        await JsonSerializer.SerializeAsync(buffer, value, typeInfo, cancellationToken).ConfigureAwait(false);
        await WriteAllBytesAsync(destinationPath, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    /// Writes UTF-8 text atomically.
    public static Task WriteAllTextAsync(
        string destinationPath,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return WriteAllBytesAsync(destinationPath, Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    private static async Task FlushToDiskAsync(string path, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.None);
        await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
        fs.Flush(flushToDisk: true);
    }

    private static void ReplaceDestination(string tempPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            var attrs = File.GetAttributes(destinationPath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException($"Destination is read-only: {destinationPath}");
            }
            if (OperatingSystem.IsWindows())
            {
                // ReplaceFile supports readers that share deletion; MoveFileEx(overwrite) may reject them.
                File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
                return;
            }
        }

        // Same-directory temp + overwrite rename is atomic on Unix (rename) and uses
        // MoveFileEx for a new Windows destination — avoids delete-then-move data loss.
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort — destination may already own the bytes after a successful move.
        }
    }
}
