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

    [Fact]
    public void Custom_replacement_invalidates_bundled_provenance()
    {
        var home = Prepare(Workspace());
        File.Delete(Path.Combine(home.Root, "InstrumentComponents.dll")); // Force replacement rather than reuse.
        Prepare(Workspace(), path: PublishedLibraryFixture.Archive);
        var provenance = File.ReadAllText(Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "hardwaretest-provenance.json"));
        Assert.Equal("""{"source":"custom"}""", provenance);
        Assert.DoesNotContain(PublishedInstrumentComponents.Origin, provenance);
        Assert.DoesNotContain(PublishedInstrumentComponents.Sha256, provenance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Offline_same_name_replacement_publishes_custom_bytes_and_invalidates_bundled_provenance(bool unpacked)
    {
        var home = Prepare(Workspace());
        var before = Files(home);
        AssertBundledProvenance(home);
        var (path, replacement) = CustomReplacement(home, unpacked);

        var selected = new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, OfflinePackagePath = path, Offline = true });

        Assert.Equal(home.Root, selected.Root);
        var payload = unpacked
            ? Path.Combine(selected.Root, "Packages", PublishedInstrumentComponents.PackageName, "InstrumentComponents.dll")
            : Path.Combine(selected.Root, "InstrumentComponents.dll");
        Assert.Equal(replacement, File.ReadAllBytes(payload));
        Assert.NotEqual(before["InstrumentComponents.dll"], File.ReadAllBytes(payload));
        AssertCustomProvenance(selected);
    }

    [Fact]
    public void Unpacked_library_override_publishes_custom_bytes_and_invalidates_bundled_provenance()
    {
        var home = Prepare(Workspace());
        var before = Files(home);
        AssertBundledProvenance(home);
        var (path, replacement) = CustomReplacement(home, unpacked: true);
        File.Delete(Path.Combine(home.Root, "InstrumentComponents.dll")); // Force replacement rather than reuse.

        var selected = Prepare(Workspace(), path: path);

        Assert.Equal(home.Root, selected.Root);
        var payload = Path.Combine(selected.Root, "Packages", PublishedInstrumentComponents.PackageName, "InstrumentComponents.dll");
        Assert.Equal(replacement, File.ReadAllBytes(payload));
        Assert.NotEqual(before["InstrumentComponents.dll"], File.ReadAllBytes(payload));
        AssertCustomProvenance(selected);
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
