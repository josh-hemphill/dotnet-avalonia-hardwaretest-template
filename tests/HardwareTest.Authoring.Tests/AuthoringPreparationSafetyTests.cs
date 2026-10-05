using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringPreparationSafetyTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
    public static IEnumerable<object[]> UnsafePrepareCases()
    {
        foreach (var optional in new[] { true, false })
            foreach (var entrypoint in new[] { "public", "vm", "cli", "coordinator" })
                foreach (var path in new[] { "parent", "absolute", "escape" }) yield return [optional, entrypoint, path];
        foreach (var optional in new[] { true, false })
            foreach (var entrypoint in new[] { "public-import", "coordinator-import" }) yield return [optional, entrypoint, "parent"];
    }

    [Theory]
    [MemberData(nameof(UnsafePrepareCases))]
    public async Task Every_prepare_entrypoint_refuses_unsafe_optional_or_undeclared_installed_declarations_before_publication(bool optional, string entrypoint, string path)
    {
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var home = AuthoringBuildSnapshotTests.Home(workspace);
        SelectEngine(workspace, optional); var outside = AuthoringBuildSnapshotTests.Temp(); File.WriteAllText(Path.Combine(outside, "payload.txt"), "preserve external bytes");
        var declared = path == "absolute" ? Path.Combine(outside, "payload.txt") : path == "escape" ? "../../../outside.txt"
            : Path.GetRelativePath(Path.Combine(home.Root, "Packages", "OpenTAP"), Path.Combine(outside, "payload.txt"));
        Declare(Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml"), declared);
        var archive = Path.Combine(workspace.Root, "unrelated.TapPackage"); WriteArchive(archive, "Unrelated");
        var before = Snapshot(home.Root, outside); var source = File.ReadAllBytes(Path.Combine(workspace.Root, "sample.TapPlan")); var manifest = File.ReadAllBytes(Path.Combine(workspace.Root, AuthoringWorkspaceLoader.ManifestFileName));
        if (entrypoint.StartsWith("public", StringComparison.Ordinal))
            Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = entrypoint.EndsWith("import", StringComparison.Ordinal) ? archive : null }));
        else if (entrypoint == "vm")
        {
            var vm = new AuthoringWorkspaceViewModel(); vm.Open(workspace.Root); vm.OpenTapHomeOverride = home.Root;
            Assert.Throws<AuthoringWorkspaceException>(() => vm.Bootstrap(new() { Offline = true }));
            Assert.DoesNotContain("OpenTAP home " + home.Root, vm.Status ?? ""); Assert.Null(vm.LastBuildReceipt); Assert.False(vm.HasUnsavedChanges); vm.StopRecovery();
        }
        else if (entrypoint == "cli")
        {
            var output = new StringWriter(); var error = new StringWriter();
            Assert.NotEqual(0, AuthoringCli.Run(["--bootstrap", workspace.Root, "--opentap-home", home.Root, "--offline"], output, error));
            Assert.Equal("", output.ToString()); Assert.NotEqual("", error.ToString());
        }
        else
        {
            using var coordinator = Coordinator();
            await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, workspace.Root, home: home.Root, offlinePackagePath: entrypoint.EndsWith("import", StringComparison.Ordinal) ? archive : null));
        }
        Assert.Equal(before, Snapshot(home.Root, outside)); Assert.Equal(source, File.ReadAllBytes(Path.Combine(workspace.Root, "sample.TapPlan")));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(workspace.Root, AuthoringWorkspaceLoader.ManifestFileName)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Safe_incremental_import_can_repair_installed_optional_metadata_before_final_safety_check(bool optional)
    {
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var home = AuthoringBuildSnapshotTests.Home(workspace); SelectEngine(workspace, optional);
        workspace.Manifest.OptionalDependencies.Add(new() { Package = "Repairable", Version = "^1.0.0" });
        workspace.Manifest.Dependencies.Add(new() { Package = "Still missing", Version = "^1.0.0" });
        workspace.Manifest.Dependencies.Add(new() { Package = OpenTapHomeBootstrapper.InstrumentComponentsPackageName, Version = "^1.0.0" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var package = Path.Combine(home.Root, "Packages", "Repairable"); Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "package.xml"), "<Package Name=\"Repairable\" Version=\"1.0.0\"><Files><File Path=\"../../../unsafe.txt\"/></Files></Package>");
        var archive = Path.Combine(workspace.Root, "repair.TapPackage"); WriteArchive(archive, "Repairable");
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(workspace.Root); vm.OpenTapHomeOverride = home.Root;
        vm.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => action());
        var result = await vm.RunOperationAsync(AuthoringOperationKind.Bootstrap, offlinePackagePath: archive);
        Assert.Equal(home.Root, result.Home); Assert.Null(vm.Error); Assert.Null(result.Build); Assert.Null(vm.LastBuildReceipt); Assert.Equal("Not checked", vm.CompatibilityState);
        Assert.Contains(vm.EnvironmentPackages, p => p.Package == "Repairable" && p.Satisfied && p.InstalledVersion == "1.1.0");
        Assert.Contains(vm.EnvironmentPackages, p => p.Package == "Still missing" && !p.Satisfied); Assert.False(vm.CanPack);
        Assert.Equal("safe incoming payload", File.ReadAllText(Path.Combine(package, "payload.txt")));
        var before = Snapshot(home.Root); Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true }));
        Assert.Equal(before, Snapshot(home.Root)); await vm.StopOperationsAsync(); vm.StopRecovery();
    }

    [Fact]
    public void Missing_selected_child_beneath_escaping_ancestor_is_rejected_before_any_external_directory_is_created()
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace()); var outside = AuthoringBuildSnapshotTests.Temp();
        var alias = Path.Combine(workspace.Root, "outside-alias"); Directory.CreateSymbolicLink(alias, outside);
        var selected = Path.Combine(alias, "absent-home"); Assert.False(Directory.Exists(selected)); Assert.False(File.Exists(selected));
        Assert.False(File.Exists(alias)); Assert.True(Directory.Exists(alias));
        var before = Snapshot(workspace.Root, outside);
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = selected, Offline = true }));
        Assert.Equal(before, Snapshot(workspace.Root, outside)); Assert.False(Directory.Exists(Path.Combine(outside, "absent-home")));
    }

    private static AuthoringOperationCoordinator Coordinator() => new(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")));
    private static void SelectEngine(AuthoringWorkspace workspace, bool optional)
    {
        workspace.Manifest.Dependencies.Clear(); workspace.Manifest.OptionalDependencies.Clear();
        if (optional) workspace.Manifest.OptionalDependencies.Add(new() { Package = "OpenTAP", Version = "^9.32.2" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
    }
    private static void Declare(string metadata, string path)
    {
        var xml = XDocument.Load(metadata); var ns = xml.Root!.Name.Namespace;
        xml.Root.Add(new XElement(ns + "Files", new XElement(ns + "File", new XAttribute("Path", path)))); xml.Save(metadata);
    }
    private static void WriteArchive(string path, string name)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("package.xml").Open())) writer.Write($"<Package Name=\"{name}\" Version=\"1.1.0\"><Files><File Path=\"payload.txt\"/></Files></Package>");
        using var payload = new StreamWriter(archive.CreateEntry("payload.txt").Open()); payload.Write("safe incoming payload");
    }
    private static string[] Snapshot(params string[] roots)
    {
        var entries = new List<string>(); foreach (var root in roots) Walk(root); return entries.Order(StringComparer.Ordinal).ToArray();
        void Walk(string path)
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (entry.LinkTarget is { } link) { entries.Add(path + "=>" + link); return; }
            if (entry is DirectoryInfo) { entries.Add(path + "=directory"); foreach (var child in Directory.EnumerateFileSystemEntries(path)) Walk(child); }
            else entries.Add(path + "=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }
}
