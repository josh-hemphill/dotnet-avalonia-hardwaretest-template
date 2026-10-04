using System.Text.Json;
using System.Xml.Linq;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    internal static bool ShellInput(string path) => !path.Split(Path.DirectorySeparatorChar).Any(p =>
        p is "bin" or "obj" or ".git" or ".authoring" or "authoring-drafts" or "plans" or "dist" or "node_modules");

    private static readonly string[] ShellConfigurationNames =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config", "global.json", ".editorconfig", ".globalconfig"];

    // Evaluation never executes a target or writes into the original project directories.
    private static IReadOnlyList<BuildInputTree> CaptureShellInputs(AuthoringWorkspace workspace, bool offline = false, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (workspace.Manifest.ShellAppProjects.Count == 0) return [];
        var sdkRoot = environment is null ? Environment.GetEnvironmentVariable("DOTNET_ROOT") : environment.GetValueOrDefault("DOTNET_ROOT");
        if (string.IsNullOrWhiteSpace(sdkRoot)) Unsupported(workspace.Root, "DOTNET_ROOT must identify the captured SDK");
        var packagesRoot = environment is null ? Environment.GetEnvironmentVariable("NUGET_PACKAGES") : environment.GetValueOrDefault("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packagesRoot))
            packagesRoot = Path.Combine(environment?.GetValueOrDefault(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        packagesRoot = Path.GetFullPath(packagesRoot!);
        var packageFolders = new HashSet<string>(StringComparer.Ordinal) { packagesRoot };
        var packagePayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projects = new Dictionary<string, ShellEvaluation>(StringComparer.Ordinal);
        var pending = new Queue<string>(workspace.Manifest.ShellAppProjects.Select(p => Resolve(workspace.Root, p)));
        var imports = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var project))
        {
            if (projects.ContainsKey(project)) continue;
            var existingAssets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
            if (File.Exists(existingAssets))
            {
                using var assets = JsonDocument.Parse(File.ReadAllBytes(existingAssets));
                foreach (var folder in assets.RootElement.GetProperty("packageFolders").EnumerateObject())
                    packageFolders.Add(Path.GetFullPath(folder.Name));
            }
            var evaluation = EvaluateShell(project, sdkRoot!, packageFolders, environment);
            projects.Add(project, evaluation);
            foreach (var reference in evaluation.References) pending.Enqueue(reference);
            foreach (var import in evaluation.Imports) imports.Add(import);
        }
        var root = Path.GetDirectoryName(projects.Keys.First())!;
        foreach (var path in projects.Keys.Concat(imports).Concat(projects.Values.SelectMany(p => p.Inputs))
            .Where(p => !ShellContains(sdkRoot!, p) && !packageFolders.Any(f => ShellContains(f, p))))
            while (!ShellContains(root, path)) root = Path.GetDirectoryName(root)
                ?? throw new AuthoringWorkspaceException("BUILD_SHELL_INPUTS: Shell graph has no bounded common source root.");
        for (var directory = Path.GetDirectoryName(root); directory is not null; directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, ".editorconfig")) || File.Exists(Path.Combine(directory, ".globalconfig")))
                root = directory;
        if (Path.GetDirectoryName(root) is null) Unsupported(root, "shell graph spanning a filesystem root cannot be captured safely");
        // Preserve ancestor lookup and relative references by retaining the graph's real hierarchy.
        var ancestors = CaptureShellAncestors(root).ToArray();
        foreach (var path in imports) ValidateShellConfiguration(path);
        foreach (var project in projects.Keys) ValidateShellConfiguration(project);
        foreach (var tree in ancestors)
            foreach (var file in tree.Files) ValidateShellConfiguration(file.Path);
        var source = CaptureTree(root, "shell", true, ShellInput);
        var captured = source.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var input in projects.Values.SelectMany(p => p.Inputs).Concat(imports).Concat(projects.Keys))
            if (!captured.Contains(input) && !ShellContains(sdkRoot!, input) && !packageFolders.Any(f => ShellContains(f, input)))
                Unsupported(input, "evaluated input is outside the captured source graph");
        var trees = new List<BuildInputTree> { source };
        trees.AddRange(ancestors);
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in projects)
        {
            var lockPath = Path.Combine(Path.GetDirectoryName(pair.Key)!, "packages.lock.json");
            if (File.Exists(lockPath)) ReadShellLock(lockPath, packageIds);
            var assets = Path.Combine(pair.Value.ExtensionsPath, "project.assets.json");
            if (File.Exists(assets))
            {
                trees.Add(CaptureTree(Path.GetDirectoryName(assets)!, $"shell-assets/{trees.Count}", false,
                    p => Path.GetFullPath(p) == assets, materialize: false));
                ReadShellAssets(assets, packageIds, packagePayloads);
            }
        }
        // Lock files omit SDK-inferred runtime/host downloads. Resolve every saved graph with
        // the same Release properties used by publish before freezing its complete payload closure.
        ResolveShellPackages(source, ancestors, projects.Keys, packagesRoot, packageIds, packagePayloads, offline, environment);
        foreach (var id in packageIds.Order(StringComparer.Ordinal))
        {
            var package = packagePayloads.GetValueOrDefault(id) ?? Path.Combine(packagesRoot, id);
            if (!Directory.Exists(package)) Unsupported(package, "resolved NuGet package payload is missing");
            trees.Add(CaptureTree(package, "shell-packages/" + id, true));
        }
        return trees.AsReadOnly();
    }

    private static IEnumerable<BuildInputTree> CaptureShellAncestors(string root)
    {
        var index = 0;
        for (var directory = Path.GetDirectoryName(root); directory is not null; directory = Path.GetDirectoryName(directory))
            yield return CaptureTree(directory, $"shell-config/{index++}", false,
                p => ShellConfigurationNames.Contains(Path.GetFileName(p), StringComparer.Ordinal));
    }

    private static string ShellStageProject(string staging, IReadOnlyList<BuildInputTree> trees, string workspaceRoot, string entry)
    {
        var source = trees.Single(t => t.StageRelativePath == "shell");
        var project = Resolve(workspaceRoot, entry);
        EnsureContained(source.Root, project);
        return Path.Combine(staging, "shell", Path.GetRelativePath(source.Root, project));
    }

    private static void PrepareShellStage(string staging, IReadOnlyList<BuildInputTree> trees)
    {
        if (!trees.Any(t => t.StageRelativePath == "shell")) return;
        CopyShellAncestors(Path.Combine(staging, "shell"), trees.Where(t => t.StageRelativePath.StartsWith("shell-config/", StringComparison.Ordinal)));
        // Roslyn walks above source directories for editorconfig. Stop at our owned staging
        // boundary, after preserving the captured source/config hierarchy beneath it.
        File.WriteAllText(Path.Combine(staging, ".editorconfig"), "root = true\n");
        var packages = Path.Combine(staging, "shell-packages");
        Directory.CreateDirectory(packages);
        var config = new XDocument(new XElement("configuration",
            new XElement("packageSources", new XElement("clear")),
            new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", packages)))));
        config.Save(Path.Combine(staging, "shell", "authoring-build-nuget.config"));
    }

    private static void CopyShellAncestors(string root, IEnumerable<BuildInputTree> ancestors)
    {
        var copied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in ancestors)
            foreach (var file in tree.Files)
            {
                var target = Path.Combine(root, file.RelativePath);
                if (copied.Add(file.RelativePath) && !File.Exists(target)) File.WriteAllBytes(target, file.Bytes);
            }
    }

    private static bool ShellContains(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void Unsupported(string path, string reason) => throw new AuthoringWorkspaceException(
        $"BUILD_SHELL_INPUTS: '{path}': {reason}. Use an SDK project with a bounded source graph and captured NuGet payloads.");
}
