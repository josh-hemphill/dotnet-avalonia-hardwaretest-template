using System.IO.Compression;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringImportBoundaryTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
    private static AuthoringOperationCoordinator Coordinator() => new(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")));

    [Fact]
    public async Task Required_packages_can_be_imported_incrementally_even_when_instrument_components_is_still_missing()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        workspace.Manifest.Dependencies.Add(new() { Package = "First", Version = "^1.0.0" });
        workspace.Manifest.Dependencies.Add(new() { Package = OpenTapHomeBootstrapper.InstrumentComponentsPackageName, Version = "^0.1.0" });
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        using var coordinator = Coordinator();
        foreach (var name in new[] { "First", OpenTapHomeBootstrapper.InstrumentComponentsPackageName })
        {
            var archive = name == OpenTapHomeBootstrapper.InstrumentComponentsPackageName
                ? PublishedLibraryFixture.Archive : Path.Combine(root, name + ".TapPackage");
            if (name == "First") WriteArchive(archive, name, "1.2.0", "payload.txt", true);
            await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archive);
            Assert.True(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(p => p.Package == name).Satisfied);
            if (name == "First")
            {
                Assert.False(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(p => p.Package == OpenTapHomeBootstrapper.InstrumentComponentsPackageName).Satisfied);
                var before = Snapshot(home.Root);
                workspace.Manifest.InstrumentComponentsPackage = "missing.TapPackage";
                AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
                await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root));
                AssertSnapshot(before, home.Root);
                workspace.Manifest.InstrumentComponentsPackage = null;
                AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
            }
        }
        Assert.All(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Where(p => !p.Optional), p => Assert.True(p.Satisfied));
    }

    [Theory]
    [InlineData("optional", "missing")]
    [InlineData("undeclared", "missing")]
    [InlineData("optional", "unsafe")]
    [InlineData("undeclared", "unsafe")]
    [InlineData("optional", "version")]
    [InlineData("undeclared", "invalid-version")]
    [InlineData("required", "borrow")]
    public async Task Imported_payload_and_version_are_validated_independently_of_selected_home(string declaration, string invalid)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var dependency = new AuthoringPackageDependency { Package = "Independent", Version = "^1.0.0" };
        if (declaration == "required") workspace.Manifest.Dependencies.Add(dependency);
        if (declaration == "optional") workspace.Manifest.OptionalDependencies.Add(new() { Package = dependency.Package, Version = dependency.Version });
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var installed = Path.Combine(home.Root, "Packages", "Independent"); Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "payload.txt"), "existing payload must never repair an incomplete import");
        var before = Snapshot(home.Root); var archive = Path.Combine(root, "independent.TapPackage");
        WriteArchive(archive, "Independent", invalid == "version" ? "2.0.0" : invalid == "invalid-version" ? "invalid" : "1.1.0", invalid == "unsafe" ? "../payload.txt" : "payload.txt", invalid is "version" or "invalid-version");
        using var coordinator = Coordinator();
        await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archive));
        AssertSnapshot(before, home.Root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_undeclared_package_supports_flat_and_real_root_relative_archive_layouts(bool rooted)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var archive = Path.Combine(root, "layout.TapPackage");
        WriteArchive(archive, "Layout", "1.0.0", "Plugins/Layout/payload.txt", true, rooted);
        using var coordinator = Coordinator();
        await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archive);
        var metadata = OpenTapHomeBootstrapper.ListInstalledPackages(home).Single(p => p.Name == "Layout");
        var payload = rooted ? Path.Combine(home.Root, "Plugins", "Layout", "payload.txt") : Path.Combine(metadata.Path, "Plugins", "Layout", "payload.txt");
        Assert.Equal("owned archive payload", File.ReadAllText(payload));
    }

    [Theory]
    [InlineData("public", "version")]
    [InlineData("vm", "version")]
    [InlineData("cli", "version")]
    [InlineData("public", "missing")]
    [InlineData("vm", "missing")]
    [InlineData("cli", "missing")]
    public void All_ordinary_prepare_entrypoints_reject_unready_environment_without_mutating_selected_bytes(string entrypoint, string invalid)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        if (invalid == "version") workspace.Manifest.Dependencies.Single(p => p.Package == "OpenTAP").Version = "99.0.0";
        else { workspace.Manifest.Dependencies.Add(new() { Package = "Still missing", Version = "1.0.0" }); File.Delete(Path.Combine(home.Root, "tap.dll")); }
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest); var before = Snapshot(home.Root);
        if (entrypoint == "public") Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true }));
        else if (entrypoint == "vm")
        {
            var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.OpenTapHomeOverride = home.Root;
            Assert.Throws<AuthoringWorkspaceException>(() => vm.Bootstrap(new() { Offline = true }));
            Assert.DoesNotContain("OpenTAP home " + home.Root, vm.Status ?? "");
        }
        else
        {
            var output = new StringWriter(); var error = new StringWriter();
            Assert.NotEqual(0, AuthoringCli.Run(["--bootstrap", root, "--opentap-home", home.Root, "--offline"], output, error));
            Assert.NotEqual("", error.ToString());
        }
        AssertSnapshot(before, home.Root);
    }

    [Fact]
    public void Corrected_home_clears_saved_preview_path_warning_with_excluded_uncompiled_source()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        workspace.Manifest.Package.Name = "Supported"; workspace.Manifest.ExcludedProgramIds.Add("broken"); AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var path = new AuthoringDocumentStore(root).GetDocumentPath("broken"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "{");
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.OpenTapHomeOverride = "invalid\0home";
        Assert.True(vm.SaveAll().Succeeded); Assert.Contains("home", vm.SavePreviewWarning!);
        vm.OpenTapHomeOverride = home.Root; Assert.True(vm.HasUncompiledSources);
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error);
        Assert.True(vm.SaveAll().Succeeded);
        Assert.Null(vm.EnvironmentPathError); Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error); Assert.Equal("{", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_engine_import_publishes_its_own_real_runtime_to_root_and_ignores_unlisted_overwrites(bool rooted)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var engine = OpenTapHomeBootstrapper.ListInstalledPackages(home).Single(package => package.Name == "OpenTAP");
        var names = new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json" };
        var payload = names.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(home.Root, name)));
        var sentinel = Path.Combine(home.Root, "selected.marker"); File.WriteAllText(sentinel, "preserve unrelated home file");
        var archivePath = Path.Combine(root, "engine.TapPackage");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(rooted ? "Packages/OpenTAP/package.xml" : "package.xml").Open()))
                writer.Write($"<Package Name=\"OpenTAP\" Version=\"{engine.Version}\"><Files>{string.Join("", names.Select(name => $"<File Path=\"{name}\"/>"))}</Files></Package>");
            foreach (var file in payload) { using var stream = archive.CreateEntry(file.Key).Open(); stream.Write(file.Value); }
            using var extra = new StreamWriter(archive.CreateEntry("selected.marker").Open()); extra.Write("unlisted replacement");
        }
        File.WriteAllText(Path.Combine(home.Root, "OpenTap.dll"), "old root engine sentinel");
        using var coordinator = Coordinator();
        await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archivePath);
        foreach (var file in payload) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
        Assert.Equal(engine.Version, OpenTapHomeBootstrapper.ListInstalledPackages(home).Single(package => package.Name == "OpenTAP").Version);
        Assert.True(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(package => package.Package == "OpenTAP").Satisfied);
        Assert.Equal("preserve unrelated home file", File.ReadAllText(sentinel));
    }

    [Theory]
    [InlineData("OpenTap.dll")]
    [InlineData("OpenTap.Package.dll")]
    [InlineData("tap.dll")]
    [InlineData("tap.runtimeconfig.json")]
    [InlineData("tap")]
    [InlineData("tap.exe")]
    public void Non_engine_root_layout_cannot_replace_reserved_runtime_files(string runtime)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var before = Snapshot(home.Root); var archive = Path.Combine(root, "foreign.TapPackage");
        WriteArchive(archive, "Foreign", "1.0.0", runtime, true, rooted: true);
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, OfflinePackagePath = archive, Offline = true }));
        AssertSnapshot(before, home.Root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unpacked_import_rejects_duplicate_and_case_colliding_declarations_without_changing_home(bool differingCase)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var folder = Path.Combine(root, "unpacked"); Directory.CreateDirectory(folder);
        var second = differingCase ? "Payload.txt" : "payload.txt";
        File.WriteAllText(Path.Combine(folder, "package.xml"), $"<Package Name=\"Unpacked\" Version=\"1.0.0\"><Files><File Path=\"payload.txt\"/><File Path=\"{second}\"/></Files></Package>");
        File.WriteAllText(Path.Combine(folder, "payload.txt"), "first bytes"); File.WriteAllText(Path.Combine(folder, second), "second bytes");
        var before = Snapshot(home.Root);
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, OfflinePackagePath = folder, Offline = true }));
        AssertSnapshot(before, home.Root);
    }

    [Fact]
    public void Public_unpacked_import_publishes_complete_package_while_other_required_package_stays_missing()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        workspace.Manifest.Dependencies.Add(new() { Package = "Unpacked", Version = "^1.0.0" });
        workspace.Manifest.Dependencies.Add(new() { Package = OpenTapHomeBootstrapper.InstrumentComponentsPackageName, Version = "^0.1.0" });
        var folder = Path.Combine(root, "unpacked"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "package.xml"), "<Package Name=\"Unpacked\" Version=\"1.2.0\"><Files><File Path=\"payload.txt\"/></Files></Package>");
        File.WriteAllText(Path.Combine(folder, "payload.txt"), "complete owned package");
        new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, OfflinePackagePath = folder, Offline = true });
        Assert.True(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(package => package.Package == "Unpacked").Satisfied);
        Assert.False(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(package => package.Package == OpenTapHomeBootstrapper.InstrumentComponentsPackageName).Satisfied);
        Assert.Equal("complete owned package", File.ReadAllText(Path.Combine(home.Root, "Packages", "Unpacked", "payload.txt")));
    }

    private static void WriteArchive(string path, string name, string version, string declared, bool include, bool rooted = false)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(rooted ? $"Packages/{name}/package.xml" : "package.xml").Open()))
            writer.Write($"<Package Name=\"{name}\" Version=\"{version}\"><Files><File Path=\"{declared}\"/></Files></Package>");
        if (include) { using var writer = new StreamWriter(archive.CreateEntry(declared).Open()); writer.Write("owned archive payload"); }
    }
    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p), File.ReadAllBytes);
    private static void AssertSnapshot(Dictionary<string, byte[]> expected, string root)
    {
        var actual = Snapshot(root); Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var file in expected) Assert.Equal(file.Value, actual[file.Key]);
    }
}
