using HardwareTest.Core.IO;

namespace HardwareTest.Core.Storage;

/// Contained per-app folders under `{DataDirectory}/shell-apps/{appId}/`.
public static class ShellAppStorage
{
    public const string DirectoryName = "shell-apps";

    /// True when <paramref name="appId"/> is a single portable directory segment.
    public static bool IsSafeAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId is "." or "..")
        {
            return false;
        }

        foreach (var c in appId)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// Resolves and creates `{dataDirectory}/shell-apps/{appId}/`. Unsafe ids fail closed.
    public static string ResolveDirectory(string dataDirectory, string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!IsSafeAppId(appId))
        {
            throw new InvalidOperationException(
                $"Shell app id '{appId}' is not a safe directory name.");
        }

        var path = PathContainment.CombineUnderRoot(dataDirectory, DirectoryName, appId);
        Directory.CreateDirectory(path);
        return path;
    }
}
