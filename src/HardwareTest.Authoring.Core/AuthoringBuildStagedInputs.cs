namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    private static IReadOnlyList<(string Path, string Target, string Hash, int? UnixMode)> CaptureStagedInputs(string staging, IReadOnlySet<string>? generatedDirectories = null)
    {
        var files = new List<(string, string, string, int?)>();
        Walk(staging);
        return files.AsReadOnly();

        void Walk(string directory)
        {
            foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(staging, path).Replace('\\', '/');
                if ((relative.StartsWith("home/", StringComparison.Ordinal) || relative.StartsWith("tui-home/", StringComparison.Ordinal))
                    && !EnvironmentInput(path)) continue;
                // package.xml is generated from the frozen manifest by the existing packer.
                if (relative == "workspace/plans/package.xml") continue;
                var target = ResolvedPath(path, false);
                EnsureContained(staging, target);
                files.Add((relative, Path.GetRelativePath(staging, target), Hash(File.ReadAllBytes(path)),
                    OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(path)));
            }
            foreach (var path in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(staging, path).Replace('\\', '/');
                // Only owned publish artifacts, SDK-generated shell intermediates, and known
                // ephemeral OpenTAP logs/cache may change. Source/config/package bytes may not.
                if (generatedDirectories is not null && (relative == "output" || generatedDirectories.Contains(relative))) continue;
                if ((relative.StartsWith("home/", StringComparison.Ordinal) || relative.StartsWith("tui-home/", StringComparison.Ordinal))
                    && !EnvironmentInput(path)) continue;
                if (new DirectoryInfo(path).LinkTarget is not null)
                    throw new AuthoringWorkspaceException($"BUILD_STAGED_CHANGED: Staged directory link '{relative}' is unsupported.");
                Walk(path);
            }
        }
    }

    private static void VerifyStagedInputs(string staging, IReadOnlyList<(string Path, string Target, string Hash, int? UnixMode)> expected, bool afterPacking)
    {
        var generated = afterPacking ? expected.Where(f => f.Path.StartsWith("shell/", StringComparison.Ordinal)
            && f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => new[] { f.Path[..f.Path.LastIndexOf('/')] + "/obj", f.Path[..f.Path.LastIndexOf('/')] + "/bin" })
            .ToHashSet(StringComparer.Ordinal) : null;
        var actual = CaptureStagedInputs(staging, generated);
        if (!expected.SequenceEqual(actual))
        {
            var difference = expected.Except(actual).Select(f => f.Path).Concat(actual.Except(expected).Select(f => f.Path)).Distinct();
            throw new AuthoringWorkspaceException($"BUILD_STAGED_CHANGED: Captured staged inputs changed during checks or packing: {string.Join(", ", difference)}.");
        }
    }
}
