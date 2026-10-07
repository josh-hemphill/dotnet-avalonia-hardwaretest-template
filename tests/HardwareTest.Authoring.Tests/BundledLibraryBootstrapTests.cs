using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Same_name_custom_import_publishes_custom_bytes_and_invalidates_bundled_provenance(bool unpacked, bool buildMetadata)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var before = Files(home);
        AssertBundledProvenance(home);
        var (path, replacement) = CustomReplacement(home, unpacked, buildMetadata);
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
    public void Installed_duplicate_layout_is_rejected_without_changing_selected_bytes(bool import)
    {
        var home = Prepare(Workspace());
        File.Copy(Path.Combine(home.Root, "InstrumentComponents.dll"), Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "InstrumentComponents.dll"));
        var before = Files(home);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = import ? PublishedLibraryFixture.Archive : null }));
        Assert.StartsWith("Cannot inspect the selected standalone/TUI home: ", error.Message);
        Assert.Contains("Instrument Components execution cannot use obsolete package-directory library DLLs. Import or repair the selected package to keep library DLLs only in the installed home root.", error.Message);
        AssertFiles(before, home);
        Assert.False(AuthoringInstrumentCatalog.LibraryPayloadAvailability(home).Available);
    }

    [Theory]
    [InlineData("Other", "0.1.0")]
    [InlineData("Other", "0.1.1")]
    [InlineData("instrumentcomponents.opentap", "0.1.1")]
    public void Noncanonical_metadata_only_library_identity_cannot_grant_reuse(string alias, string canonicalVersion)
    {
        // Case-only directory aliases are separate physical directories on case-sensitive filesystems.
        if (OperatingSystem.IsWindows() && alias.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase)) return;
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var metadata = Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        xml.Root!.SetAttributeValue("Version", canonicalVersion);
        xml.Save(metadata);
        var aliasDirectory = Path.Combine(home.Root, "Packages", alias);
        Directory.CreateDirectory(aliasDirectory);
        File.WriteAllText(Path.Combine(aliasDirectory, "package.xml"),
            """<Package Name=" instrumentcomponents.opentap " Version="0.1.1" />""");
        var workspace = Workspace("^0.1.1");
        var before = Files(home);

        Assert.False(AuthoringInstrumentCatalog.LibraryPayloadAvailability(home).Available);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(workspace));

        Assert.StartsWith("Cannot inspect the selected standalone/TUI home: ", error.Message);
        Assert.Contains("Instrument Components execution cannot use noncanonical or duplicate installed package identities.", error.Message);
        AssertFiles(before, home);
        AssertBundledProvenance(home);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Home_root_library_identity_is_rejected_without_changing_selected_bytes(bool declared)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        File.WriteAllText(Path.Combine(home.Root, "package.xml"),
            $"""<Package Name="{PublishedInstrumentComponents.PackageName}" Version="{PublishedInstrumentComponents.Version}" />""");
        if (declared)
        {
            var metadata = Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
            var xml = System.Xml.Linq.XDocument.Load(metadata);
            xml.Root!.Elements().Single(element => element.Name.LocalName == "Files")
                .Add(new System.Xml.Linq.XElement("File", new System.Xml.Linq.XAttribute("Path", "package.xml")));
            xml.Save(metadata);
        }
        var before = Files(home);

        Assert.False(AuthoringInstrumentCatalog.LibraryPayloadAvailability(home).Available);
        Assert.Throws<IOException>(() => AuthoringAdapterPayloadInspection.LibraryPayloadPath(home, "InstrumentComponents.dll"));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace()));

        Assert.StartsWith("Cannot inspect the selected standalone/TUI home: ", error.Message);
        Assert.Contains("Instrument Components execution requires an installed home root with root DLLs and Packages/InstrumentComponents.OpenTap/package.xml. Import package directories before execution.", error.Message);
        AssertFiles(before, home);
        AssertBundledProvenance(home);
    }

    [Theory]
    [InlineData("duplicate-files")]
    [InlineData("stray-file")]
    [InlineData("nested-files")]
    [InlineData("wrapped-hash")]
    [InlineData("hash-nested-content")]
    [InlineData("declared-missing")]
    [InlineData("declared-bad-hash")]
    [InlineData("old-package-version")]
    [InlineData("old-contract-version")]
    [InlineData("old-adapter-version")]
    public void Invalid_installed_library_metadata_is_unavailable_and_failed_prepare_preserves_bytes(string invalid)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var metadata = Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        var declaration = new System.Xml.Linq.XElement("File", new System.Xml.Linq.XAttribute("Path", "extra.txt"),
            new System.Xml.Linq.XElement("Hash", "invalid"));
        if (invalid == "duplicate-files") xml.Root!.Add(new System.Xml.Linq.XElement("Files"));
        else if (invalid == "stray-file") xml.Root!.Add(new System.Xml.Linq.XElement("Wrapper", declaration));
        else if (invalid == "nested-files") xml.Root!.Add(new System.Xml.Linq.XElement("Wrapper", new System.Xml.Linq.XElement("Files")));
        else if (invalid == "wrapped-hash")
        {
            var file = xml.Descendants().First(element => element.Name.LocalName == "File");
            foreach (var hash in file.Elements().Where(element => element.Name.LocalName == "Hash").ToArray()) hash.Remove();
            file.Add(new System.Xml.Linq.XElement("Wrapper", new System.Xml.Linq.XElement("Hash", "invalid")));
        }
        else if (invalid == "hash-nested-content")
        {
            var file = xml.Descendants().First(element => element.Name.LocalName == "File");
            var expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(
                Path.Combine(home.Root, (string)file.Attribute("Path")!))));
            foreach (var hash in file.Elements().Where(element => element.Name.LocalName == "Hash").ToArray()) hash.Remove();
            file.Add(new System.Xml.Linq.XElement("Hash", new System.Xml.Linq.XElement("Wrapper", expected)));
        }
        else if (invalid == "old-package-version") xml.Root!.SetAttributeValue("Version", "0.1.0");
        else if (invalid is "old-contract-version" or "old-adapter-version")
        {
            var file = invalid == "old-contract-version" ? "InstrumentComponents.dll" : "InstrumentComponents.OpenTap.dll";
            SetOldAssemblyVersion(Path.Combine(home.Root, file));
            var entry = xml.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == file);
            foreach (var hash in entry.Elements().Where(element => element.Name.LocalName == "Hash").ToArray()) hash.Remove();
            entry.Add(new System.Xml.Linq.XElement("Hash", Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(Path.Combine(home.Root, file))))));
        }
        else
        {
            xml.Root!.Elements().Single(element => element.Name.LocalName == "Files").Add(declaration);
            if (invalid == "declared-bad-hash") File.WriteAllText(Path.Combine(home.Root, "extra.txt"), "selected extra payload");
        }
        xml.Save(metadata);
        var before = Files(home);

        Assert.False(AuthoringInstrumentCatalog.LibraryPayloadAvailability(home).Available);
        Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace(), path: "missing.TapPackage"));
        AssertFiles(before, home);
        AssertBundledProvenance(home);

        if (invalid.StartsWith("old-", StringComparison.Ordinal)) return;
        var repaired = Prepare(Workspace());
        Assert.True(AuthoringInstrumentCatalog.LibraryPayloadAvailability(repaired).Available);
        Assert.Equal(before["unrelated.txt"], File.ReadAllBytes(Path.Combine(repaired.Root, "unrelated.txt")));
        AssertBundledProvenance(repaired);
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
    [InlineData(false, "hash-nested-content")]
    [InlineData(true, "hash-nested-content")]
    [InlineData(false, "old-package-version")]
    [InlineData(true, "old-package-version")]
    [InlineData(false, "old-contract-version")]
    [InlineData(true, "old-contract-version")]
    [InlineData(false, "old-adapter-version")]
    [InlineData(true, "old-adapter-version")]
    [InlineData(false, "root-metadata-declaration")]
    [InlineData(true, "root-metadata-declaration")]
    public void Incomplete_or_invalid_custom_library_preserves_selected_home(bool unpacked, string invalid)
    {
        var home = Prepare(Workspace());
        File.WriteAllText(Path.Combine(home.Root, "unrelated.txt"), "selected home sentinel");
        var before = Files(home);
        var (path, _) = CustomReplacement(home, unpacked: true);
        var metadata = Path.Combine(path, "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        if (invalid == "old-package-version") xml.Root!.SetAttributeValue("Version", "0.1.0");
        if (invalid is "old-contract-version" or "old-adapter-version")
            SetOldAssemblyVersion(Path.Combine(path, invalid == "old-contract-version" ? "InstrumentComponents.dll" : "InstrumentComponents.OpenTap.dll"));
        if (invalid == "root-metadata-declaration")
            xml.Root!.Element("Files")!.Add(new System.Xml.Linq.XElement("File", new System.Xml.Linq.XAttribute("Path", "package.xml")));
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
        if (invalid == "hash-nested-content")
        {
            var expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(Path.Combine(path, "InstrumentComponents.dll"))));
            xml.Descendants("File").First().Add(new System.Xml.Linq.XElement("Hash", new System.Xml.Linq.XElement("Wrapper", expected)));
        }
        else if (invalid.StartsWith("hash-", StringComparison.Ordinal))
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

    private static void SetOldAssemblyVersion(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        Assert.Equal(new Version(0, 1, 1, 0), metadata.GetAssemblyDefinition().Version);
        var assemblyRow = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.Assembly);
        // Assembly table stores HashAlgId, then Major/Minor/Build/Revision as UInt16 fields.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(assemblyRow + 8, 2), 0);
        File.WriteAllBytes(path, bytes);
    }

    private (string Path, byte[] Payload) CustomReplacement(OpenTapHome home, bool unpacked, bool buildMetadata = false)
    {
        var folder = Path.Combine(root, "custom-library");
        Directory.CreateDirectory(folder);
        // Retain genuine managed assemblies while making the incoming library bytes distinguishable.
        var replacement = File.ReadAllBytes(Path.Combine(home.Root, "InstrumentComponents.dll")).Concat(new byte[] { 0 }).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "InstrumentComponents.dll"), replacement);
        File.Copy(Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll"), Path.Combine(folder, "InstrumentComponents.OpenTap.dll"));
        var metadata = unpacked ? folder : Path.Combine(folder, "Packages", PublishedInstrumentComponents.PackageName);
        Directory.CreateDirectory(metadata);
        var version = PublishedInstrumentComponents.Version + (buildMetadata ? "+custom.1" : "");
        File.WriteAllText(Path.Combine(metadata, "package.xml"), $"""<Package Name="{PublishedInstrumentComponents.PackageName}" Version="{version}"><Files><File Path="InstrumentComponents.dll" /><File Path="InstrumentComponents.OpenTap.dll" /></Files></Package>""");
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
