using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class StandaloneVisaBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-counterpart-bootstrap-" + Guid.NewGuid().ToString("N"));
    private AuthoringWorkspace Workspace(bool library = true) => new(_root, new AuthoringManifest
    { Dependencies = library ? [new() { Package = PublishedInstrumentComponents.PackageName, Version = PublishedInstrumentComponents.Version }] : [] }, []);
    private OpenTapHome Prepare(AuthoringWorkspace workspace, string? overridePath = null) => new OpenTapHomeBootstrapper().Bootstrap(workspace,
        new() { HomeDirectory = Path.Combine(_root, "home"), Offline = true, InstrumentComponentsPackagePath = overridePath });

    [Fact]
    public void Declared_library_prepares_counterpart_and_repairs_substituted_provider_without_loading_it()
    {
        var home = Prepare(Workspace());
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap.Visa");
        var provider = Path.Combine(home.Root, "InstrumentComponents.OpenTap.Visa.dll");
        var expected = File.ReadAllBytes(provider);
        File.WriteAllText(provider, "substituted");
        Assert.False(StandaloneVisaReadiness.Assess(home).Available);
        Prepare(Workspace());
        Assert.Equal(expected, File.ReadAllBytes(provider));
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
    }

    [Fact]
    public void Unsupported_selected_dependency_versions_fail_preparation_and_preserve_home()
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("Version=\"0.1.1\"", "Version=\"0.1.0\"", StringComparison.Ordinal));
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace(), "missing-override.TapPackage"));
        Assert.Contains("unsupported", error.Message);
        Assert.Contains("unsupported", StandaloneVisaReadiness.Assess(home).Reason);
        Assert.False(AuthoringEnvironmentAssessment.Packages(Workspace().Manifest, home).Single().Satisfied);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_opentap_version_fails_counterpart_preparation_transactionally(bool explicitPartialImport)
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var text = File.ReadAllText(metadata);
        var document = System.Xml.Linq.XDocument.Parse(text);
        document.Root!.SetAttributeValue("Version", "9.34.0");
        File.WriteAllText(metadata, document.ToString());
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        string? archive = null;
        if (explicitPartialImport)
        {
            archive = Path.Combine(_root, "unrelated.TapPackage");
            using var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open()))
                writer.Write("<Package Name=\"Unrelated\" Version=\"1.0.0\"><Files><File Path=\"payload.txt\"/></Files></Package>");
            using (var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open())) writer.Write("unrelated payload");
        }
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive }));
        Assert.Contains("unsupported", error.Message);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Theory]
    [InlineData("9.34.0", "9.35.0")]
    [InlineData("9.35.0", "9.34.0")]
    [InlineData("9.35.0", "9.35.0")]
    public void Duplicate_opentap_identity_cannot_spoof_current_dependency_readiness_or_preparation(string canonicalVersion, string aliasVersion)
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var document = System.Xml.Linq.XDocument.Load(metadata);
        document.Root!.SetAttributeValue("Version", canonicalVersion);
        document.Save(metadata);
        var alias = Path.Combine(home.Root, "Packages", "OtherOpenTap");
        Directory.CreateDirectory(alias);
        File.WriteAllText(Path.Combine(alias, "package.xml"), $"<Package Name=\"OpenTAP\" Version=\"{aliasVersion}\"><Files/></Package>");
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        Assert.False(StandaloneVisaReadiness.Assess(home).Available);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace()));
        Assert.Contains("unsupported", error.Message);
        var after = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(home.Root, path));
        Assert.Equal(before.Keys.Order(), after.Order());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Theory]
    [InlineData("InstrumentComponents.OpenTap", "99.0.0")]
    [InlineData("OpenTAP", "9.34.0")]
    public void Explicit_unrelated_import_respects_declared_selected_library_and_engine_requirements(string package, string version)
    {
        var home = Prepare(Workspace());
        var workspace = Workspace();
        if (package == PublishedInstrumentComponents.PackageName) workspace.Manifest.Dependencies.Single().Version = version;
        else workspace.Manifest.Dependencies.Add(new() { Package = package, Version = version });
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        var archive = Path.Combine(_root, "unrelated.TapPackage");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open()))
                writer.Write("<Package Name=\"Unrelated\" Version=\"1.0.0\"><Files><File Path=\"payload.txt\"/></Files></Package>");
            using (var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open())) writer.Write("unrelated payload");
        }
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace,
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive }));
        Assert.Contains("version mismatch", error.Message);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(home.Root, path)).Order());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Theory]
    [InlineData("OpenTap.dll", false)]
    [InlineData("tap.dll", false)]
    [InlineData("OpenTap.dll", true)]
    [InlineData("tap.dll", true)]
    [InlineData("config-json", false)]
    [InlineData("config-framework", false)]
    [InlineData("hash-mismatch", false)]
    [InlineData("hash-malformed", false)]
    [InlineData("hash-nested", false)]
    public void Unsupported_or_corrupt_selected_runtime_is_unavailable_and_prepare_preserves_bytes(string invalid, bool olderAssembly)
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        if (invalid.EndsWith(".dll", StringComparison.Ordinal))
        {
            var path = Path.Combine(home.Root, invalid);
            if (olderAssembly)
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes, writable: false);
                using var pe = new PEReader(stream);
                var reader = pe.GetMetadataReader();
                var versionFields = pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.Assembly) + 4;
                var field = Enumerable.Range(0, 4).First(index => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(versionFields + index * 2, 2)) > 0);
                var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(versionFields + field * 2, 2));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(versionFields + field * 2, 2), (ushort)(value - 1));
                File.WriteAllBytes(path, bytes);
            }
            else File.WriteAllText(path, "corrupted runtime assembly");
            var entry = xml.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == invalid);
            entry.Elements().Single(element => element.Name.LocalName == "Hash").Value = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path)));
            xml.Save(metadata);
        }
        else if (invalid.StartsWith("config-", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(home.Root, "tap.runtimeconfig.json"), invalid == "config-json" ? "{" : """{"runtimeOptions":{"framework":{"name":"Unsupported.Framework","version":"99.0.0"}}}""");
        else
        {
            var hash = xml.Descendants().First(element => element.Name.LocalName == "File")
                .Elements().Single(element => element.Name.LocalName == "Hash");
            if (invalid == "hash-nested") hash.ReplaceNodes(new System.Xml.Linq.XElement("Wrapper", hash.Value));
            else hash.Value = invalid == "hash-malformed" ? new string('z', 40) : new string('0', 40);
            xml.Save(metadata);
        }
        var before = Snapshot(home);
        Assert.False(StandaloneVisaReadiness.Assess(home).Available);
        Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace()));
        AssertSnapshot(before, home);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_library_home_checks_manifest_engine_requirement_even_without_library_declaration(bool selectedLibrary)
    {
        var home = Prepare(Workspace(library: selectedLibrary));
        var workspace = Workspace(library: false);
        workspace.Manifest.Dependencies.Add(new() { Package = "OpenTAP", Version = "99.0.0" });
        var archive = Path.Combine(_root, "unrelated.TapPackage");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("package.xml").Open());
            writer.Write("""<Package Name="Unrelated" Version="1.0.0"><Files /></Package>""");
        }
        var before = Snapshot(home);
        if (selectedLibrary)
        {
            var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace,
                new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive }));
            Assert.Contains("version mismatch", error.Message);
            AssertSnapshot(before, home);
        }
        else
        {
            new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive });
            Assert.Contains(OpenTapHomeBootstrapper.ListInstalledPackages(home), package => package.Name == "Unrelated");
            Assert.False(StandaloneVisaReadiness.RequiresStandaloneReadiness(home));
        }
    }

    [Theory]
    [InlineData("tap.dll", false)]
    [InlineData("tap.dll", true)]
    [InlineData("launcher", false)]
    [InlineData("launcher", true)]
    [InlineData("Dependencies/Newtonsoft.Json.13.0.0.0/Newtonsoft.Json.dll", false)]
    [InlineData("Dependencies/Newtonsoft.Json.13.0.0.0/Newtonsoft.Json.dll", true)]
    public void Missing_declared_runtime_payload_is_repaired_before_home_is_considered_complete(string relative, bool library)
    {
        var workspace = Workspace(library);
        var home = Prepare(workspace);
        if (relative == "launcher") relative = OperatingSystem.IsWindows() ? "tap.exe" : "tap";
        var missing = Path.Combine(home.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(missing));
        var expected = File.ReadAllBytes(missing);
        File.Delete(missing);
        Assert.NotNull(StandaloneVisaReadiness.OpenTapRuntimeIssue(home));
        if (library) Assert.False(StandaloneVisaReadiness.Assess(home).Available);

        Prepare(workspace);

        Assert.Equal(expected, File.ReadAllBytes(missing));
        Assert.Null(StandaloneVisaReadiness.OpenTapRuntimeIssue(home));
        if (library) Assert.True(StandaloneVisaReadiness.Assess(home).Available);
    }

    [Theory]
    [InlineData("Packages/OpenTAP/OpenTap.Plugins.BasicSteps.dll")]
    [InlineData("Dependencies/Newtonsoft.Json.13.0.0.0/Newtonsoft.Json.dll")]
    public void Current_library_preparation_repairs_declared_runtime_payload_without_rewriting_metadata(string relative)
    {
        var workspace = Workspace();
        var home = Prepare(workspace);
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        xml.Root!.Add(new System.Xml.Linq.XComment("Preserve installed runtime metadata during repair."));
        xml.Save(metadata);
        var metadataBytes = File.ReadAllBytes(metadata);
        var missing = Path.Combine(home.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        var expected = File.ReadAllBytes(missing);
        File.Delete(missing);
        Assert.NotNull(StandaloneVisaReadiness.OpenTapRuntimeIssue(home));

        Prepare(workspace);

        Assert.Equal(expected, File.ReadAllBytes(missing));
        Assert.Equal(metadataBytes, File.ReadAllBytes(metadata));
        Assert.Null(StandaloneVisaReadiness.OpenTapRuntimeIssue(home));
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direct_bootstrap_rejects_unrepairable_missing_declarations_before_copying_runtime(bool wrongHash)
    {
        var workspace = Workspace();
        var home = Prepare(workspace);
        const string relative = "Dependencies/Newtonsoft.Json.13.0.0.0/Newtonsoft.Json.dll";
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        if (wrongHash)
        {
            var declaration = xml.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == relative);
            foreach (var hash in declaration.Elements().Where(element => element.Name.LocalName == "Hash").ToArray()) hash.Remove();
            declaration.Add(new System.Xml.Linq.XElement(declaration.Name.Namespace + "Hash", new string('0', 40)));
        }
        else xml.Root!.Add(new System.Xml.Linq.XElement("Files", new System.Xml.Linq.XElement("File",
            new System.Xml.Linq.XAttribute("Path", "missing-required-runtime.bin"))));
        xml.Save(metadata);
        File.Delete(Path.Combine(home.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var before = Snapshot(home);

        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace,
            new() { HomeDirectory = home.Root, Offline = true }));

        Assert.Contains("cannot be repaired", error.Message);
        AssertSnapshot(before, home);
        // The same preflight also protects direct owned preparation before any runtime copy.
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().BootstrapOwned(workspace,
            new() { HomeDirectory = home.Root, Offline = true }));
        AssertSnapshot(before, home);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_only_runtime_repair_preserves_valid_present_payloads_with_optional_or_updated_hash(bool updateHash)
    {
        var workspace = Workspace();
        workspace.Manifest.SchemaVersion = 2;
        var home = Prepare(workspace);
        const string customRelative = "Packages/OpenTAP/OpenTap.Plugins.BasicSteps.dll";
        const string missingRelative = "Dependencies/Newtonsoft.Json.13.0.0.0/Newtonsoft.Json.dll";
        var custom = Path.Combine(home.Root, customRelative.Replace('/', Path.DirectorySeparatorChar));
        var missing = Path.Combine(home.Root, missingRelative.Replace('/', Path.DirectorySeparatorChar));
        var expectedMissing = File.ReadAllBytes(missing);
        var missingKey = Path.GetRelativePath(home.Root, missing);
        var customBytes = File.ReadAllBytes(custom).Concat(new byte[] { 0 }).ToArray();
        File.WriteAllBytes(custom, customBytes);
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var xml = System.Xml.Linq.XDocument.Load(metadata);
        var declaration = xml.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == customRelative);
        foreach (var hash in declaration.Elements().Where(element => element.Name.LocalName == "Hash").ToArray()) hash.Remove();
        if (updateHash) declaration.Add(new System.Xml.Linq.XElement(declaration.Name.Namespace + "Hash",
            Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(customBytes))));
        xml.Save(metadata);
        Assert.Null(StandaloneVisaReadiness.OpenTapRuntimeIssue(home));

        foreach (var owned in new[] { true, false })
        {
            File.Delete(missing);
            var before = Snapshot(home);
            var bootstrap = new OpenTapHomeBootstrapper();
            var options = new BootstrapOptions { HomeDirectory = home.Root, Offline = true };
            if (owned) bootstrap.BootstrapOwned(workspace, options);
            else bootstrap.Bootstrap(workspace, options);

            var after = Snapshot(home);
            Assert.Equal(before.Keys.Append(missingKey).Order(), after.Keys.Order());
            foreach (var file in before) Assert.Equal(file.Value, after[file.Key]);
            Assert.Equal(expectedMissing, after[missingKey]);
            Assert.Null(StandaloneVisaReadiness.OpenTapRuntimeIssue(home));
            Assert.True(StandaloneVisaReadiness.Assess(home).Available);
        }
    }

    private static Dictionary<string, byte[]> Snapshot(OpenTapHome home) => Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
    private static void AssertSnapshot(Dictionary<string, byte[]> before, OpenTapHome home)
    {
        var after = Snapshot(home);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var file in before) Assert.Equal(file.Value, after[file.Key]);
    }

    [Fact]
    public void Genuine_catalog_owned_library_is_reused_by_broker_execution_without_a_selected_root()
    {
        var home = Prepare(Workspace());
        Assert.Equal(8, AuthoringInstrumentCatalog.Discover(home).Count);
        var original = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == PublishedInstrumentComponents.PackageName);
        new OpenTapHostCatalog(new HardwareTest.Core.Settings.AppSettings(), Serilog.Log.Logger, new NeverOpenBroker()).EnsurePlugins();
        Assert.Same(original, AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == PublishedInstrumentComponents.PackageName));
    }

    [Fact]
    public async Task Cold_owned_catalog_preserves_lazy_lifecycle_serialization_and_execution_reuse()
    {
        var home = Prepare(Workspace());
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll"));
        start.ArgumentList.Add("--owned-library-lifecycle");
        start.ArgumentList.Add(home.Root);
        using var child = System.Diagnostics.Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
        var output = await stdout + await stderr;
        Assert.True(child.ExitCode == 0, output);
        Assert.Contains("cold-owned-catalog-lifecycle-and-execution-reused", output);
    }

    private sealed class NeverOpenBroker : HardwareTest.Core.Hardware.IVisaBroker
    {
        public Task<HardwareTest.Core.Hardware.IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Discovery must not open a broker session.");
    }

    [Fact]
    public void Ordinary_mock_home_does_not_install_the_standalone_counterpart()
    {
        var home = Prepare(Workspace(library: false));
        Assert.DoesNotContain(OpenTapHomeBootstrapper.ListInstalledPackages(home), package => package.Name == StandaloneVisaPackage.PackageName);
        Assert.False(File.Exists(Path.Combine(home.Root, StandaloneVisaPackage.WrapperFileName)));
        Assert.Contains("mock plans do not need it", StandaloneVisaReadiness.Assess(home).Reason);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
