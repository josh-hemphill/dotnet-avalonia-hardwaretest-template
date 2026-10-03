using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed record PackPreflightFinding(string Code, string Message, bool IsError, string? Path = null)
{
    public string DisplayText => $"{Code}: {Message}{(Path is null ? string.Empty : $" ({Path})")}";
}

public sealed record PackPreflightReport(
    IReadOnlyList<PackPreflightFinding> Findings,
    PlanContractBatchReport? Contract = null,
    TuiCompatReport? Compatibility = null,
    OpenTapHome? Home = null,
    OpenTapHome? TuiHome = null)
{
    public bool HasErrors => Findings.Any(f => f.IsError);
    public IReadOnlyList<string> IncludedFiles { get; init; } = [];
    public IReadOnlyList<string> ExcludedPlans { get; init; } = [];
}

public sealed class PackPreflightException(PackPreflightReport report)
    : AuthoringWorkspaceException(string.Join(Environment.NewLine, report.Findings.Where(f => f.IsError).Select(f => f.DisplayText)))
{
    public PackPreflightReport Report { get; } = report;
}

public static partial class WorkspacePacker
{
    public static PackPreflightReport Preflight(AuthoringWorkspace workspace, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        var findings = new List<PackPreflightFinding>();
        var includedFiles = PackageXmlRenderer.EnumeratePackFiles(workspace);
        var includedPaths = includedFiles.Where(f => f.EndsWith(".TapPlan", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFullPath(Path.Combine(ResolvePlansDirectory(workspace), f))).ToArray();
        var packingWorkspace = workspace with { TapPlanPaths = includedPaths };
        PlanContractBatchReport? contract = null;
        TuiCompatReport? compatibility = null;
        OpenTapHome? home = options.Home;
        OpenTapHome? tuiHome = options.TuiHome;
        PackPreflightReport Complete()
        {
            var report = new PackPreflightReport(findings.ToArray(), contract, compatibility, home, tuiHome)
            {
                IncludedFiles = includedFiles,
                ExcludedPlans = workspace.TapPlanPaths.Except(includedPaths, StringComparer.OrdinalIgnoreCase).ToArray(),
            };
            options.PreflightCompleted?.Invoke(report);
            return report;
        }

        if (!IsWritableWorkspace(workspace) || !ProbeWindowsWorkspaceWriteAccess(workspace))
        {
            findings.Add(new("PACK_WORKSPACE", "Open an existing writable workspace before packing.", true, workspace.Root));
            return Complete();
        }

        foreach (var entry in workspace.Manifest.PluginProjects.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var path = ResolveEntry(workspace, entry);
            if (!IsTapPackagePath(path) || !File.Exists(path))
                findings.Add(new(AuthoringPackCodes.PluginMissing, $"pluginProjects requires an existing .TapPackage or .zip: '{entry}'.", true, path));
        }
        foreach (var entry in workspace.Manifest.ShellAppProjects.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var path = ResolveEntry(workspace, entry);
            if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                findings.Add(new(AuthoringPackCodes.ShellAppFailed, $"shellAppProjects requires an existing .csproj: '{entry}'.", true, path));
        }
        foreach (var file in PackageXmlRenderer.EnumeratePackFiles(workspace))
        {
            var path = Path.Combine(ResolvePlansDirectory(workspace), file);
            if (!File.Exists(path)) findings.Add(new("PACK_FILE_MISSING", "Required package file is missing.", true, path));
        }

        try
        {
            contract = PlanContractValidator.Validate(packingWorkspace.TapPlanPaths,
                new PlanContractOptions { Strict = true, ExcludeVisaAdapter = true });
            foreach (var plan in contract.Plans)
                foreach (var finding in plan.Findings)
                    findings.Add(new(AuthoringPackCodes.ContractFailed + "/" + finding.Code, finding.Message,
                        finding.Severity == PlanContractSeverity.Error, finding.Path ?? plan.TargetPath));
        }
        catch (Exception ex)
        {
            findings.Add(new(AuthoringPackCodes.ContractFailed, ex.Message, true));
        }
        if (findings.Any(f => f.IsError)) return Complete();

        try
        {
            home ??= new OpenTapHomeBootstrapper().Bootstrap(workspace,
                new BootstrapOptions { Offline = options.Offline, HomeDirectory = options.BootstrapHomeDirectory });
            tuiHome ??= home;
            InspectHome(home, workspace.Manifest.Dependencies.Select(d => d.Package), findings);
            if (!string.Equals(home.Root, tuiHome.Root, StringComparison.OrdinalIgnoreCase))
                InspectHome(tuiHome, workspace.Manifest.Dependencies.Select(d => d.Package), findings);
            if (workspace.Manifest.IncludeTui && !HasUsableTuiPackage(tuiHome, out var tuiDetails))
                findings.Add(new("PACK_TUI_MISSING", $"includeTui=true requires an installed TUI-named package with a declared, readable managed TUI DLL; install it or set includeTui=false. {tuiDetails}", true, tuiHome.Root));
        }
        catch (Exception ex)
        {
            findings.Add(new("PACK_PREREQUISITE", ex.Message, true, home?.Root));
            return Complete();
        }

        try
        {
            compatibility = (options.Compat ?? new TuiCompatChecker()).Compare(packingWorkspace, home, tuiHome);
            foreach (var delta in compatibility.Catalog)
            {
                var single = new TuiCompatReport([delta], []);
                findings.Add(new(AuthoringPackCodes.CompatBlocked + "/CATALOG", $"Type '{delta.TypeName}' missing on {delta.MissingOn}.", single.BlocksPack()));
            }
            foreach (var item in compatibility.RoundTrips)
                findings.Add(new(AuthoringPackCodes.CompatBlocked + "/" + item.Code, item.Message,
                    new TuiCompatReport([], [item]).BlocksPack(), item.PlanPath));
            findings.Add(new("PACK_COMPAT_SCOPE", "Checked plugin catalogs and in-process plan load/save round trips; no external TUI process was run.", false));
        }
        catch (Exception ex)
        {
            findings.Add(new(AuthoringPackCodes.CompatBlocked, ex.Message, true, tuiHome?.Root));
        }
        return Complete();
    }

    // Evidence is an installed TUI-named package declaring an existing managed TUI DLL.
    // This establishes installed payload, not an external-process integration check.
    private static bool HasUsableTuiPackage(OpenTapHome home, out string details)
    {
        var rejected = new List<string>();
        foreach (var package in OpenTapHomeBootstrapper.ListInstalledPackages(home)
            .Where(p => p.Name.Contains("TUI", StringComparison.OrdinalIgnoreCase)))
        {
            var files = XDocument.Load(Path.Combine(package.Path, "package.xml")).Descendants()
                .Where(e => e.Name.LocalName == "File")
                .Select(e => (string?)e.Attribute("Path"))
                .OfType<string>()
                .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(file).Contains("TUI", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var file in files)
            {
                var path = Path.GetFullPath(Path.Combine(package.Path, file));
                if (IsTuiAssembly(path))
                {
                    details = string.Empty;
                    return true;
                }
                rejected.Add($"Declared DLL '{path}' is missing or is not a readable managed TUI assembly.");
            }
            if (files.Length == 0) rejected.Add($"Package '{package.Name}' declares no TUI DLL.");
        }
        details = rejected.Count > 0 ? string.Join(" ", rejected) : "No TUI package is installed.";
        return false;
    }

    private static bool IsTuiAssembly(string path)
    {
        try
        {
            return File.Exists(path) && AssemblyName.GetAssemblyName(path).Name?.Contains("TUI", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool IsWritableWorkspace(AuthoringWorkspace workspace)
    {
        if (workspace.IsReadOnly || !Directory.Exists(workspace.Root)) return false;
        foreach (var directory in new[] { workspace.Root, ResolvePlansDirectory(workspace) })
        {
            if (!Directory.Exists(directory)) return false;
            if (!OperatingSystem.IsWindows())
            {
                var writes = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
                if ((File.GetUnixFileMode(directory) & writes) == 0) return false;
            }
        }
        return true;
    }

    private static bool ProbeWindowsWorkspaceWriteAccess(AuthoringWorkspace workspace)
    {
        if (!OperatingSystem.IsWindows()) return true;
        // A directory's ReadOnly attribute does not describe Windows write access.
        // Probe only during preflight, never while evaluating UI bindings.
        foreach (var directory in new[] { workspace.Root, ResolvePlansDirectory(workspace) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var probe = new FileStream(Path.Combine(directory, ".authoring-pack-probe-" + Guid.NewGuid().ToString("N")),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        return true;
    }

    private static string ResolveEntry(AuthoringWorkspace workspace, string entry)
        => Path.GetFullPath(Path.IsPathRooted(entry) ? entry : Path.Combine(workspace.Root, entry));

    private static void InspectHome(OpenTapHome home, IEnumerable<string> dependencies, List<PackPreflightFinding> findings)
    {
        foreach (var file in new[] { "tap.dll", "tap.runtimeconfig.json", "OpenTap.dll", "OpenTap.Package.dll" })
        {
            var path = Path.Combine(home.Root, file);
            if (!File.Exists(path))
            {
                findings.Add(new("PACK_RUNTIME_MISSING", $"Required OpenTAP runtime file '{file}' is missing; bootstrap this home.", true, home.Root));
                continue;
            }
            try
            {
                if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    _ = AssemblyName.GetAssemblyName(path);
                else
                {
                    using var config = JsonDocument.Parse(File.ReadAllText(path));
                    if (config.RootElement.ValueKind != JsonValueKind.Object
                        || !config.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions)
                        || runtimeOptions.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("The runtime configuration must contain runtimeOptions.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or BadImageFormatException or UnauthorizedAccessException or JsonException)
            {
                findings.Add(new("PACK_RUNTIME_CORRUPT", $"Required OpenTAP runtime file '{file}' is unreadable or invalid; bootstrap this home. {ex.Message}", true, path));
            }
        }
        var packages = OpenTapHomeBootstrapper.ListInstalledPackages(home);
        foreach (var dependency in dependencies)
            if (!packages.Any(p => string.Equals(p.Name, dependency, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("PACK_PACKAGE_MISSING", $"Required package '{dependency}' is not installed; bootstrap or install it in this home.", true, home.Root));
    }
}
