using System.IO.Compression;
using System.Text;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class BundledLibraryBootstrapTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ht-bundled-tests-" + Guid.NewGuid().ToString("N"));
    private AuthoringWorkspace Workspace(string version = "^0.1.0") => new(root, new AuthoringManifest
    { Dependencies = [new() { Package = PublishedInstrumentComponents.PackageName, Version = version }] }, []);
    private OpenTapHome Prepare(AuthoringWorkspace workspace, string name = "home", string? path = null) => new OpenTapHomeBootstrapper().Bootstrap(workspace,
        new() { HomeDirectory = Path.Combine(root, name), Offline = true, InstrumentComponentsPackagePath = path });

    [Fact]
    public void Incompatible_bundled_requirement_and_invalid_explicit_path_preserve_selected_bytes()
    {
        Directory.CreateDirectory(Path.Combine(root, "home"));
        var marker = Path.Combine(root, "home", "selected.txt"); File.WriteAllText(marker, "selected");
        Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace("^2.0.0")));
        Assert.Equal("selected", File.ReadAllText(marker));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(marker)!, "*", SearchOption.AllDirectories));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace(), path: "missing.TapPackage"));
        Assert.Contains("not found", error.Message);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(marker)!, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Options_manifest_environment_overrides_retain_priority_without_fallback()
    {
        var previous = Environment.GetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE");
        try
        {
            Environment.SetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE", "missing-env.TapPackage");
            var workspace = Workspace(); workspace.Manifest.InstrumentComponentsPackage = PublishedLibraryFixture.Archive;
            var fromManifest = Prepare(workspace, "manifest");
            Assert.True(File.Exists(Path.Combine(fromManifest.Root, "InstrumentComponents.dll")));
            Assert.Throws<AuthoringWorkspaceException>(() => Prepare(workspace, "options", "missing-options.TapPackage"));
            workspace.Manifest.InstrumentComponentsPackage = "missing-manifest.TapPackage";
            var fromOptions = Prepare(workspace, "options-valid", PublishedLibraryFixture.Archive);
            Assert.True(File.Exists(Path.Combine(fromOptions.Root, "InstrumentComponents.dll")));
            workspace.Manifest.InstrumentComponentsPackage = null;
            Assert.Throws<AuthoringWorkspaceException>(() => Prepare(workspace, "environment-invalid"));
            Environment.SetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE", PublishedLibraryFixture.Archive);
            Assert.True(File.Exists(Path.Combine(Prepare(workspace, "environment-valid").Root, "InstrumentComponents.dll")));
        }
        finally { Environment.SetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE", previous); }
    }

    [Fact]
    public void Compatible_installed_home_reuses_bytes_even_with_invalid_override()
    {
        var home = Prepare(Workspace());
        var before = Files(home);
        Prepare(Workspace(), path: "missing.TapPackage");
        AssertFiles(before, home);
        var captured = AuthoringBuildService.CaptureTree(home.Root, "home", true);
        var provenance = Assert.Single(captured.Files, file => file.RelativePath.EndsWith("hardwaretest-provenance.json", StringComparison.Ordinal));
        Assert.Equal(before[provenance.RelativePath], provenance.Bytes);
        Assert.Equal(AuthoringBuildService.Hash(provenance.Bytes), provenance.Hash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Offline_same_name_replacement_publishes_custom_bytes_and_invalidates_bundled_provenance(bool unpacked)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var before = Files(home);
        AssertBundledProvenance(home);
        var (path, replacement) = CustomReplacement(home, unpacked);

        var selected = new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, OfflinePackagePath = path, Offline = true });

        Assert.Equal(home.Root, selected.Root);
        AssertLibraryReplacement(before, selected, replacement);
        AssertCustomProvenance(selected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Same_name_custom_import_publishes_custom_bytes_and_invalidates_bundled_provenance(bool unpacked)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var before = Files(home);
        AssertBundledProvenance(home);
        var (path, replacement) = CustomReplacement(home, unpacked);
        var selected = new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, OfflinePackagePath = path, Offline = true });

        Assert.Equal(home.Root, selected.Root);
        AssertLibraryReplacement(before, selected, replacement);
        AssertCustomProvenance(selected);
    }

    [Fact]
    public void Same_name_archive_with_conflicting_declared_library_aliases_preserves_selected_bytes()
    {
        var home = Prepare(Workspace());
        var before = Files(home);
        var (path, replacement) = CustomReplacement(home, unpacked: false);
        var alias = $"Packages/{PublishedInstrumentComponents.PackageName}/InstrumentComponents.dll";
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var metadata = archive.GetEntry($"Packages/{PublishedInstrumentComponents.PackageName}/package.xml")!;
            System.Xml.Linq.XDocument xml;
            using (var input = metadata.Open()) xml = System.Xml.Linq.XDocument.Load(input);
            metadata.Delete();
            xml.Root!.Element("Files")!.Add(new System.Xml.Linq.XElement("File", new System.Xml.Linq.XAttribute("Path", alias)));
            using (var output = archive.CreateEntry($"Packages/{PublishedInstrumentComponents.PackageName}/package.xml").Open()) xml.Save(output);
            using var incoming = archive.CreateEntry(alias).Open();
            incoming.Write(replacement);
            incoming.WriteByte(1);
        }

        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, OfflinePackagePath = path, Offline = true }));

        Assert.Contains("alternate DLL layout", error.Message);
        AssertFiles(before, home);
        AssertBundledProvenance(home);
    }

    [Theory]
    [InlineData("InstrumentComponents.dll")]
    [InlineData("InstrumentComponents.OpenTap.dll")]
    public void Differently_named_rooted_package_cannot_replace_bundled_payload_or_attestation(string payload)
    {
        var home = Prepare(Workspace());
        var before = Files(home);
        var packagePath = Path.Combine(root, "Foreign.TapPackage");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            using (var metadata = archive.CreateEntry("Packages/Foreign/package.xml").Open())
                metadata.Write(Encoding.UTF8.GetBytes($"""<Package Name="Foreign" Version="1.0.0"><Files><File Path="{payload}" /></Files></Package>"""));
            using var incoming = archive.CreateEntry(payload).Open();
            incoming.Write(before[payload]);
            incoming.WriteByte(0); // Genuine published DLL bytes with a distinguishable replacement.
        }
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, OfflinePackagePath = packagePath, Offline = true }));
        Assert.Contains("Only an InstrumentComponents.OpenTap package", error.Message);
        AssertFiles(before, home);
        var provenance = File.ReadAllText(Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "hardwaretest-provenance.json"));
        Assert.Contains(PublishedInstrumentComponents.Origin, provenance);
        Assert.Contains(PublishedInstrumentComponents.Sha256, provenance);
        Assert.Equal(before[payload], File.ReadAllBytes(Path.Combine(home.Root, payload)));
    }

    [Fact]
    public void Deleted_first_home_does_not_break_second_home_generic_lifecycle_metadata()
    {
        var first = Prepare(Workspace(), "first");
        Assert.Equal(8, AuthoringInstrumentCatalog.Discover(first).Count);
        var second = Prepare(Workspace(), "second");
        Directory.Delete(first.Root, true);
        var adapters = AuthoringInstrumentCatalog.Discover(second);
        Assert.Equal(8, adapters.Count);
        foreach (var adapter in adapters)
        {
            var binding = new InstrumentRef("Device", adapter.TypeId, "TCPIP0::192.0.2.8::inst0::INSTR");
            var draft = new AuthoringPlanInitializer().Construct(new("lifecycle")
            { Home = second, Instruments = [binding], IdentityInstrumentSlot = "Device", IncludeSafeShutdown = true }).Draft;
            var path = Path.Combine(root, "lifecycle.TapPlan");
            var compiler = new PlanCompiler(selectedHome: second); compiler.Save(draft, path);
            Assert.Equal(adapter.TypeId, Assert.Single(compiler.Load(path).Instruments).TypeId);
            var xml = File.ReadAllText(path);
            Assert.Contains("InstrumentComponents.OpenTap.IdentityQueryStep", xml);
            Assert.Contains("InstrumentComponents.OpenTap.SafeShutdownStep", xml);
        }
        Assert.DoesNotContain(second.Root, global::OpenTap.PluginManager.DirectoriesToSearch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Installed_duplicate_layout_requires_fresh_home_without_changing_selected_bytes(bool import)
    {
        var home = Prepare(Workspace());
        File.Copy(Path.Combine(home.Root, "InstrumentComponents.dll"), Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "InstrumentComponents.dll"));
        var before = Files(home);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = import ? PublishedLibraryFixture.Archive : null }));
        Assert.Contains("fresh home", error.Message);
        AssertFiles(before, home);
        Assert.False(AuthoringInstrumentCatalog.LibraryPayloadAvailability(home).Available);
    }

    [Theory]
    [InlineData(false, "metadata-only")]
    [InlineData(true, "metadata-only")]
    [InlineData(false, "undeclared")]
    [InlineData(true, "undeclared")]
    [InlineData(false, "direct-files")]
    [InlineData(true, "direct-files")]
    [InlineData(false, "nested-files")]
    [InlineData(true, "nested-files")]
    [InlineData(false, "duplicate-files")]
    [InlineData(true, "duplicate-files")]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "native")]
    [InlineData(true, "native")]
    [InlineData(false, "identity")]
    [InlineData(true, "identity")]
    [InlineData(false, "hash-malformed")]
    [InlineData(true, "hash-malformed")]
    [InlineData(false, "hash-mismatch")]
    [InlineData(true, "hash-mismatch")]
    public void Incomplete_or_invalid_custom_library_preserves_selected_home(bool unpacked, string invalid)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var before = Files(home);
        var (path, _) = CustomReplacement(home, unpacked: true);
        var metadata = Path.Combine(path, "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        if (invalid == "metadata-only") xml.Root!.Element("Files")!.Remove();
        if (invalid == "undeclared") xml.Descendants("File").First().Remove();
        if (invalid == "direct-files")
        {
            var declarations = xml.Root!.Element("Files")!;
            declarations.ReplaceWith(declarations.Elements().ToArray());
        }
        if (invalid == "nested-files")
        {
            var declarations = xml.Root!.Element("Files")!;
            declarations.Remove();
            xml.Root.Add(new System.Xml.Linq.XElement("Wrapper", declarations));
        }
        if (invalid == "duplicate-files") xml.Root!.Add(new System.Xml.Linq.XElement("Files"));
        if (invalid == "missing") File.Delete(Path.Combine(path, "InstrumentComponents.dll"));
        if (invalid == "native") File.WriteAllText(Path.Combine(path, "InstrumentComponents.dll"), "not a managed PE image");
        if (invalid == "identity") File.Copy(Path.Combine(path, "InstrumentComponents.OpenTap.dll"), Path.Combine(path, "InstrumentComponents.dll"), overwrite: true);
        if (invalid.StartsWith("hash-", StringComparison.Ordinal))
            xml.Descendants("File").First().Add(new System.Xml.Linq.XElement("Hash", invalid == "hash-malformed" ? new string('z', 40) : new string('0', 40)));
        xml.Save(metadata);
        if (!unpacked)
        {
            var archive = Path.Combine(root, "invalid-library.TapPackage");
            ZipFile.CreateFromDirectory(path, archive);
            path = archive;
        }
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = path }));
        AssertFiles(before, home);
        AssertBundledProvenance(home);
    }

    [Theory]
    [InlineData("Foreign", "1.0.0", false)]
    [InlineData("InstrumentComponents.OpenTap", "2.0.0", false)]
    [InlineData("Foreign", "1.0.0", true)]
    [InlineData("InstrumentComponents.OpenTap", "2.0.0", true)]
    public void Flat_folder_payload_cannot_overwrite_validated_library_identity(string name, string version, bool differingCase)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var before = Files(home);
        var (folder, _) = CustomReplacement(home, unpacked: true);
        var nested = $"Packages/{PublishedInstrumentComponents.PackageName}/package.xml";
        if (differingCase) nested = nested.ToUpperInvariant();
        var destination = Path.Combine(folder, nested);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, $"""<Package Name="{name}" Version="{version}" />""");
        var metadata = Path.Combine(folder, "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        xml.Root!.Element("Files")!.Add(new System.Xml.Linq.XElement("File", new System.Xml.Linq.XAttribute("Path", nested)));
        xml.Save(metadata);

        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = folder }));

        Assert.Contains("validated package metadata", error.Message);
        AssertFiles(before, home);
        AssertBundledProvenance(home);
    }

    private (string Path, byte[] Payload) CustomReplacement(OpenTapHome home, bool unpacked)
    {
        var folder = Path.Combine(root, "custom-library");
        Directory.CreateDirectory(folder);
        // Retain genuine managed assemblies while making the incoming library bytes distinguishable.
        var replacement = File.ReadAllBytes(Path.Combine(home.Root, "InstrumentComponents.dll")).Concat(new byte[] { 0 }).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "InstrumentComponents.dll"), replacement);
        File.Copy(Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll"), Path.Combine(folder, "InstrumentComponents.OpenTap.dll"));
        var metadata = unpacked ? folder : Path.Combine(folder, "Packages", PublishedInstrumentComponents.PackageName);
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "package.xml"), $"""<Package Name="{PublishedInstrumentComponents.PackageName}" Version="{PublishedInstrumentComponents.Version}"><Files><File Path="InstrumentComponents.dll" /><File Path="InstrumentComponents.OpenTap.dll" /></Files></Package>""");
        if (unpacked) return (folder, replacement);
        var archive = Path.Combine(root, "custom-library.TapPackage");
        ZipFile.CreateFromDirectory(folder, archive);
        return (archive, replacement);
    }

    private static void AssertLibraryReplacement(Dictionary<string, byte[]> before, OpenTapHome home, byte[] replacement)
    {
        Assert.NotEqual(before["InstrumentComponents.dll"], replacement);
        Assert.Equal(replacement, File.ReadAllBytes(Path.Combine(home.Root, "InstrumentComponents.dll")));
        Assert.Equal(before["InstrumentComponents.OpenTap.dll"], File.ReadAllBytes(Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll")));
        foreach (var file in AuthoringAdapterPayloadInspection.LibraryFiles)
            Assert.False(File.Exists(Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, file)));
        var availability = AuthoringInstrumentCatalog.LibraryPayloadAvailability(home);
        Assert.True(availability.Available, availability.Reason);
        Assert.Equal(before["unrelated.txt"], File.ReadAllBytes(Path.Combine(home.Root, "unrelated.txt")));
    }

    private static string Provenance(OpenTapHome home) => File.ReadAllText(Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "hardwaretest-provenance.json"));
    private static void AssertBundledProvenance(OpenTapHome home)
    {
        var provenance = Provenance(home);
        Assert.Contains(PublishedInstrumentComponents.Origin, provenance);
        Assert.Contains(PublishedInstrumentComponents.Sha256, provenance);
    }
    private static void AssertCustomProvenance(OpenTapHome home)
    {
        var provenance = Provenance(home);
        Assert.Equal("""{"source":"custom"}""", provenance);
        Assert.DoesNotContain(PublishedInstrumentComponents.Origin, provenance);
        Assert.DoesNotContain(PublishedInstrumentComponents.Sha256, provenance);
    }
    private static Dictionary<string, byte[]> Files(OpenTapHome home) => Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
    private static void AssertFiles(Dictionary<string, byte[]> before, OpenTapHome home)
    {
        var after = Files(home); Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
