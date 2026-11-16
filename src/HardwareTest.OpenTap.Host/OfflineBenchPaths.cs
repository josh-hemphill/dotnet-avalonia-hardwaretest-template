using System.Security.Cryptography;

namespace HardwareTest.OpenTap.Host;

internal static class OfflineBenchPaths
{
    public static string Relative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':'))
            throw new InvalidDataException("Offline payload paths must be portable relative paths.");
        var parts = path.Split('/');
        foreach (var part in parts)
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(character => character < 32 || "<>\"|?*".Contains(character)))
                throw new InvalidDataException($"Unsafe offline payload path: '{path}'.");
            var stem = part.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Reserved offline payload path: '{path}'.");
        }
        return path;
    }

    public static string Under(string root, string relative)
    {
        Relative(relative);
        var result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnder(root, result)) throw new InvalidDataException("Offline payload escapes its owned directory.");
        RejectLinks(result);
        return result;
    }

    public static bool IsUnder(string root, string path)
        => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                || (File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Offline input and destination paths cannot traverse symbolic links or reparse points.");
            if (Path.GetPathRoot(current) == current) break;
        }
    }

    public static IReadOnlyDictionary<string, string> Tree(string root)
    {
        RejectLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Offline directory not found: {root}");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                RejectLinks(path);
                var relative = Relative(Path.GetRelativePath(root, path).Replace('\\', '/'));
                if (!names.Add(relative)) throw new InvalidDataException($"Case-colliding offline payload: {relative}");
                if (Directory.Exists(path)) Walk(path);
                else result.Add(relative, path);
            }
        }
        Walk(root);
        return result;
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string HashBytes(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
