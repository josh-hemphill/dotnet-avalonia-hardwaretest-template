using System.Xml.Linq;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringEnvironmentPayloadContainmentTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("file")]
    [InlineData("ancestor")]
    [InlineData("fallback-file")]
    [InlineData("fallback-ancestor")]
    [InlineData("metadata")]
    [InlineData("package")]
    [InlineData("home")]
    [InlineData("home-ancestor")]
    [InlineData("OpenTap.dll")]
    [InlineData("OpenTap.Package.dll")]
    [InlineData("tap.dll")]
    [InlineData("tap.runtimeconfig.json")]
    public void Escaping_installed_payload_is_unavailable_before_public_preflight_checker_and_prepare_preserves_all_inputs(string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        var original = AuthoringBuildSnapshotTests.Home(workspace); var home = original;
        var outside = AuthoringBuildSnapshotTests.Temp(); var package = Path.Combine(home.Root, "Packages", "OpenTAP");
        var metadata = Path.Combine(package, "package.xml");
        if (kind is "file" or "ancestor" or "fallback-file" or "fallback-ancestor")
        {
            var ancestor = kind.EndsWith("ancestor", StringComparison.Ordinal);
            var relative = ancestor ? "linked/payload.txt" : "payload.txt";
            Declare(metadata, relative); File.WriteAllText(Path.Combine(outside, "payload.txt"), "external declared payload");
            var parent = kind.StartsWith("fallback", StringComparison.Ordinal) ? home.Root : package;
            if (ancestor) Directory.CreateSymbolicLink(Path.Combine(parent, "linked"), outside);
            else File.CreateSymbolicLink(Path.Combine(parent, "payload.txt"), Path.Combine(outside, "payload.txt"));
        }
        else if (kind == "metadata")
        {
            var target = Path.Combine(outside, "package.xml"); File.Move(metadata, target); File.CreateSymbolicLink(metadata, target);
        }
        else if (kind == "package")
        {
            var target = Path.Combine(outside, "OpenTAP"); Directory.Move(package, target); Directory.CreateSymbolicLink(package, target);
        }
        else if (kind is "home" or "home-ancestor")
        {
            var link = Path.Combine(outside, "selected");
            if (kind == "home") { Directory.CreateSymbolicLink(link, original.Root); home = new(link); }
            else { Directory.CreateSymbolicLink(link, Path.GetDirectoryName(original.Root)!); home = new(Path.Combine(link, Path.GetFileName(original.Root))); }
        }
        else
        {
            var target = Path.Combine(outside, kind); File.Copy(Path.Combine(home.Root, kind), target);
            File.Delete(Path.Combine(home.Root, kind)); File.CreateSymbolicLink(Path.Combine(home.Root, kind), target);
        }
        var before = Snapshot(original.Root, outside, workspace.Root);
        var checker = new UnexpectedChecker();
        var report = WorkspacePacker.Preflight(workspace, new PackOptions { Home = home, Compat = checker, Offline = true });
        Assert.False(checker.Called); Assert.Null(report.Compatibility);
        Assert.Contains(report.Findings, finding => finding.Code == "PACK_PACKAGE_MISSING" && finding.IsError && finding.Message.Contains("OpenTAP"));
        Assert.False(Assert.Single(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home), p => p.Package == "OpenTAP").Satisfied);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(workspace.Root); vm.OpenTapHomeOverride = home.Root;
        Assert.False(vm.CanPack); Assert.Contains(vm.EnvironmentPackages, p => p.Package == "OpenTAP" && !p.Satisfied); vm.StopRecovery();
        if (kind is "OpenTap.dll" or "OpenTap.Package.dll" or "tap.dll" or "tap.runtimeconfig.json")
            Assert.Contains(AuthoringEnvironmentAssessment.RuntimeFiles(home), line => line.StartsWith(kind + ":", StringComparison.Ordinal) && line.Contains("unsafe"));
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true }));
        Assert.Equal(before, Snapshot(original.Root, outside, workspace.Root));
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("payload-link")]
    [InlineData("metadata-link")]
    [InlineData("runtime-link")]
    public void Regular_and_contained_file_payloads_reach_real_checker_and_publish_production_receipt(string kind)
    {
        if (kind != "regular" && OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var package = Path.Combine(home.Root, "Packages", "OpenTAP"); Declare(Path.Combine(package, "package.xml"), "payload.txt");
        var file = Path.Combine(package, "payload.txt");
        if (kind == "payload-link")
        {
            var target = Path.Combine(home.Root, "contained-payload.txt"); File.WriteAllText(target, "contained declared payload"); File.CreateSymbolicLink(file, target);
        }
        else File.WriteAllText(file, "regular declared payload");
        if (kind is "metadata-link" or "runtime-link")
        {
            var link = kind == "metadata-link" ? Path.Combine(package, "package.xml") : Path.Combine(home.Root, "OpenTap.dll");
            var target = Path.Combine(home.Root, "contained-" + Path.GetFileName(link));
            File.Move(link, target); File.CreateSymbolicLink(link, target);
        }
        Assert.True(Assert.Single(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home), p => p.Package == "OpenTAP").Satisfied);
        var result = AuthoringBuildService.Execute(AuthoringBuildService.CaptureSaved(workspace, new() { Home = home, Offline = true }), AuthoringBuildSnapshotTests.Temp());
        Assert.Contains(result.Receipt.RequiredChecks, check => check.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(result.Receipt.RequiredChecks, check => check.Code == "BUILD_COMPAT_PROVIDER" && check.Message.Contains("Production in-process TuiCompatChecker"));
    }

    [Fact]
    public void Cyclic_payload_links_are_unavailable_without_reaching_checker()
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var package = Path.Combine(home.Root, "Packages", "OpenTAP"); Declare(Path.Combine(package, "package.xml"), "cycle.txt");
        File.CreateSymbolicLink(Path.Combine(package, "cycle.txt"), "cycle.txt"); var checker = new UnexpectedChecker();
        var report = WorkspacePacker.Preflight(workspace, new() { Home = home, Compat = checker, Offline = true });
        Assert.False(checker.Called); Assert.Null(report.Compatibility); Assert.True(report.HasErrors);
        Assert.False(Assert.Single(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home), p => p.Package == "OpenTAP").Satisfied);
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true }));
    }

    [Theory]
    [InlineData(true, "OpenTap.dll")]
    [InlineData(false, "OpenTap.dll")]
    [InlineData(true, "OpenTap.Package.dll")]
    [InlineData(false, "OpenTap.Package.dll")]
    [InlineData(true, "tap.dll")]
    [InlineData(false, "tap.dll")]
    [InlineData(true, "tap.runtimeconfig.json")]
    [InlineData(false, "tap.runtimeconfig.json")]
    [InlineData(true, "home")]
    [InlineData(false, "home")]
    [InlineData(true, "ancestor")]
    [InlineData(false, "ancestor")]
    public void Mandatory_runtime_escape_is_blocked_even_when_engine_dependency_is_optional_or_undeclared(bool optional, string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var original = AuthoringBuildSnapshotTests.Home(workspace);
        SelectEngineDeclaration(workspace, optional); var home = original; var outside = AuthoringBuildSnapshotTests.Temp();
        if (kind is "home" or "ancestor")
        {
            var link = Path.Combine(outside, "selected");
            Directory.CreateSymbolicLink(link, kind == "home" ? original.Root : Path.GetDirectoryName(original.Root)!);
            home = new(kind == "home" ? link : Path.Combine(link, Path.GetFileName(original.Root)));
        }
        else
        {
            var file = Path.Combine(home.Root, kind); var target = Path.Combine(outside, kind);
            File.Move(file, target); File.CreateSymbolicLink(file, target);
        }
        var before = Snapshot(original.Root, outside, workspace.Root); var checker = new UnexpectedChecker();
        var report = WorkspacePacker.Preflight(workspace, new() { Home = home, Compat = checker, Offline = true });
        Assert.False(checker.Called); Assert.Null(report.Compatibility);
        Assert.Contains(report.Findings, finding => finding.Code == "PACK_RUNTIME_MISSING" && finding.IsError);
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true }));
        Assert.Equal(before, Snapshot(original.Root, outside, workspace.Root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Contained_runtime_is_supported_with_optional_or_undeclared_engine_and_missing_optional_package(bool optional)
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var home = AuthoringBuildSnapshotTests.Home(workspace);
        SelectEngineDeclaration(workspace, optional);
        workspace.Manifest.OptionalDependencies.Add(new() { Package = "Optional absent fixture", Version = "^1.0.0" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var file = Path.Combine(home.Root, "OpenTap.dll"); var target = Path.Combine(home.Root, "contained-engine.dll");
        File.Move(file, target); File.CreateSymbolicLink(file, target);
        Assert.Contains(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home), p => p.Package == "Optional absent fixture" && p.Optional && !p.Satisfied);
        var result = AuthoringBuildService.Execute(AuthoringBuildService.CaptureSaved(workspace, new() { Home = home, Offline = true }), AuthoringBuildSnapshotTests.Temp());
        Assert.Contains(result.Receipt.RequiredChecks, check => check.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(result.Receipt.RequiredChecks, check => check.Code == "BUILD_COMPAT_PROVIDER" && check.Message.Contains("Production in-process TuiCompatChecker"));
    }

    private static void SelectEngineDeclaration(AuthoringWorkspace workspace, bool optional)
    {
        workspace.Manifest.Dependencies.Clear(); workspace.Manifest.OptionalDependencies.Clear();
        if (optional) workspace.Manifest.OptionalDependencies.Add(new() { Package = "OpenTAP", Version = "^9.32.2" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
    }

    private static void Declare(string metadata, string relative)
    {
        var document = XDocument.Load(metadata); var root = document.Root!;
        var files = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Files");
        if (files is null) { files = new(root.Name.Namespace + "Files"); root.Add(files); }
        files.Add(new XElement(root.Name.Namespace + "File", new XAttribute("Path", relative))); document.Save(metadata);
    }
    private static string[] Snapshot(params string[] roots)
    {
        var entries = new List<string>(); foreach (var root in roots) Walk(root); return entries.Order(StringComparer.Ordinal).ToArray();
        void Walk(string path)
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (entry.LinkTarget is { } link) { entries.Add(path + "=>" + link); return; }
            if (entry is DirectoryInfo) foreach (var child in Directory.EnumerateFileSystemEntries(path)) Walk(child);
            else entries.Add(path + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
    }
    private sealed class UnexpectedChecker : ITuiCompatChecker
    {
        public bool Called { get; private set; }
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
        { Called = true; throw new InvalidOperationException("Unsafe installed payload reached checker."); }
    }
}
