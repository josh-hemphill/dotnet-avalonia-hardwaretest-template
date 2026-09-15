using System.Runtime.Loader;
using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Storage;
using HardwareTest.Shell;

namespace HardwareTest;

/// Loads launch-time shell apps from trusted folders. No assembly scanning of the host.
public static class ShellApplicationLoader
{
    public const string ApplicationFolderName = "shell-apps";
    public const string ManifestFileName = "shell-app.json";

    /// Discovers packages next to the exe and under `{DataDirectory}/shell-app-packages`.
    public static IReadOnlyList<IShellApplication> Load(
        string applicationDirectory,
        string dataDirectory,
        Action<string, Exception>? onError = null,
        Func<string, string, IShellApplication>? create = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var factory = create ?? CreateFromAssembly;
        var apps = new List<IShellApplication>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packageDir in EnumeratePackageDirectories(applicationDirectory, dataDirectory))
        {
            ParsedPackage? parsed;
            try
            {
                parsed = ParsePackage(packageDir);
            }
            catch (Exception ex)
            {
                if (onError is null)
                {
                    throw;
                }

                onError(packageDir, ex);
                continue;
            }

            if (parsed is null)
            {
                continue;
            }

            if (!seenIds.Add(parsed.Manifest.Id))
            {
                throw new InvalidOperationException($"Duplicate shell app id '{parsed.Manifest.Id}'.");
            }

            try
            {
                var app = factory(parsed.AssemblyPath, parsed.Manifest.Type);
                if (!string.Equals(app.Id, parsed.Manifest.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Shell app type '{parsed.Manifest.Type}' id '{app.Id}' does not match manifest '{parsed.Manifest.Id}'.");
                }

                if (app.MinHostAbi > ShellHostAbi.Current)
                {
                    throw new InvalidOperationException(
                        $"Shell app '{app.Id}' requires ABI {app.MinHostAbi}; host ABI is {ShellHostAbi.Current}.");
                }

                apps.Add(app);
            }
            catch (Exception ex)
            {
                if (onError is null)
                {
                    throw;
                }

                onError(packageDir, ex);
            }
        }

        return apps;
    }

    /// Package directories next to the exe and under the trusted data packages root.
    public static IReadOnlyList<string> EnumeratePackageDirectories(
        string applicationDirectory,
        string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var roots = new[]
        {
            PathContainment.CombineUnderRoot(applicationDirectory, ApplicationFolderName),
            ShellAppPackageTrust.TrustedRoot(dataDirectory),
        };

        var dirs = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var candidate in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!ShellAppStorage.IsSafeAppId(name))
                {
                    continue;
                }

                if (!PathContainment.IsUnderRoot(root, candidate))
                {
                    continue;
                }

                dirs.Add(Path.GetFullPath(candidate));
            }
        }

        return dirs;
    }

    private sealed record ParsedPackage(ShellApplicationPackageManifest Manifest, string AssemblyPath);

    private static ParsedPackage? ParsePackage(string packageDir)
    {
        var manifestPath = PathContainment.CombineUnderRoot(packageDir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize(json, ShellApplicationJsonContext.Default.ShellApplicationPackageManifest)
            ?? throw new InvalidOperationException($"Shell app manifest is empty: '{manifestPath}'.");

        if (!ShellAppStorage.IsSafeAppId(manifest.Id))
        {
            throw new InvalidOperationException($"Shell app manifest id '{manifest.Id}' is not a safe directory name.");
        }

        var folderId = Path.GetFileName(packageDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(manifest.Id, folderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Shell app manifest id '{manifest.Id}' does not match folder '{folderId}'.");
        }

        if (!IsSafeAssemblyFileName(manifest.Assembly))
        {
            throw new InvalidOperationException(
                $"Shell app '{manifest.Id}' assembly '{manifest.Assembly}' is not a safe file name.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Type))
        {
            throw new InvalidOperationException($"Shell app '{manifest.Id}' is missing a type name.");
        }

        var assemblyPath = PathContainment.CombineUnderRoot(packageDir, manifest.Assembly);
        if (!File.Exists(assemblyPath))
        {
            throw new InvalidOperationException(
                $"Shell app '{manifest.Id}' assembly was not found: '{assemblyPath}'.");
        }

        return new ParsedPackage(manifest, assemblyPath);
    }

    internal static bool IsSafeAssemblyFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || fileName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var name = Path.GetFileName(fileName);
        if (!string.Equals(name, fileName, StringComparison.Ordinal))
        {
            return false;
        }

        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               && ShellAppStorage.IsSafeAppId(Path.GetFileNameWithoutExtension(name));
    }

    private static IShellApplication CreateFromAssembly(string assemblyPath, string typeName)
    {
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(typeName, throwOnError: true, ignoreCase: false)
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in '{assemblyPath}'.");
        if (!typeof(IShellApplication).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Type '{typeName}' is not an IShellApplication.");
        }

        return Activator.CreateInstance(type) as IShellApplication
            ?? throw new InvalidOperationException($"Type '{typeName}' could not be constructed.");
    }
}
