namespace HardwareTest.Authoring;

/// Publishes a single durable file, retaining the previous committed bytes as a backup.
public sealed class AuthoringAtomicWriter
{
    private readonly Action<string, string> _replace;
    private readonly Action? _beforeCreate;
    public AuthoringAtomicWriter(Action<string, string>? replace = null, Action? beforeCreate = null)
    {
        _replace = replace ?? ((temporary, destination) => File.Move(temporary, destination, true));
        _beforeCreate = beforeCreate;
    }

    public void WriteNew(string path, ReadOnlySpan<byte> bytes, Action validate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        validate();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".initialize-" + Guid.NewGuid().ToString("N") + ".tmp");
        var ownsTemporary = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            _beforeCreate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            using var ownership = AuthoringPublicationOwnership.Acquire(path, cancellationToken);
            validate();
            cancellationToken.ThrowIfCancellationRequested();
            // No replacement or backup: this operation owns only its staging file.
            File.Move(temporary, path, false);
            ownsTemporary = false;
        }
        finally { if (ownsTemporary) TryDelete(temporary); }
    }

    public void Write(string path, ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A file directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupTemp = path + "." + Guid.NewGuid().ToString("N") + ".backup.tmp";
        try
        {
            WriteFlushed(temp, bytes);
            if (File.Exists(path))
            {
                WriteFlushed(backupTemp, File.ReadAllBytes(path));
                File.Move(backupTemp, path + ".bak", true);
            }
            _replace(temp, path);
        }
        finally
        {
            TryDelete(temp);
            TryDelete(backupTemp);
        }
    }

    private static void WriteFlushed(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
