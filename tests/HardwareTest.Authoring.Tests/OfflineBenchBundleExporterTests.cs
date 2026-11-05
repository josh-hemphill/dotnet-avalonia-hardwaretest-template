using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class OfflineBenchBundleExporterTests : IDisposable
{
    private readonly string _archives = AuthoringBuildSnapshotTests.Temp();
    private readonly string _owned = AuthoringBuildSnapshotTests.Temp();
    private readonly string _output = AuthoringBuildSnapshotTests.Temp();
    private static readonly OfflineBenchExportOptions Linux = new("Linux", "x64");

    [Fact]
    public void Dependency_diamond_backtracks_and_pins_one_compatible_version_per_identity()
    {
        Archive("A", "1.1.0", dependencies: [("C", "^2.0.0")]);
        Archive("A", "1.0.0", dependencies: [("C", "^1.0.0")]);
        Archive("B", "1.0.0", dependencies: [("C", "^1.0.0")]);
        Archive("C", "2.0.0"); Archive("C", "1.0.0");
        var result = Resolve(Manifest(("A", "^1.0.0"), ("B", "^1.0.0")));
        Assert.Equal(["A", "B", "C"], result.Select(package => package.Name));
        Assert.All(result, package => Assert.Equal("1.0.0", package.Version.ToString()));
        Assert.All(result, package => Assert.True(File.Exists(package.Archive)));
    }

    [Fact]
    public void Transitive_optional_package_is_included_but_unrelated_optional_is_not_shipped()
    {
        Archive("A", "1.0.0", dependencies: [("Useful optional", "^1.0.0")]);
        Archive("Useful optional", "1.0.0"); Archive("Unrelated", "1.0.0");
        var manifest = Manifest(("A", "^1.0.0"));
        manifest.OptionalDependencies = [new() { Package = "Useful optional", Version = "^1.0.0" }, new() { Package = "Unrelated", Version = "^1.0.0" }];
        Assert.Equal(["A", "Useful optional"], Resolve(manifest).Select(package => package.Name));
    }

    [Fact]
    public void Conflicting_transitive_ranges_fail_with_manual_recovery_instructions()
    {
        Archive("A", "1.0.0", dependencies: [("C", "^1.0.0")]);
        Archive("B", "1.0.0", dependencies: [("C", "^2.0.0")]);
        Archive("C", "1.0.0"); Archive("C", "2.0.0");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("A", "^1.0.0"), ("B", "^1.0.0"))));
        Assert.Contains("conflict", error.Message);
        Assert.Contains("Cannot resolve 'C'", error.Message);
        Assert.Contains("^1.0.0 AND ^2.0.0", error.Message);
        Assert.Contains("package folder", error.Message);
        Assert.Contains("Automatic download is disabled", error.Message);
    }

    [Fact]
    public void Conflict_discovered_after_dependency_selection_reports_that_package_and_all_constraints()
    {
        Archive("A", "1.0.0", dependencies: [("B", "^1.0.0")]);
        Archive("B", "1.0.0"); Archive("B", "2.0.0");
        Archive("Z", "1.0.0", dependencies: [("B", "^2.0.0")]);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("A", "^1.0.0"), ("Z", "^1.0.0"))));
        Assert.Contains("Cannot resolve 'B'", error.Message);
        Assert.Contains("^1.0.0 AND ^2.0.0", error.Message);
        Assert.Contains("Selected candidates:", error.Message);
        Assert.Contains("B 1.0.0", error.Message);
    }

    [Fact]
    public void Transitive_diamond_conflict_names_the_leaf_and_preserves_the_previous_bundle()
    {
        var workspace = SavedProgram();
        Archive("Device", "1.0.0", dependencies: [("A", "^1.0.0"), ("B", "^1.0.0")]);
        Archive("A", "1.0.0", dependencies: [("C", "^1.0.0")]);
        var changed = Archive("B", "1.0.0", dependencies: [("C", "^1.0.0")]);
        Archive("C", "1.0.0"); Archive("C", "2.0.0");
        var options = Linux with { PackageDirectory = _archives };
        var result = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options);
        var retained = File.ReadAllBytes(Path.Combine(result.Directory, OfflineBenchBundle.ManifestFileName));
        File.Delete(changed); Archive("B", "1.0.0", dependencies: [("C", "^2.0.0")]);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options));
        Assert.Contains("Cannot resolve 'C'", error.Message);
        Assert.Contains("^1.0.0 AND ^2.0.0", error.Message);
        Assert.Equal(retained, File.ReadAllBytes(Path.Combine(result.Directory, OfflineBenchBundle.ManifestFileName)));
        OfflineBenchBundle.Validate(result.Directory, "Linux", "x64");
    }

    [Theory]
    [InlineData("Windows", "x64")]
    [InlineData("Linux", "arm64")]
    public void Only_archives_compatible_with_the_selected_bench_target_are_candidates(string os, string architecture)
    {
        Archive("Device", "1.0.0", os: os, architecture: architecture);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("Device", "^1.0.0"))));
        Assert.Contains("Linux/x64", error.Message);
        Assert.Contains("missing", error.Message);
        var options = new OfflineBenchExportOptions(os, architecture);
        var inventory = new OfflineBenchPackageInventory(_owned, options, default);
        inventory.AddFolder(_archives, 0);
        Assert.Single(new OfflineBenchPackageResolver(inventory, _owned, options, default).Resolve(Manifest(("Device", "^1.0.0"))));
    }

    [Fact]
    public void Same_identity_and_version_with_different_payloads_are_not_silently_selected()
    {
        Archive("Device", "1.0.0", payload: "first", suffix: "a");
        Archive("Device", "1.0.0", payload: "different", suffix: "b");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("Device", "^1.0.0"))));
        Assert.Contains("Ambiguous", error.Message);
    }

    [Fact]
    public void Snapshot_recheck_detects_changed_archive_and_added_candidates()
    {
        var archive = Archive("Device", "1.0.0");
        var inventory = new OfflineBenchPackageInventory(_owned, Linux, default);
        inventory.AddFolder(_archives, 0);
        inventory.EnsureCandidates("Device");
        File.AppendAllText(archive, "changed");
        Assert.Throws<AuthoringWorkspaceException>(inventory.Recheck);
        inventory = new OfflineBenchPackageInventory(_owned, Linux, default);
        // Restoring a fresh valid archive allows an independent directory-listing check.
        File.Delete(archive); Archive("Device", "1.0.0");
        inventory.AddFolder(_archives, 0);
        inventory.EnsureCandidates("Device");
        Archive("Added", "1.0.0");
        Assert.Throws<AuthoringWorkspaceException>(inventory.Recheck);
    }

    [Fact]
    public void Unsafe_archive_paths_and_install_actions_are_rejected_without_extracting()
    {
        var path = Archive("Device", "1.0.0");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var output = new StreamWriter(zip.CreateEntry("../outside.txt").Open()); output.Write("escape");
        }
        Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("Device", "^1.0.0"))));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_owned)!, "outside.txt")));
        File.Delete(path);
        Archive("Device", "1.0.0", installAction: true);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("Device", "^1.0.0"))));
        Assert.Contains("install actions", error.Message);
    }

    [Fact]
    public void Unrelated_broken_action_bearing_or_wrong_target_folder_archives_do_not_block_required_closure()
    {
        Archive("Device", "1.0.0");
        Archive("Editor only", "1.0.0", installAction: true);
        Archive("Other target", "1.0.0", os: "Windows");
        File.WriteAllText(Path.Combine(_archives, "broken-optional.TapPackage"), "not a zip");
        Assert.Equal("Device", Assert.Single(Resolve(Manifest(("Device", "^1.0.0")))).Name);
        var action = Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("Editor only", "^1.0.0"))));
        Assert.Contains("install actions", action.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrelated_installed_action_and_missing_payload_are_ignored_with_or_without_manual_archives(bool manual)
    {
        var workspace = SavedProgram();
        var home = AuthoringBuildSnapshotTests.Home(workspace with { Manifest = new AuthoringManifest() });
        Installed("Device", "Packages/Device/payload.txt", "payload", false);
        Installed("Editor only", "Packages/Editor only/missing.txt", null, true);
        Installed("Optional broken", "Packages/Optional broken/missing.txt", null, false);
        if (manual) Archive("Device", "1.0.0");
        var options = new OfflineBenchExportOptions(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture);
        var inventory = new OfflineBenchPackageInventory(_owned, options, default);
        if (manual) inventory.AddFolder(_archives, 0);
        inventory.AddInstalledHome(home.Root);
        var resolved = new OfflineBenchPackageResolver(inventory, _owned, options, default).Resolve(Manifest(("Device", "^1.0.0")));
        Assert.Equal("Device", Assert.Single(resolved).Name);
        inventory.Recheck();

        void Installed(string name, string file, string? payload, bool action)
        {
            var metadata = Path.Combine(home.Root, "Packages", name, "package.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
            new XElement("Package", new XAttribute("Name", name), new XAttribute("Version", "1.0.0"), new XAttribute("OS", OfflineBenchBundle.CurrentOS), new XAttribute("Architecture", OfflineBenchBundle.CurrentArchitecture),
                new XElement("Files", new XElement("File", new XAttribute("Path", file))), action ? new XElement("InstallAction", "editor-only") : null).Save(metadata);
            if (payload is not null) File.WriteAllText(Path.Combine(home.Root, file), payload, new UTF8Encoding(false));
        }
    }

    [Fact]
    public void Broken_installed_home_does_not_block_complete_matching_manual_archives()
    {
        Archive("Device", "1.0.0");
        var home = AuthoringBuildSnapshotTests.Temp();
        var folder = Path.Combine(home, "Packages", "Device"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "package.xml"), "<Package Name=\"Device\" Version=\"1.0.0\"><Files><File Path=\"missing.dll\"/></Files></Package>");
        var inventory = new OfflineBenchPackageInventory(_owned, Linux, default);
        inventory.AddFolder(_archives, 0); inventory.AddInstalledHome(home);
        Assert.Equal("Device", Assert.Single(new OfflineBenchPackageResolver(inventory, _owned, Linux, default).Resolve(Manifest(("Device", "^1.0.0")))).Name);
    }

    [Fact]
    public void Same_bytes_at_different_deployment_paths_are_ambiguous_releases()
    {
        Archive("Device", "1.0.0", suffix: "scoped");
        Archive("Device", "1.0.0", suffix: "root", payloadPath: "payload.txt");
        Assert.Contains("Ambiguous", Assert.Throws<AuthoringWorkspaceException>(() => Resolve(Manifest(("Device", "^1.0.0")))).Message);
    }

    [Fact]
    public void Captured_input_that_grows_past_limit_is_rejected_by_streaming_recheck()
    {
        var path = Path.Combine(_archives, "input.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        var inventory = new OfflineBenchPackageInventory(_owned, Linux, default, maximumBytes: 1024);
        inventory.Capture(path);
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(2048);
        Assert.Contains("size limit", Assert.Throws<AuthoringWorkspaceException>(inventory.Recheck).Message);
        Assert.Contains("size limit", Assert.Throws<AuthoringWorkspaceException>(() => inventory.Capture(path)).Message);
    }

    [Fact]
    public void Successful_bundle_has_checked_program_exact_archives_and_independent_host_validation()
    {
        var workspace = SavedProgram();
        var archive = Archive("Device", "1.0.0", dependencies: [("Support", "^1.0.0")]);
        Archive("Support", "1.0.0"); Archive("Unrelated", "1.0.0");
        var result = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, Linux with { PackageDirectory = _archives });
        var verified = OfflineBenchBundle.Validate(result.Directory, "Linux", "x64");
        Assert.Equal(result.Manifest.ExportId, verified.ExportId);
        Assert.Contains(verified.Packages, package => package.Name == "Device");
        Assert.Contains(verified.Packages, package => package.Name == "Support");
        Assert.Contains(verified.Packages, package => package.Name == "OpenTAP");
        Assert.Contains(verified.Requirements, requirement => requirement.Name == "OpenTAP");
        Assert.DoesNotContain(verified.Packages, package => package.Name == "Unrelated");
        Assert.Equal(File.ReadAllBytes(archive), File.ReadAllBytes(Path.Combine(result.Directory, verified.Packages.Single(package => package.Name == "Device").Path)));
        var plan = Path.Combine(result.Directory, OfflineBenchBundle.ProgramDirectory("offline-device"), "offline-device.TapPlan");
        Assert.Equal("offline-device", BenchProgramHandoff.ValidateProgram(plan)!.PlanId);
        Assert.Contains("fresh", File.ReadAllText(Path.Combine(result.Directory, "README.txt")), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".authoring", "opentap")));
    }

    [Fact]
    public void Compiled_package_roots_are_required_even_when_not_declared_in_the_workspace_manifest()
    {
        var workspace = SavedProgram(); Archive("Device", "1.0.0");
        Assert.DoesNotContain(workspace.Manifest.Dependencies, dependency => dependency.Package == "OpenTAP");
        var result = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, Linux with { PackageDirectory = _archives });
        Assert.Contains(result.Manifest.Packages, package => package.Name == "OpenTAP");
        foreach (var path in Directory.EnumerateFiles(_archives, "OpenTAP*.TapPackage")) File.Delete(path);
        var prior = File.ReadAllBytes(Path.Combine(result.Directory, OfflineBenchBundle.ManifestFileName));
        Assert.Contains("OpenTAP", Assert.Throws<AuthoringWorkspaceException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, Linux with { PackageDirectory = _archives })).Message);
        Assert.Equal(prior, File.ReadAllBytes(Path.Combine(result.Directory, OfflineBenchBundle.ManifestFileName)));
    }

    [Fact]
    public void Manifest_and_compiled_package_constraints_are_both_enforced()
    {
        var workspace = SavedProgram(); Archive("Device", "1.0.0");
        workspace.Manifest.Dependencies.Add(new() { Package = "OpenTAP", Version = "^0.0.1" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        Assert.Throws<AuthoringWorkspaceException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, Linux with { PackageDirectory = _archives }));
        Assert.DoesNotContain(Directory.EnumerateDirectories(_output), directory => !Path.GetFileName(directory).StartsWith('.'));
    }

    [Fact]
    public void Installed_executable_modes_are_preserved_and_mode_only_releases_are_ambiguous()
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = SavedProgram();
        var home = AuthoringBuildSnapshotTests.Home(workspace with { Manifest = new AuthoringManifest() });
        var helper = Path.Combine(home.Root, "Packages", "Native helper", "helper");
        Directory.CreateDirectory(Path.GetDirectoryName(helper)!); File.WriteAllText(helper, "#!/bin/sh\nexit 0\n");
        var executableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(helper, executableMode);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(helper)!, "package.xml"), $"<Package Name=\"Native helper\" Version=\"1.0.0\" OS=\"{OfflineBenchBundle.CurrentOS}\" Architecture=\"{OfflineBenchBundle.CurrentArchitecture}\"><Files><File Path=\"Packages/Native helper/helper\"/></Files></Package>");
        var options = new OfflineBenchExportOptions(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture);
        var inventory = new OfflineBenchPackageInventory(_owned, options, default); inventory.AddInstalledHome(home.Root);
        var package = Assert.Single(new OfflineBenchPackageResolver(inventory, _owned, options, default).Resolve(Manifest(("Native helper", "^1.0.0"))));
        using (var zip = ZipFile.OpenRead(package.Archive))
            Assert.Equal((int)executableMode, zip.GetEntry("Packages/Native helper/helper")!.ExternalAttributes >> 16 & 0x1FF);
        var nonExecutable = Path.Combine(_archives, "non-executable.TapPackage"); File.Copy(package.Archive, nonExecutable);
        using (var zip = ZipFile.Open(nonExecutable, ZipArchiveMode.Update)) zip.GetEntry("Packages/Native helper/helper")!.ExternalAttributes = (0x8000 | 0x1A4) << 16;
        var other = Path.Combine(_archives, "executable.TapPackage"); File.Copy(package.Archive, other);
        inventory = new OfflineBenchPackageInventory(_owned, options, default); inventory.AddFolder(_archives, 0);
        Assert.Contains("Ambiguous", Assert.Throws<AuthoringWorkspaceException>(() => new OfflineBenchPackageResolver(inventory, _owned, options, default).Resolve(Manifest(("Native helper", "^1.0.0")))).Message);
    }

    [Fact]
    public void Genuine_installed_OpenTAP_release_exports_with_its_declared_tap_permission_without_running_actions()
    {
        if (!OperatingSystem.IsLinux()) return;
        var workspace = SavedProgram(includeEmbeddedArchives: false); Archive("Device", "1.0.0");
        var home = AuthoringBuildSnapshotTests.Home(workspace with { Manifest = new AuthoringManifest() });
        var metadataPath = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var metadata = File.ReadAllBytes(metadataPath);
        Assert.Contains("PackageActionExtensions", Encoding.UTF8.GetString(metadata));
        var tap = Path.Combine(home.Root, "tap");
        var nonExecutable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(tap, nonExecutable);
        var tapBytes = File.ReadAllBytes(tap);
        var options = new OfflineBenchExportOptions(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture,
            PackageDirectory: _archives, OpenTapHomeDirectory: home.Root);
        var result = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options);
        var engine = Assert.Single(result.Manifest.Packages, package => package.Name == "OpenTAP");
        using (var zip = ZipFile.OpenRead(Path.Combine(result.Directory, engine.Path)))
        {
            var entry = zip.GetEntry("tap"); Assert.NotNull(entry);
            Assert.Equal(0x49, entry.ExternalAttributes >> 16 & 0x49);
            using var input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes);
            Assert.Equal(tapBytes, bytes.ToArray());
        }
        Assert.Equal(metadata, File.ReadAllBytes(metadataPath));
        Assert.Equal(tapBytes, File.ReadAllBytes(tap));
        Assert.Equal(nonExecutable, File.GetUnixFileMode(tap));
        OfflineBenchBundle.Validate(result.Directory, options.TargetOS, options.TargetArchitecture);
    }

    [Theory]
    [InlineData("Windows", 0x1ED, 0x1A4)]
    [InlineData("Linux", 0, 0x1A4)]
    public void Equivalent_target_permission_modes_do_not_create_false_release_ambiguity(string os, int firstMode, int secondMode)
    {
        var first = Archive("Helper", "1.0.0", os: os, suffix: "first", payloadPath: "Packages/Helper/helper");
        var second = Archive("Helper", "1.0.0", os: os, suffix: "second", payloadPath: "Packages/Helper/helper");
        SetMode(first, firstMode); SetMode(second, secondMode);
        var options = new OfflineBenchExportOptions(os, "x64");
        var inventory = new OfflineBenchPackageInventory(_owned, options, default); inventory.AddFolder(_archives, 0);
        var result = new OfflineBenchPackageResolver(inventory, _owned, options, default).Resolve(Manifest(("Helper", "^1.0.0")));
        Assert.Equal("Helper", Assert.Single(result).Name);

        static void SetMode(string path, int mode)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            archive.GetEntry("Packages/Helper/helper")!.ExternalAttributes = (0x8000 | mode) << 16;
        }
    }

    [Fact]
    public void Cancellation_changed_owner_and_tampered_prior_bundle_preserve_prior_output()
    {
        var workspace = SavedProgram(); Archive("Device", "1.0.0");
        var options = Linux with { PackageDirectory = _archives };
        var first = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options);
        var manifest = Path.Combine(first.Directory, OfflineBenchBundle.ManifestFileName);
        var before = File.ReadAllBytes(manifest);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options, cancellationToken: canceled.Token));
        Assert.Throws<AuthoringWorkspaceException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options, () => false));
        Assert.Equal(before, File.ReadAllBytes(manifest));
        var unrelated = Path.Combine(first.Directory, "keep.txt"); File.WriteAllText(unrelated, "retain");
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options));
        Assert.Equal("retain", File.ReadAllText(unrelated)); Assert.Equal(before, File.ReadAllBytes(manifest));
    }

    [Fact]
    public void Only_intact_owned_bundles_are_replaced_and_input_changes_before_publication_preserve_them()
    {
        var workspace = SavedProgram(); var archive = Archive("Device", "1.0.0");
        var options = Linux with { PackageDirectory = _archives };
        var first = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options);
        var second = OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options);
        Assert.Equal(first.Directory, second.Directory);
        Assert.NotEqual(first.Manifest.ExportId, second.Manifest.ExportId);
        var path = Path.Combine(second.Directory, OfflineBenchBundle.ManifestFileName);
        var retained = File.ReadAllBytes(path);
        var changed = false;
        Assert.Throws<AuthoringWorkspaceException>(() => OfflineBenchBundleExporter.Export(workspace, "offline-device", _output, options, () =>
        {
            if (!changed && Directory.EnumerateDirectories(_output, ".offline-stage-*").Any(stage => Directory.Exists(Path.Combine(stage, "packages"))
                && Directory.EnumerateFiles(Path.Combine(stage, "packages")).Any()))
            {
                File.AppendAllText(archive, "changed after owned snapshot"); changed = true;
            }
            return true;
        }));
        Assert.True(changed);
        Assert.Equal(retained, File.ReadAllBytes(path));
        Assert.Equal(second.Manifest.ExportId, OfflineBenchBundle.Validate(second.Directory, "Linux", "x64").ExportId);
        Assert.DoesNotContain(Directory.EnumerateDirectories(_output), directory => Path.GetFileName(directory).StartsWith(".offline-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("MacOS", "arm64")]
    [InlineData("Windows", "x86")]
    public void Unsupported_targets_are_refused_before_any_publication(string os, string architecture)
    {
        Assert.Throws<AuthoringWorkspaceException>(() => OfflineBenchBundleExporter.Export(SavedProgram(), "offline-device", _output, new(os, architecture)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_output));
    }

    private IReadOnlyList<OfflineBenchCandidate> Resolve(AuthoringManifest manifest)
    {
        var inventory = new OfflineBenchPackageInventory(_owned, Linux, default); inventory.AddFolder(_archives, 0);
        return new OfflineBenchPackageResolver(inventory, _owned, Linux, default).Resolve(manifest);
    }

    private string Archive(string name, string version, string os = "Linux", string architecture = "x64", string payload = "payload",
        (string Name, string Range)[]? dependencies = null, string suffix = "", bool installAction = false, string? payloadPath = null)
    {
        var path = Path.Combine(_archives, name + version + suffix + ".TapPackage");
        var file = payloadPath ?? "Packages/" + name + "/payload.txt";
        var xml = new XElement("Package", new XAttribute("Name", name), new XAttribute("Version", version), new XAttribute("OS", os), new XAttribute("Architecture", architecture),
            new XElement("Dependencies", (dependencies ?? []).Select(dependency => new XElement("PackageDependency", new XAttribute("Package", dependency.Name), new XAttribute("Version", dependency.Range)))),
            new XElement("Files", new XElement("File", new XAttribute("Path", file))));
        if (installAction) xml.Add(new XElement("InstallAction", "run-something"));
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("package.xml", xml.ToString()); Write(file, payload);
        return path;
        void Write(string relative, string contents) { using var writer = new StreamWriter(zip.CreateEntry(relative).Open(), new UTF8Encoding(false)); writer.Write(contents); }
    }

    private static AuthoringManifest Manifest(params (string Name, string Range)[] requirements)
        => new() { Dependencies = requirements.Select(requirement => new AuthoringPackageDependency { Package = requirement.Name, Version = requirement.Range }).ToList() };

    private AuthoringWorkspace SavedProgram(bool includeEmbeddedArchives = true)
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "offline-device.TapPlan");
        var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create("offline-device"), AuthoringRecipeIds.Acquire);
        new PlanCompiler().Save(draft, path);
        // Synthetic data-only archives exercise closure and integrity only; the
        // joined runtime proof separately uses genuine published release payload.
        foreach (var dependency in XDocument.Load(path).Descendants().Where(element => includeEmbeddedArchives && element.Name.LocalName == "Package" && element.Parent?.Name.LocalName == "Package.Dependencies"))
        {
            var name = (string)dependency.Attribute("Name")!;
            var range = (string)dependency.Attribute("Version")!;
            var version = System.Text.RegularExpressions.Regex.Match(range, @"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.+-]+)?").Value;
            if (string.IsNullOrEmpty(version)) version = "1.0.0";
            if (!File.Exists(Path.Combine(_archives, name + version + ".TapPackage"))) Archive(name, version);
        }
        new AuthoringDocumentStore(root).Save(AuthoringDocumentDto.FromDraft(draft, revision: 1,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(path), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path))));
        var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.Dependencies = [new() { Package = "Device", Version = "^1.0.0" }];
        workspace.Manifest.OptionalDependencies = []; workspace.Manifest.PluginProjects = [];
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        return AuthoringWorkspaceLoader.Load(root);
    }

    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
}
