namespace HardwareTest.Core.IO;

/// Ensures a resolved path stays under an intended root directory.
public static class PathContainment
{
    /// Returns true when <paramref name="candidate"/> is the root or a path under it.
    public static bool IsUnderRoot(string rootDirectory, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        var root = NormalizeRoot(rootDirectory);
        var candidate = Path.GetFullPath(candidatePath);
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = Path.GetRelativePath(root, candidate);
        return !string.IsNullOrEmpty(relative)
               && !Path.IsPathRooted(relative)
               && !relative.StartsWith("..", StringComparison.Ordinal);
    }

    /// Resolves <paramref name="candidatePath"/> and throws when it escapes the root.
    public static string EnsureUnderRoot(string rootDirectory, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        if (!IsUnderRoot(rootDirectory, full))
        {
            throw new InvalidOperationException(
                $"Path escapes intended root '{rootDirectory}': '{candidatePath}'.");
        }

        return full;
    }

    /// Combines root + relative segments and verifies the result stays under root.
    public static string CombineUnderRoot(string rootDirectory, params string[] relativeSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(relativeSegments);

        var root = NormalizeRoot(rootDirectory);
        var combined = relativeSegments.Length == 0
            ? root
            : Path.GetFullPath(Path.Combine([root, .. relativeSegments]));
        return EnsureUnderRoot(root, combined);
    }

    /// Root with trailing separator so prefix comparisons cannot match a sibling directory.
    public static string NormalizeRoot(string rootDirectory)
    {
        var full = Path.GetFullPath(rootDirectory);
        return full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)
            ? full
            : full + Path.DirectorySeparatorChar;
    }
}
