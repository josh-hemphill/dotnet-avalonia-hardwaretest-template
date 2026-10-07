using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    internal static IReadOnlyDictionary<string, string> ShellWriteProfile(string packages, string? config = null,
        bool createLock = false, string? publishDirectory = null)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Configuration"] = "Release",
            ["NuGetAudit"] = "false",
            ["SourceRevisionId"] = "local",
            ["SourceRevisionDate"] = "1970-01-01T00:00:00Z",
            ["RestorePackagesPath"] = packages
        };
        if (config is not null) properties["RestoreConfigFile"] = config;
        if (createLock) properties["RestorePackagesWithLockFile"] = "true";
        if (publishDirectory is not null)
        {
            properties["PublishDir"] = Path.TrimEndingDirectorySeparator(Path.GetFullPath(publishDirectory)) + Path.DirectorySeparatorChar;
            properties["_IsPublishing"] = "true";
            properties["_CommandLineDefinedOutputPath"] = "true";
        }
        return new ReadOnlyDictionary<string, string>(properties);
    }

    internal static IReadOnlyList<string> ShellOperationArguments(string project, IReadOnlyDictionary<string, string> profile, string? target = null)
        => new[] { "msbuild", project, "-nologo" }.Concat(profile.Select(p => "-p:" + p.Key + "=" + p.Value))
            .Concat(target is null ? [] : new[] { "-target:" + target }).ToArray();

    // Evaluation and execution consume the same global property map. Direct MSBuild targets
    // avoid implicit CLI globals differing from the profile that was checked without targets.
    internal static void ValidateShellWriteProfile(string project, string ownedRoot, IReadOnlyDictionary<string, string> profile,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var pending = new Queue<string>(); pending.Enqueue(project);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var current))
        {
            current = Path.GetFullPath(current);
            if (!seen.Add(current)) continue;
            EnsureContained(ownedRoot, ResolvedPath(current, false));
            ValidateShellEvaluationFiles(current);
            foreach (var properties in ProfileProjections(profile))
            {
                var arguments = ShellOperationArguments(current, properties).Concat([
                    "-getProperty:" + string.Join(',', ShellOutputIdentityProperties), "-getItem:ProjectReference"]);
                using var result = JsonDocument.Parse(RunShellDotNet(Path.GetDirectoryName(current)!, arguments, environment));
                foreach (var name in ShellOutputIdentityProperties)
                    if (!SafeAssemblyOutputName(ShellJsonProperty(result.RootElement.GetProperty("Properties"), name).GetString()!))
                        Unsupported(current, $"{name} contains output path components under the actual staged SDK write profile");
                foreach (var reference in ShellJsonProperty(result.RootElement.GetProperty("Items"), "ProjectReference").EnumerateArray())
                    pending.Enqueue(ShellJsonProperty(reference, "FullPath").GetString()!);
            }
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, string>> ProfileProjections(IReadOnlyDictionary<string, string> profile)
    {
        yield return profile;
        if (!profile.ContainsKey("PublishDir")) yield break;
        // Standard SDK reference builds do not publish reference projects to the parent
        // destination; check their build projection too, on the actual copied project paths.
        var build = profile.Where(p => p.Key is not ("PublishDir" or "_CommandLineDefinedOutputPath"))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        yield return build;
        yield return build.Where(p => p.Key != "_IsPublishing").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }
}
