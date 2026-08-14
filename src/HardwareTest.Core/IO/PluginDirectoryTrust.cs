namespace HardwareTest.Core.IO;

/// Extra OpenTAP plugin roots are trusted only under `{DataDirectory}/plugins`
/// unless Engineer debug is on.
public static class PluginDirectoryTrust
{
    public const string FolderName = "plugins";

    /// `{dataDirectory}/plugins`, or empty when the data directory is unset.
    public static string TrustedRoot(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return string.Empty;
        }

        return Path.GetFullPath(Path.Combine(dataDirectory, FolderName));
    }

    /// True when <paramref name="candidatePath"/> may be added to OpenTAP search.
    public static bool Allows(string? dataDirectory, string? candidatePath, bool engineerDebug)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        if (engineerDebug)
        {
            return true;
        }

        var root = TrustedRoot(dataDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            return PathContainment.IsUnderRoot(root, candidatePath);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
