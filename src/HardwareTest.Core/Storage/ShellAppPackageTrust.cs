using HardwareTest.Core.IO;

namespace HardwareTest.Core.Storage;

/// Launch-time shell-app packages under `{DataDirectory}/shell-app-packages`.
/// Distinct from OpenTAP `{DataDirectory}/plugins` and from app data `{DataDirectory}/shell-apps`.
public static class ShellAppPackageTrust
{
    public const string FolderName = "shell-app-packages";

    /// `{dataDirectory}/shell-app-packages`, or empty when the data directory is unset.
    public static string TrustedRoot(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return string.Empty;
        }

        return PathContainment.CombineUnderRoot(dataDirectory, FolderName);
    }

    /// True when <paramref name="candidatePath"/> is under the trusted packages root.
    public static bool Allows(string? dataDirectory, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
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
