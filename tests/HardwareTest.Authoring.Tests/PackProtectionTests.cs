using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class PackProtectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-pack-protection-" + Guid.NewGuid().ToString("N"));
    private readonly string _workspace;
    private readonly string _originalPackageXml;

    public PackProtectionTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "dirs.proj"))) source = source.Parent;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(_workspace, Path.GetFileName(file)));
        _originalPackageXml = File.ReadAllText(Path.Combine(_workspace, "package.xml"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dirty_edit_or_new_program_blocks_before_home_and_output_writes(bool create)
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_workspace);
        if (create) vm.InitializePlan(new("unsaved") { Instruments = [] });
        vm.DisplayName = "edited";
        var output = Path.Combine(_root, "dist");
        var home = Path.Combine(_root, "home");
        vm.OpenTapHomeOverride = home;
        var ex = Assert.Throws<PackPreflightException>(() => vm.Pack(output));
        Assert.False(vm.CanPack);
        Assert.NotEmpty(vm.DirtyProgramIds);
        Assert.Same(ex.Report, vm.LastPackPreflight);
        Assert.All(ex.Report.Findings, f => Assert.Equal("PACK_DIRTY", f.Code));
        Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(home));
        Assert.Equal(File.ReadAllText(Path.Combine(_workspace, "package.xml")), _originalPackageXml);
    }

    [Fact]
    public void Preference_home_is_used_when_options_omit_home_and_explicit_home_wins()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_workspace);
        var configured = Path.Combine(_root, "configured");
        vm.OpenTapHomeOverride = configured;
        var checker = new RecordingBlocker();
        Assert.Throws<PackPreflightException>(() => vm.Pack(Path.Combine(_root, "dist"), new PackOptions { Compat = checker }));
        Assert.NotEqual(configured, checker.Home!.Root); // Compatibility checks use the captured home in staging.
        Assert.Equal(configured, vm.LastPackPreflight!.Home!.Root);
        var explicitHome = new OpenTapHomeBootstrapper().Bootstrap(vm.Workspace!, new BootstrapOptions { HomeDirectory = Path.Combine(_root, "explicit"), Offline = true });
        Assert.Throws<PackPreflightException>(() => vm.Pack(Path.Combine(_root, "dist"), new PackOptions { Home = explicitHome, Compat = checker }));
        Assert.NotEqual(explicitHome.Root, checker.Home.Root);
        Assert.Equal(explicitHome.Root, vm.LastPackPreflight!.Home!.Root);
    }

    [Fact]
    public void Blocked_report_retains_details_and_preserves_existing_artifacts()
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var home = Bootstrap(workspace);
        var output = Path.Combine(_root, "dist");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "ship-manifest.json"), "sentinel");
        File.WriteAllText(Path.Combine(_workspace, "package.xml"), "existing xml");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home, Compat = new RecordingBlocker() }));
        Assert.Contains(ex.Report.Compatibility!.RoundTrips, f => f.Message == "actionable blocker");
        Assert.Contains("PACK_COMPAT/TYPE_UNKNOWN", ex.Message);
        Assert.Equal("sentinel", File.ReadAllText(Path.Combine(output, "ship-manifest.json")));
        Assert.Equal("existing xml", File.ReadAllText(Path.Combine(_workspace, "package.xml")));
    }

    [Fact]
    public void Default_checker_runs_for_direct_and_cli_pack_omitting_injection()
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var home = Bootstrap(workspace);
        var report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = home });
        Assert.False(report.HasErrors, string.Join("\n", report.Findings));
        Assert.NotNull(report.Compatibility);
        Assert.Contains(report.Findings, f => f.Code == "PACK_COMPAT_SCOPE");
        Directory.Delete(Path.Combine(home.Root, "Packages", "HardwareTest Mixins"), true);
        var output = Path.Combine(_root, "dist");
        var error = new StringWriter();
        Assert.Equal(1, AuthoringCli.Run(["--pack", _workspace, "--out", output, "--opentap-home", home.Root], new StringWriter(), error));
        Assert.Contains("PACK_PACKAGE_MISSING", error.ToString());
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Excluded_invalid_plan_does_not_block_selected_package_but_included_invalid_plan_does()
    {
        File.WriteAllText(Path.Combine(_workspace, "board-demo.TapPlan"), "invalid xml");
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = Bootstrap(workspace) });
        Assert.False(report.HasErrors, string.Join("\n", report.Findings));
        Assert.Contains(report.ExcludedPlans, p => p.EndsWith("board-demo.TapPlan", StringComparison.Ordinal));
        Assert.Equal(PackageXmlRenderer.EnumeratePackFiles(workspace), report.IncludedFiles);
        File.WriteAllText(Path.Combine(_workspace, "sample.TapPlan"), "invalid xml");
        report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = report.Home });
        Assert.True(report.HasErrors);
        Assert.NotNull(report.Contract);
        Assert.Contains(report.Findings, f => f.Code.StartsWith("PACK_CONTRACT", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_runtime_plugin_shell_or_tui_prerequisites_are_actionable()
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var home = Bootstrap(workspace);
        File.Delete(Path.Combine(home.Root, "tap.dll"));
        var report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = home });
        Assert.Contains(report.Findings, f => f.Code == "PACK_RUNTIME_MISSING" && f.IsError);
        workspace.Manifest.IncludeTui = true;
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = home });
        Assert.Contains(report.Findings, f => f.Code == "PACK_TUI_MISSING" && f.IsError);
        workspace.Manifest.PluginProjects = ["absent.TapPackage"];
        workspace.Manifest.ShellAppProjects = ["wrong.txt"];
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var output = Path.Combine(_root, "dist");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions()));
        Assert.Contains(ex.Report.Findings, f => f.Code == AuthoringPackCodes.PluginMissing);
        Assert.Contains(ex.Report.Findings, f => f.Code == AuthoringPackCodes.ShellAppFailed);
        Assert.False(Directory.Exists(output));
        Assert.Equal(File.ReadAllText(Path.Combine(_workspace, "package.xml")), _originalPackageXml);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_or_read_only_workspace_fails_before_default_home_and_artifacts(bool missing)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        workspace = missing ? workspace with { Root = Path.Combine(_root, "absent") } : workspace with { IsReadOnly = true };
        var output = Path.Combine(_root, "dist");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions()));
        Assert.Contains(ex.Report.Findings, f => f.Code == "PACK_WORKSPACE");
        Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".authoring")));
    }

    [Theory]
    [InlineData("tap.dll", "corrupt")]
    [InlineData("tap.runtimeconfig.json", "invalid json")]
    [InlineData("tap.runtimeconfig.json", "{}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":false}}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"framework\":{\"name\":false,\"version\":\"9.0.0\"}}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"framework\":{\"name\":\"wrong\",\"version\":\"9.0.0\"}}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"invalid\"}}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":[]}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":{}}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":[{}]}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":false}]}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"wrong\",\"version\":\"9.0.0\"}]}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"invalid\"}]}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"9.0.0\"},{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"9.0.0\"}]}}")]
    [InlineData("tap.runtimeconfig.json", "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"},\"frameworks\":[{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"9.0.0\"}]}}")]
    public void Corrupt_runtime_blocks_before_existing_artifacts_are_changed(string file, string contents)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var home = Bootstrap(workspace);
        File.WriteAllText(Path.Combine(home.Root, file), contents);
        var output = Path.Combine(_root, "dist");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, "ship-manifest.json");
        File.WriteAllText(existing, "sentinel");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home }));
        Assert.Contains(ex.Report.Findings, f => f.Code == "PACK_RUNTIME_CORRUPT" && f.IsError);
        Assert.Equal("sentinel", File.ReadAllText(existing));
        Assert.Equal(_originalPackageXml, File.ReadAllText(Path.Combine(_workspace, "package.xml")));
    }

    [Theory]
    [InlineData("framework", "{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"}")]
    [InlineData("frameworks", "[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"}]")]
    [InlineData("framework", "{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"9.0.0\"}")]
    [InlineData("frameworks", "[{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"9.0.0\"}]")]
    [InlineData("framework", "{\"name\":\"Microsoft.AspNetCore.App\",\"version\":\"9.0.0\"}")]
    [InlineData("frameworks", "[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"},{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"9.0.0\"}]")]
    public void Supported_runtime_framework_declaration_forms_pass_prerequisite_validation(string property, string value)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var home = Bootstrap(workspace);
        File.WriteAllText(Path.Combine(home.Root, "tap.runtimeconfig.json"), $"{{\"runtimeOptions\":{{\"{property}\":{value}}}}}");
        var report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = home });
        Assert.False(report.HasErrors, string.Join("\n", report.Findings));
    }

    [Theory]
    [InlineData("[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"}]")]
    [InlineData("{}")]
    public void Self_contained_runtime_configuration_blocks_before_existing_artifacts_are_changed(string includedFrameworks)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var home = Bootstrap(workspace);
        File.WriteAllText(Path.Combine(home.Root, "tap.runtimeconfig.json"), $"{{\"runtimeOptions\":{{\"includedFrameworks\":{includedFrameworks}}}}}");
        var output = Path.Combine(_root, "dist");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, "ship-manifest.json");
        File.WriteAllText(existing, "sentinel");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home }));
        Assert.Contains(ex.Report.Findings, f => f.Code == "PACK_RUNTIME_CONFIG" && f.IsError
            && f.Message.Contains("framework-dependent", StringComparison.Ordinal));
        Assert.Equal("sentinel", File.ReadAllText(existing));
        Assert.Equal(_originalPackageXml, File.ReadAllText(Path.Combine(_workspace, "package.xml")));
    }

    [Fact]
    public void Unix_owner_without_write_access_blocks_before_home_output_or_package_changes_and_cleans_probes()
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        var plans = Path.Combine(_workspace, "plans");
        Directory.CreateDirectory(plans);
        foreach (var file in Directory.EnumerateFiles(_workspace).Where(f => Path.GetFileName(f) != "authoring.json"))
            File.Move(file, Path.Combine(plans, Path.GetFileName(file)));
        workspace.Manifest.PlansDirectory = "plans";
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        workspace = workspace with { TapPlanPaths = workspace.TapPlanPaths.Select(p => Path.Combine(plans, Path.GetFileName(p))).ToArray() };
        var packageXml = Path.Combine(plans, "package.xml");
        File.WriteAllText(packageXml, "sentinel xml");
        var output = Path.Combine(_root, "dist");
        var home = Path.Combine(_root, "home");
        var mode = File.GetUnixFileMode(plans);
        try
        {
            // 0577: the owning user has no write permission even though group/other do.
            File.SetUnixFileMode(plans, UnixFileMode.UserRead | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            var canStillWrite = false;
            try
            {
                using var probe = new FileStream(Path.Combine(plans, ".permission-test-probe-" + Guid.NewGuid().ToString("N")),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                canStillWrite = true;
            }
            catch (UnauthorizedAccessException)
            {
                // The effective identity observes the intended owner permission denial.
            }
            Assert.Empty(Directory.EnumerateFiles(plans, ".permission-test-probe-*"));
            Assert.SkipWhen(canStillWrite, "The effective process can write despite owner mode 0577 (for example, root); Unix permission denial cannot be exercised.");
            var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output,
                new PackOptions { BootstrapHomeDirectory = home, Offline = true }));
            Assert.Contains(ex.Report.Findings, f => f.Code == "PACK_WORKSPACE");
            Assert.False(Directory.Exists(home));
            Assert.False(Directory.Exists(output));
            Assert.Equal("sentinel xml", File.ReadAllText(packageXml));
            Assert.Empty(Directory.EnumerateFiles(_workspace, ".authoring-pack-probe-*", SearchOption.AllDirectories));
        }
        finally
        {
            File.SetUnixFileMode(plans, mode);
        }
    }

    [Fact]
    public void Windows_directory_readonly_attribute_does_not_disable_pack_or_leave_probe_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_workspace);
        var home = Bootstrap(vm.Workspace!);
        var attributes = File.GetAttributes(_workspace);
        try
        {
            File.SetAttributes(_workspace, attributes | FileAttributes.ReadOnly);
            Assert.True(vm.CanPack);
            var report = WorkspacePacker.Preflight(vm.Workspace!, new PackOptions { Home = home });
            Assert.False(report.HasErrors, string.Join("\n", report.Findings));
            Assert.Empty(Directory.EnumerateFiles(_workspace, ".authoring-pack-probe-*"));
        }
        finally
        {
            File.SetAttributes(_workspace, attributes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tui_named_manifest_with_missing_or_corrupt_declared_dll_is_not_installation_evidence(bool corrupt)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        workspace.Manifest.IncludeTui = true;
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var home = Bootstrap(workspace);
        var package = Path.Combine(home.Root, "Packages", "OpenTAP TUI");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "package.xml"),
            """<Package Name="OpenTAP TUI" Version="1.0.0"><Files><File Path="OpenTap.TUI.dll" /></Files></Package>""");
        if (corrupt) File.WriteAllText(Path.Combine(package, "OpenTap.TUI.dll"), "corrupt");
        var output = Path.Combine(_root, "dist");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home }));
        Assert.Contains(ex.Report.Findings, f => f.Code == "PACK_TUI_MISSING" && f.IsError);
        Assert.False(Directory.Exists(output));
        Assert.Equal(_originalPackageXml, File.ReadAllText(Path.Combine(_workspace, "package.xml")));
    }

    [Theory]
    [InlineData("OpenTap.TUI.dll")]
    [InlineData("../../Plugins/OpenTap.TUI/OpenTap.TUI.dll")]
    public void Include_tui_accepts_managed_synthetic_payload_declared_inside_selected_home(string declaredPath)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        workspace.Manifest.IncludeTui = true;
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var home = Bootstrap(workspace);
        var package = InstallSyntheticTuiManifest(home, declaredPath);
        WriteSyntheticTuiAssembly(Path.GetFullPath(Path.Combine(package, declaredPath)));
        // These fixtures test installation evidence only. Keep their assemblies out
        // of the process-global loader so Windows can delete the fixture payload.
        var checker = new PrerequisiteChecker();
        var report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = home, Compat = checker });
        Assert.False(report.HasErrors, string.Join("\n", report.Findings));
        Assert.True(checker.Called);
        Assert.NotNull(report.Compatibility);
        Assert.Contains(report.Findings, f => f.Code == "PACK_COMPAT_SCOPE" && f.Message.Contains("no external TUI process", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Include_tui_rejects_managed_payload_outside_selected_home(bool absolute)
    {
        var workspace = AuthoringWorkspaceLoader.Load(_workspace);
        workspace.Manifest.IncludeTui = true;
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var home = Bootstrap(workspace);
        var outside = Path.Combine(_root, "home-other", "OpenTap.TUI.dll");
        WriteSyntheticTuiAssembly(outside);
        var package = Path.Combine(home.Root, "Packages", "OpenTAP TUI");
        var declaredPath = absolute ? outside : Path.GetRelativePath(package, outside);
        InstallSyntheticTuiManifest(home, declaredPath);
        var output = Path.Combine(_root, "dist");
        var ex = Assert.Throws<PackPreflightException>(() => WorkspacePacker.Pack(workspace, output,
            new PackOptions { Home = home, Compat = new PrerequisiteChecker() }));
        Assert.Contains(ex.Report.Findings, f => f.Code == "PACK_TUI_MISSING" && f.Message.Contains("outside the selected OpenTAP home", StringComparison.Ordinal));
        Assert.False(Directory.Exists(output));
        Assert.Equal(_originalPackageXml, File.ReadAllText(Path.Combine(_workspace, "package.xml")));
    }

    private static string InstallSyntheticTuiManifest(OpenTapHome home, string declaredPath)
    {
        var package = Path.Combine(home.Root, "Packages", "OpenTAP TUI");
        Directory.CreateDirectory(package);
        var ns = PackageXmlRenderer.PackageNs;
        new XDocument(new XElement(ns + "Package", new XAttribute("Name", "OpenTAP TUI"), new XAttribute("Version", "1.0.0"),
            new XElement(ns + "Files", new XElement(ns + "File", new XAttribute("Path", declaredPath)))))
            .Save(Path.Combine(package, "package.xml"));
        return package;
    }

    private static void WriteSyntheticTuiAssembly(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("OpenTap.TUI"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("OpenTap.TUI");
        module.DefineType("OpenTap.TUI.PrerequisiteFixture", TypeAttributes.Public).CreateType();
        assembly.Save(path);
    }

    private OpenTapHome Bootstrap(AuthoringWorkspace workspace) => new OpenTapHomeBootstrapper().Bootstrap(workspace,
        new BootstrapOptions { HomeDirectory = Path.Combine(_root, "home"), Offline = true });

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class PrerequisiteChecker : ITuiCompatChecker
    {
        public bool Called { get; private set; }
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
        {
            Called = true;
            return new([], []);
        }
    }

    private sealed class RecordingBlocker : ITuiCompatChecker
    {
        public OpenTapHome? Home { get; private set; }
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
        {
            Home = authoringHome;
            return new([], [new("sample.TapPlan", TuiCompatCodes.TypeUnknown, "actionable blocker")]);
        }
    }
}
