using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public static class AuthoringPackCodes
{
    public const string ContractFailed = "PACK_CONTRACT";
    public const string CompatBlocked = "PACK_COMPAT";
    public const string TapCreateFailed = "PACK_TAP_CREATE";
    public const string PluginMissing = "PACK_PLUGIN_MISSING";
    public const string ShellAppFailed = "PACK_SHELL_APP";
}

public sealed class PackOptions
{
    public OpenTapHome? Home { get; init; }

    public OpenTapHome? TuiHome { get; init; }

    public ITuiCompatChecker? Compat { get; init; }

    public bool Offline { get; init; }
}

public sealed record ShipDependency(string Package, string Version, bool Optional = false, string? When = null);

public sealed record ShipManifest(
    string PackageName,
    string Version,
    IReadOnlyList<string> Files,
    IReadOnlyList<ShipDependency>? Dependencies = null)
{
    [JsonIgnore]
    public IReadOnlyList<ShipDependency> ResolvedDependencies => Dependencies ?? [];
}

/// Validates a workspace, writes package.xml, creates the program TapPackage, and writes ship-manifest.json.
public static class WorkspacePacker
{
    public const string ShipManifestFileName = "ship-manifest.json";
    public const string PackageXmlFileName = "package.xml";
    public const string BakeTimeSuffix = " (bake-time)";

    internal static string ShellAppDirectoryEntry(string id) => $"shell-apps/{id}";

    internal static string ShellAppBakeTimeEntry(string id) => ShellAppDirectoryEntry(id) + BakeTimeSuffix;

    public static ShipManifest Pack(AuthoringWorkspace workspace, string outputDirectory, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(outputDirectory);
        var home = options.Home ?? new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { Offline = options.Offline });

