namespace HardwareTest.Authoring;

/// Publishes a single durable file, retaining the previous committed bytes as a backup.
public sealed class AuthoringAtomicWriter
{
    private readonly Action<string, string> _replace;
    public AuthoringAtomicWriter(Action<string, string>? replace = null)
        => _replace = replace ?? ((temporary, destination) => File.Move(temporary, destination, true));

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
