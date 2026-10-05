using System.Security.Cryptography;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    public static AuthoringBuildRequest CaptureSaved(AuthoringWorkspace workspace, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        options.CancellationToken.ThrowIfCancellationRequested();
        var environment = CaptureEnvironment();
        if (!WorkspacePacker.IsWritableWorkspace(workspace) || !WorkspacePacker.ProbeWorkspaceWriteAccess(workspace))
            throw new PackPreflightException(new PackPreflightReport([new("PACK_WORKSPACE", "Open an existing writable workspace before packing.", true, workspace.Root)]));
        // Always reload disk: caller manifests and drafts are deliberately ignored.
        var saved = AuthoringWorkspaceLoader.Load(workspace.Root);
        if (saved.IsReadOnly) throw new AuthoringWorkspaceException("BUILD_SOURCE: Future manifest cannot be built.");
        var preliminary = new List<PackPreflightFinding>();
        foreach (var entry in saved.Manifest.PluginProjects)
        {
            var path = Resolve(saved.Root, entry);
            if (!File.Exists(path) || !(path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                preliminary.Add(new(AuthoringPackCodes.PluginMissing, $"Missing or unsupported plugin package '{entry}'.", true, path));
        }
        foreach (var entry in saved.Manifest.ShellAppProjects)
        {
            var path = Resolve(saved.Root, entry);
            if (!File.Exists(path) || !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                preliminary.Add(new(AuthoringPackCodes.ShellAppFailed, $"Missing or unsupported shell project '{entry}'.", true, path));
        }
        if (preliminary.Count > 0) throw new PackPreflightException(new PackPreflightReport(preliminary.AsReadOnly()));
        var home = options.Home is { } selectedHome ? new OpenTapHome(Path.GetFullPath(selectedHome.Root))
            : new OpenTapHomeBootstrapper().Bootstrap(saved,
            new BootstrapOptions { Offline = options.Offline, HomeDirectory = options.BootstrapHomeDirectory });
        var tuiHome = new OpenTapHome(Path.GetFullPath((options.TuiHome ?? home).Root));
        var trees = new List<BuildInputTree>
        {
            CaptureTree(saved.Root, "workspace", false, p => Path.GetFileName(p) == AuthoringWorkspaceLoader.ManifestFileName),
            CaptureTree(PlansRoot(saved), "workspace/plans", false),
            CaptureTree(Path.Combine(saved.Root, "authoring-drafts"), "workspace/authoring-drafts", false,
                p => p.EndsWith(".authoring.json", StringComparison.Ordinal)),
            CaptureTree(home.Root, "home", true, EnvironmentInput),
        };
        if (!string.Equals(Path.GetFullPath(home.Root), Path.GetFullPath(tuiHome.Root), StringComparison.Ordinal))
            trees.Add(CaptureTree(tuiHome.Root, "tui-home", true, EnvironmentInput));
        for (var i = 0; i < saved.Manifest.PluginProjects.Count; i++)
        {
            var path = Resolve(saved.Root, saved.Manifest.PluginProjects[i]);
            if (!File.Exists(path)) throw new AuthoringWorkspaceException($"BUILD_DEPENDENCY: Missing '{path}'.");
            trees.Add(CaptureTree(Path.GetDirectoryName(path)!, $"plugins/{i}", false,
                p => Path.GetFullPath(p) == path));
        }
        if (saved.Manifest.ShellAppProjects.Count != 0)
        {
            trees.AddRange(CaptureShellInputs(saved, options.Offline, environment));
            var sdkRoot = environment.GetValueOrDefault("DOTNET_ROOT");
            if (string.IsNullOrWhiteSpace(sdkRoot) || !File.Exists(Path.Combine(sdkRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")))
                throw new AuthoringWorkspaceException("BUILD_SHELL_INPUTS: Set DOTNET_ROOT to the SDK home so its identity can be captured.");
            trees.Add(CaptureTree(sdkRoot, "sdk-identity", true, null, materialize: false));
        }
        var executable = ResolveDotNetExecutable(environment);
        trees.Add(CaptureTree(Path.GetDirectoryName(executable)!, "dotnet-host", false,
            p => Path.GetFullPath(p) == executable, materialize: false));
        var shared = Path.Combine(Path.GetDirectoryName(executable)!, "shared");
        if (Directory.Exists(shared) && !trees.Any(t => t.StageRelativePath == "sdk-identity"))
            trees.Add(CaptureTree(shared, "dotnet-runtime", true, null, materialize: false));
        // Compiler and built-in plugin identities are part of the snapshot as well.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic &&
            (a.GetName().Name?.StartsWith("HardwareTest", StringComparison.Ordinal) == true || a.GetName().Name == "OpenTap")))
        {
            var path = assembly.Location;
            if (path.Length != 0) trees.Add(CaptureTree(Path.GetDirectoryName(path)!, $"compiler/{trees.Count}", false,
                p => Path.GetFullPath(p) == path));
        }
        var request = new AuthoringBuildRequest(saved.Root, new PackOptions
        {
            Home = home,
            TuiHome = tuiHome,
            DotNetExecutable = executable,
            Offline = options.Offline,
            Compat = options.Compat,
            Progress = options.Progress,
            PreflightCompleted = options.PreflightCompleted
        }, trees.AsReadOnly(), environment);
        Recheck(request);
        return request;
    }

    internal static BuildInputTree CaptureTree(string root, string stage, bool recursive, Func<string, bool>? select = null, bool materialize = true)
    {
        root = Path.GetFullPath(root);
        EnsureContained(root, ResolvedPath(root, directory: true));
        var files = new List<BuildInputFile>();
        var links = new List<string>();
        if (Directory.Exists(root)) Walk(root);
        return new(root, stage, recursive, files.AsReadOnly(), links.AsReadOnly(), materialize);
        void Walk(string directory)
        {
            var resolvedDirectory = ResolvedPath(directory, true);
            EnsureContained(root, resolvedDirectory);
            if (new DirectoryInfo(directory).LinkTarget is not null) links.Add(directory + "=>" + resolvedDirectory);
            foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if (select is not null && !select(path)) continue;
                var target = CapturedFileTarget(path, resolvedDirectory);
                EnsureContained(root, target);
                var content = ReadCapturedFile(path, materialize);
                var mode = OperatingSystem.IsWindows() ? (int?)null : (int)File.GetUnixFileMode(path);
                files.Add(new(path, Path.GetRelativePath(root, path), target, content.Hash, content.Bytes, mode));
            }
            if (recursive)
                foreach (var sub in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
                {
                    if (stage == "shell" && !ShellInput(sub)) continue;
                    if ((stage is "home" or "tui-home") && !EnvironmentInput(sub)) continue;
                    if (new DirectoryInfo(sub).LinkTarget is not null)
                        throw new AuthoringWorkspaceException($"BUILD_LINK: Directory links are unsupported in captured trees: '{sub}'.");
                    Walk(sub);
                }
        }
    }

    internal static void Recheck(AuthoringBuildRequest request)
    {
        var currentEnvironment = CaptureEnvironment();
        if (!request.EnvironmentValues.SequenceEqual(currentEnvironment))
            throw new AuthoringWorkspaceException("BUILD_ENVIRONMENT_CHANGED: Inherited build environment changed. Capture a new build.");
        foreach (var entry in request.EnvironmentValues)
            if (Environment.GetEnvironmentVariable(entry.Key) != entry.Value)
                throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT_CHANGED: Environment input '{entry.Key}' changed. Capture a new build.");
        foreach (var tree in request.Trees)
        {
            var selected = tree.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            Func<string, bool>? filter = tree.StageRelativePath == "workspace"
                ? p => Path.GetFileName(p) == AuthoringWorkspaceLoader.ManifestFileName
                : tree.StageRelativePath.StartsWith("shell-config/", StringComparison.Ordinal)
                    ? p => ShellConfigurationNames.Contains(Path.GetFileName(p), StringComparer.Ordinal)
                : tree.StageRelativePath == "workspace/authoring-drafts"
                    ? p => p.EndsWith(".authoring.json", StringComparison.Ordinal)
                    : tree.StageRelativePath.StartsWith("plugins/", StringComparison.Ordinal) || tree.StageRelativePath.StartsWith("compiler/", StringComparison.Ordinal) || tree.StageRelativePath == "dotnet-host" || tree.StageRelativePath.StartsWith("shell-assets/", StringComparison.Ordinal)
                        ? p => selected.Contains(p) : tree.StageRelativePath == "shell" ? ShellInput
                        : tree.StageRelativePath is "home" or "tui-home" ? EnvironmentInput : null;
            var current = CaptureTree(tree.Root, tree.StageRelativePath, tree.Recursive, filter, materialize: false);
            if (!tree.Files.Select(f => (f.Path, f.Target, f.Hash, f.UnixMode)).SequenceEqual(current.Files.Select(f => (f.Path, f.Target, f.Hash, f.UnixMode)))
                || !tree.Links.SequenceEqual(current.Links))
                throw new AuthoringWorkspaceException($"BUILD_INPUT_CHANGED: Saved input or dependency changed: '{tree.Root}'. Capture a new build.");
        }
    }
    private static string ResolveDotNetExecutable(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var root = environment is null ? Environment.GetEnvironmentVariable("DOTNET_ROOT") : environment.GetValueOrDefault("DOTNET_ROOT");
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (!string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, name))) return ResolvedPath(Path.Combine(root, name), directory: false);
        foreach (var directory in ((environment is null ? Environment.GetEnvironmentVariable("PATH") : environment.GetValueOrDefault("PATH")) ?? "").Split(Path.PathSeparator))
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name))) return ResolvedPath(Path.Combine(directory, name), directory: false);
        throw new AuthoringWorkspaceException("BUILD_PREREQUISITE: dotnet executable was not found.");
    }
    private static bool EnvironmentInput(string path) => !path.Split(Path.DirectorySeparatorChar).Any(p => p is "SessionLogs" or ".opentap_recent_logs" or ".PackageCache" || p.StartsWith(".PackageCache.", StringComparison.Ordinal));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string Resolve(string root, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    internal static string PlansRoot(AuthoringWorkspace workspace) => Resolve(workspace.Root, AuthoringManifest.RelativePlansDirectory(workspace.Manifest.PlansDirectory));
    internal static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AuthoringWorkspaceException($"BUILD_CONTAINMENT: '{path}' escapes '{root}'.");
    }
    internal static string ResolvedPath(string path, bool directory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var activeLinks = new HashSet<string>(comparison);
        var traversals = 0;
        return ResolveTarget(Path.GetFullPath(path));

        string ResolveTarget(string candidate)
        {
            var current = Path.GetPathRoot(candidate)!;
            foreach (var component in candidate[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (component == ".") continue;
                if (component == "..") { current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? current; continue; }
                current = Path.Combine(current, component);
                FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                var rawTarget = entry.LinkTarget;
                if (rawTarget is null) continue;
                var link = current;
                if (++traversals > 64 || !activeLinks.Add(link))
                    throw new AuthoringWorkspaceException($"BUILD_LINK: Link cycle or excessive link chain at '{link}'.");
                try
                {
                    if (Path.IsPathRooted(rawTarget) && !Path.IsPathFullyQualified(rawTarget))
                        throw new AuthoringWorkspaceException($"BUILD_LINK: Ambiguous symlink target '{rawTarget}'.");
                    var target = Path.IsPathFullyQualified(rawTarget) ? rawTarget : Path.Combine(Path.GetDirectoryName(link)!, rawTarget);
                    // Keep raw '..' until preceding directory links have resolved, matching File.Open.
                    current = ResolveTarget(target);
                }
                finally { activeLinks.Remove(link); }
            }
            return Path.GetFullPath(current);
        }
    }
}