        ValidateContract(workspace);
        if (options.Compat is not null)
        {
            var tuiHome = options.TuiHome ?? home;
            var report = options.Compat.Compare(workspace, home, tuiHome);
            if (report.BlocksPack())
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.CompatBlocked}: TUI compatibility report blocks pack.");
            }
        }

        var plansDir = ResolvePlansDirectory(workspace);
        var packageXml = Path.Combine(plansDir, PackageXmlFileName);
        File.WriteAllText(packageXml, PackageXmlRenderer.Render(workspace));

        var tapPackage = CreateTapPackage(home, plansDir, workspace.Manifest.Package, outputDirectory);
        var shipped = new List<string> { Path.GetFileName(tapPackage) };
        shipped.AddRange(CopyPluginPackages(workspace, outputDirectory));
        shipped.AddRange(PublishShellApps(workspace, outputDirectory));

        var deps = WorkspacePackPlan.ShipDependencies(workspace.Manifest);
        var manifest = new ShipManifest(
            workspace.Manifest.Package.Name,
            workspace.Manifest.Package.Version,
            shipped,
            deps);
        File.WriteAllText(
            Path.Combine(outputDirectory, ShipManifestFileName),
            JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.ShipManifest));
        return manifest;
    }

    private static void ValidateContract(AuthoringWorkspace workspace)
    {
        var report = PlanContractValidator.Validate(
            workspace.TapPlanPaths,
            new PlanContractOptions
            {
                Strict = true,
                ExcludeVisaAdapter = true,
            });
        if (!report.HasErrors)
        {
            return;
        }

        var details = string.Join(
            "; ",
            report.Plans.SelectMany(p => p.Findings)
                .Where(f => f.Severity == PlanContractSeverity.Error)
                .Select(f => $"{f.Code}: {f.Message}"));
        throw new AuthoringWorkspaceException($"{AuthoringPackCodes.ContractFailed}: {details}");
    }

    private static string ResolvePlansDirectory(AuthoringWorkspace workspace)
    {
        var relative = AuthoringManifest.RelativePlansDirectory(workspace.Manifest.PlansDirectory);
        return Path.IsPathRooted(relative)
            ? Path.GetFullPath(relative)
            : Path.GetFullPath(Path.Combine(workspace.Root, relative));
    }

    private static string CreateTapPackage(
        OpenTapHome home,
        string plansDir,
        AuthoringPackageSpec spec,
        string outputDirectory)
    {
        var tapDll = Path.Combine(home.Root, "tap.dll");
        if (!File.Exists(tapDll))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringPackCodes.TapCreateFailed}: tap.dll was not found in '{home.Root}'.");
        }

        var before = Directory.EnumerateFiles(plansDir, "*.TapPackage")
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{tapDll}\" package create \"{PackageXmlFileName}\"",
            WorkingDirectory = plansDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(psi)
            ?? throw new AuthoringWorkspaceException(
                $"{AuthoringPackCodes.TapCreateFailed}: failed to start tap package create.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            TryKill(process);
            throw new AuthoringWorkspaceException(
                $"{AuthoringPackCodes.TapCreateFailed}: tap package create timed out.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringPackCodes.TapCreateFailed}: tap package create exited {process.ExitCode}. {stderr} {stdout}");
        }

        var created = FindCreatedTapPackage(plansDir, before, spec.Name);
        if (created is null)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringPackCodes.TapCreateFailed}: no .TapPackage was produced in '{plansDir}'.");
        }

        var dest = Path.Combine(outputDirectory, Path.GetFileName(created));
        File.Copy(created, dest, overwrite: true);
        try
        {
            File.Delete(created);
        }
        catch
        {
            // Dist copy is the ship artifact; leftover plans-dir package is non-fatal.
        }

        return dest;
    }

    private static string? FindCreatedTapPackage(
        string plansDir,
        HashSet<string> before,
        string packageName)
    {
        var after = Directory.EnumerateFiles(plansDir, "*.TapPackage")
            .Select(Path.GetFullPath)
            .ToArray();
        var created = after
            .Where(path => !before.Contains(path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (created is not null)
        {
            return created;
        }

        if (string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        return after.FirstOrDefault(path =>
            Path.GetFileName(path).StartsWith(packageName, StringComparison.OrdinalIgnoreCase));
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch
        {
            // Best-effort stop of a stuck child process.
        }
    }

    private static IReadOnlyList<string> CopyPluginPackages(AuthoringWorkspace workspace, string outputDirectory)
    {
        var copied = new List<string>();
        foreach (var entry in workspace.Manifest.PluginProjects)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var path = Path.IsPathRooted(entry)
                ? entry
                : Path.GetFullPath(Path.Combine(workspace.Root, entry));
            if (!IsTapPackagePath(path))
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.PluginMissing}: pluginProjects must point at a .TapPackage (not '{entry}').");
            }

            if (!File.Exists(path))
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.PluginMissing}: plugin TapPackage '{entry}' was not found.");
            }

            var name = Path.GetFileName(path);
            File.Copy(path, Path.Combine(outputDirectory, name), overwrite: true);
            copied.Add(name);
        }

        return copied;
    }

    private static bool IsTapPackagePath(string path)
        => path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> PublishShellApps(AuthoringWorkspace workspace, string outputDirectory)
    {
        var entries = new List<string>();
        foreach (var project in workspace.Manifest.ShellAppProjects)
        {
            if (string.IsNullOrWhiteSpace(project))
            {
                continue;
            }

            var csproj = Path.IsPathRooted(project)
                ? project
                : Path.GetFullPath(Path.Combine(workspace.Root, project));
            if (!File.Exists(csproj))
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.ShellAppFailed}: shell-app project '{project}' was not found.");
            }

            var id = Path.GetFileNameWithoutExtension(csproj);
            var dest = Path.Combine(outputDirectory, "shell-apps", id);
            Directory.CreateDirectory(dest);
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{csproj}\" -o \"{dest}\" --nologo",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            using var process = Process.Start(psi)
                ?? throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.ShellAppFailed}: failed to start dotnet publish for '{id}'.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180_000))
            {
                TryKill(process);
                throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.ShellAppFailed}: dotnet publish '{id}' timed out.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringPackCodes.ShellAppFailed}: dotnet publish '{id}' failed. {stderr} {stdout}");
            }

            entries.Add(ShellAppDirectoryEntry(id));
            entries.Add(ShellAppBakeTimeEntry(id));
        }

        return entries;
    }
}
