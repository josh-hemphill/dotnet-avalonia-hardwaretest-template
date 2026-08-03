namespace HardwareTest.Core.IO;

/// Portable file-name sanitization (Windows-invalid chars even when running on Linux CI).
public static class PortableFileNames
{
    private static readonly char[] ExtraInvalid =
    [
        '"', '<', '>', '|', '\0', ':', '*', '?', '\\', '/',
    ];

    /// Replaces path-invalid filename characters with underscores for cross-platform run folders.
    /// Rejects empty, ".", and ".." (returns a stable fallback) so Path.Combine cannot escape a root.
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        var invalid = Path.GetInvalidFileNameChars()
            .Concat(ExtraInvalid)
            .Distinct()
            .ToArray();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }

        // Control characters 1–31 (Windows-invalid).
        for (var i = 1; i <= 31; i++)
        {
            name = name.Replace((char)i, '_');
        }

        name = name.Trim();
        if (string.IsNullOrEmpty(name) || name is "." or "..")
        {
            return "_";
        }

        return name;
    }
}
