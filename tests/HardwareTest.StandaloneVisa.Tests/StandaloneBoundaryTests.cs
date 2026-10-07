using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-standalone-test-" + Guid.NewGuid().ToString("N"));
    public StandaloneBoundaryTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Genuine_installed_home_actual_wrapper_dispatch_registers_and_preserves_provider_and_exit_code()
    {
        var home = InstalledHome(minimal: true);
        var result = await Run(home, StandaloneVisaPackage.WrapperFileName, "visa-boundary");
        Assert.Equal(17, result.Code);
        Assert.Contains("standalone-registered-before-dispatch-existing-provider-preserved", result.Output);
    }

    [Fact]
    public async Task Actual_published_interface_uses_broker_and_closes_failed_acquired_lease()
    {
        var home = InstalledHome();
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home);
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Cold_managed_fallback_stages_the_genuine_bundled_payload_before_typed_calls()
    {
        var home = InstalledHome();
        RemoveBaseFromFixture(home);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", "fallback");
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Cold_process_reuses_already_loaded_genuine_library_without_a_configured_selection()
    {
        var home = InstalledHome();
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--loaded");
        Assert.Equal(0, result.Code);
        Assert.Contains("already-loaded-library-reused", result.Output);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Externally_preloaded_library_with_same_mvid_replaced_origin_has_no_verified_provenance()
    {
        var home = InstalledHome();
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--external-same-mvid-update");
        Assert.Equal(0, result.Code);
        Assert.Contains("unverified-preloaded-library-refused", result.Output);
    }

    [Fact]
    public async Task Current_custom_selected_owned_payload_is_reused()
    {
        var home = InstalledHome();
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        foreach (var file in new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll" })
        {
            var path = Path.Combine(home, file);
            byte[] bytes = [.. File.ReadAllBytes(path), 1];
            File.WriteAllBytes(path, bytes);
            document.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == file)
                .Elements().Single(element => element.Name.LocalName == "Hash").Value = Convert.ToHexString(SHA1.HashData(bytes));
        }
        document.Save(metadata);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--managed-custom-reuse");
        Assert.Equal(0, result.Code);
        Assert.Contains("current-custom-owned-library-reused", result.Output);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Selected_additional_declared_payload_cannot_escape_through_a_link_before_library_loading()
    {
        if (OperatingSystem.IsWindows()) return;
        var home = InstalledHome();
        var outside = Path.Combine(_root, "outside-payload.txt");
        File.WriteAllText(outside, "external-library-payload");
        var linked = Path.Combine(home, "additional.txt");
        File.CreateSymbolicLink(linked, outside);
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        document.Root!.Elements().Single(element => element.Name.LocalName == "Files")
            .Add(new XElement("File", new XAttribute("Path", "additional.txt"),
                new XElement("Hash", Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(outside))))));
        document.Save(metadata);
        var originalMetadata = File.ReadAllBytes(metadata);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--invalid-selected-metadata", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains("resolves outside its root", result.Output);
        Assert.Equal(outside, new FileInfo(linked).LinkTarget);
        Assert.Equal(originalMetadata, File.ReadAllBytes(metadata));
        Assert.Equal("external-library-payload", File.ReadAllText(outside));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_execution_accepts_optional_hashes_and_contained_additional_declared_payload(bool omitHashes)
    {
        var home = InstalledHome();
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        if (omitHashes)
            document.Descendants().Where(element => element.Name.LocalName == "Hash").Remove();
        var additional = Path.Combine(home, "Documentation", "library.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(additional)!);
        File.WriteAllText(additional, "selected-library-documentation");
        document.Root!.Elements().Single(element => element.Name.LocalName == "Files")
            .Add(new XElement("File", new XAttribute("Path", "Documentation/library.txt"),
                new XElement("Hash", Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(additional))))));
        document.Save(metadata);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home);
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }

    [Theory]
    [InlineData("stale-contract")]
    [InlineData("stale-provider")]
    [InlineData("malformed-hash")]
    [InlineData("nested-hash")]
    [InlineData("nested-hash-content")]
    [InlineData("duplicate-hash")]
    [InlineData("missing-contract-declaration")]
    [InlineData("missing-provider-declaration")]
    [InlineData("nested-file")]
    [InlineData("duplicate-file")]
    [InlineData("missing-extra-payload")]
    [InlineData("unsafe-extra-payload")]
    public async Task Invalid_selected_metadata_is_refused_before_either_library_load_without_fallback_or_home_changes(string defect)
    {
        var home = InstalledHome();
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        var files = document.Root!.Elements().Single(element => element.Name.LocalName == "Files");
        var contract = files.Elements().Single(element => (string?)element.Attribute("Path") == "InstrumentComponents.dll");
        var provider = files.Elements().Single(element => (string?)element.Attribute("Path") == "InstrumentComponents.OpenTap.dll");
        var hash = provider.Elements().Single(element => element.Name.LocalName == "Hash");
        switch (defect)
        {
            case "stale-contract":
            case "stale-provider":
                var path = Path.Combine(home, defect == "stale-contract" ? "InstrumentComponents.dll" : "InstrumentComponents.OpenTap.dll");
                File.WriteAllBytes(path, [.. File.ReadAllBytes(path), 1]);
                break;
            case "malformed-hash": hash.Value = "not-a-sha1"; break;
            case "nested-hash": hash.ReplaceWith(new XElement("Nested", new XElement(hash))); break;
            case "nested-hash-content": hash.Add(new XElement("Value", hash.Value)); break;
            case "duplicate-hash": provider.Add(new XElement(hash)); break;
            case "missing-contract-declaration": contract.Remove(); break;
            case "missing-provider-declaration": provider.Remove(); break;
            case "nested-file": provider.ReplaceWith(new XElement("Nested", new XElement(provider))); break;
            case "duplicate-file": files.Add(new XElement(provider)); break;
            case "missing-extra-payload": files.Add(new XElement("File", new XAttribute("Path", "missing.txt"))); break;
            case "unsafe-extra-payload": files.Add(new XElement("File", new XAttribute("Path", "../outside.txt"))); break;
            default: throw new InvalidOperationException(defect);
        }
        document.Save(metadata);
        var before = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home, path), File.ReadAllBytes);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--invalid-selected-metadata", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
        var after = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home, path), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }

    [Fact]
    public async Task Already_loaded_library_without_the_current_provider_contract_is_refused()
    {
        var home = InstalledHome();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "RebuiltFixture", "InstrumentComponents.OpenTap.dll"), Path.Combine(home, "replacement.dll"));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--loaded-unsupported");
        Assert.Equal(0, result.Code);
        Assert.Contains("unsupported-loaded-library-refused", result.Output);
    }

    [Fact]
    public async Task Explicit_trusted_custom_payload_mismatch_is_not_replaced_by_bundled_fallback()
    {
        var home = InstalledHome();
        var custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(custom);
        foreach (var name in new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "BoundaryFixture", name), Path.Combine(custom, name));
        CopyTree(Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName), Path.Combine(custom, "Packages", PublishedInstrumentComponents.PackageName));
        var customMetadata = Path.Combine(custom, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(customMetadata);
        foreach (var declaration in document.Descendants().Where(element => element.Name.LocalName == "File"))
            declaration.Elements().Single(element => element.Name.LocalName == "Hash").Value = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(Path.Combine(custom, (string)declaration.Attribute("Path")!))));
        document.Save(customMetadata);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", custom, "--custom-mismatch", allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("differs from the loaded execution library", result.Output);
    }

    [Fact]
    public async Task Previously_loaded_library_with_atomically_replaced_origin_is_refused()
    {
        var home = InstalledHome();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "RebuiltFixture", "InstrumentComponents.OpenTap.dll"), Path.Combine(home, "replacement.dll"));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--selected-update");
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-origin-replacement-refused", result.Output);
    }

    [Theory]
    [InlineData("--loaded-update")]
    [InlineData("--loaded-update-after-reuse")]
    public async Task Unselected_loaded_library_with_atomically_replaced_origin_is_refused(string mode)
    {
        var home = InstalledHome();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "RebuiltFixture", "InstrumentComponents.OpenTap.dll"), Path.Combine(home, "replacement.dll"));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, mode);
        Assert.Equal(0, result.Code);
        Assert.Contains("unselected-loaded-origin-replacement-refused", result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_package_directory_is_rejected_as_an_installed_execution_home(bool withPayload)
    {
        var home = InstalledHome();
        var selected = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName);
        if (withPayload)
            foreach (var file in new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll" })
                File.Copy(Path.Combine(home, file), Path.Combine(selected, file));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", selected, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("requires an installed home root", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Installed_package_directory_payload_is_rejected_even_with_valid_root_payload(bool retainRootPayload)
    {
        var home = InstalledHome();
        var package = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName);
        foreach (var file in new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll" })
        {
            File.Copy(Path.Combine(home, file), Path.Combine(package, file));
            if (!retainRootPayload) File.Delete(Path.Combine(home, file));
        }
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("cannot use obsolete package-directory library DLLs", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Alias_package_directory_payload_without_root_dlls_or_canonical_metadata_is_rejected()
    {
        var home = InstalledHome();
        var alias = Path.Combine(home, "Packages", "Alias");
        Directory.CreateDirectory(alias);
        foreach (var file in new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll" })
            File.Move(Path.Combine(home, file), Path.Combine(alias, file));
        File.Delete(Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("cannot use obsolete package-directory library DLLs", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
    }

    [Theory]
    [InlineData("dll")]
    [InlineData("metadata")]
    [InlineData("package-directory")]
    [InlineData("packages")]
    public async Task Selected_execution_home_cannot_borrow_payload_or_metadata_through_outside_links(string linked)
    {
        if (OperatingSystem.IsWindows()) return; // Windows link creation requires an elevated test process.
        var home = InstalledHome();
        var outside = Path.Combine(_root, "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        if (linked is "dll" or "metadata")
        {
            var path = linked == "dll" ? Path.Combine(home, "InstrumentComponents.OpenTap.dll") : metadata;
            var target = Path.Combine(outside, Path.GetFileName(path));
            File.Move(path, target);
            File.CreateSymbolicLink(path, target);
        }
        else
        {
            var path = linked == "packages" ? Path.Combine(home, "Packages") : Path.GetDirectoryName(metadata)!;
            var target = Path.Combine(outside, Path.GetFileName(path));
            Directory.Move(path, target);
            Directory.CreateSymbolicLink(path, target);
        }
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("resolves outside its root", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
    }

    [Theory]
    [InlineData(false, "Alias")]
    [InlineData(true, "Alias")]
    [InlineData(false, "instrumentcomponents.opentap")]
    [InlineData(true, "instrumentcomponents.opentap")]
    public async Task Metadata_only_alias_cannot_grant_bundled_fallback_or_join_canonical_execution_home(bool canonical, string aliasName)
    {
        if (OperatingSystem.IsWindows() && aliasName.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase)) return;
        var home = InstalledHome();
        if (!canonical)
        {
            File.Delete(Path.Combine(home, "InstrumentComponents.dll"));
            File.Delete(Path.Combine(home, "InstrumentComponents.OpenTap.dll"));
            File.Delete(Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"));
        }
        var alias = Path.Combine(home, "Packages", aliasName);
        Directory.CreateDirectory(alias);
        File.WriteAllText(Path.Combine(alias, "package.xml"), "<Package Name=\" instrumentcomponents.opentap \" Version=\"0.1.1\"/>");
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("noncanonical or duplicate installed package identities", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Case_variant_home_root_package_metadata_is_rejected()
    {
        var home = InstalledHome();
        File.Copy(Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"), Path.Combine(home, "Package.XML"));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("requires an installed home root", result.Output);
    }

    [Fact]
    public async Task Package_directory_cycle_is_rejected_before_plugin_search()
    {
        if (OperatingSystem.IsWindows()) return;
        var home = InstalledHome();
        Directory.CreateSymbolicLink(Path.Combine(home, "Packages", "Cycle"), Path.Combine(home, "Packages"));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("link cycle", result.Output);
        Assert.DoesNotContain("managed-broker-bound-and-cleaned", result.Output);
    }

    [Fact]
    public async Task Explicit_home_root_link_keeps_its_own_resolved_boundary()
    {
        if (OperatingSystem.IsWindows()) return;
        var home = InstalledHome();
        var selected = Path.Combine(_root, "selected-link");
        Directory.CreateSymbolicLink(selected, home);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", selected);
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }

    [Theory]
    [InlineData("no-broker")]
    [InlineData("adapter-disabled")]
    public async Task Metadata_and_explicitly_disabled_adapter_do_not_acquire_execution_library(string gate)
    {
        var home = InstalledHome();
        RemoveBaseFromFixture(home);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", gate, "--metadata");
        Assert.Equal(0, result.Code);
        Assert.Contains("metadata-boundary-isolated", result.Output);
    }

    private static void RemoveBaseFromFixture(string home)
    {
        File.Delete(Path.Combine(home, "InstrumentComponents.dll"));
        File.Delete(Path.Combine(home, "InstrumentComponents.OpenTap.dll"));
        RemoveBaseRuntimeBindings(home);
    }

    private static void RemoveBaseRuntimeBindings(string home)
    {
        // The managed Host excludes the library's NuGet runtime assets. Keep typed
        // compile references in the probe, but give its process the same runtime graph.
        var deps = Path.Combine(home, "HardwareTest.StandaloneVisa.ProcessFixture.deps.json");
        var graph = JsonNode.Parse(File.ReadAllText(deps))!;
        foreach (var target in graph["targets"]!.AsObject())
            foreach (var dependency in target.Value!.AsObject())
                if (dependency.Key.StartsWith("InstrumentComponents/", StringComparison.Ordinal)
                    || dependency.Key.StartsWith("InstrumentComponents.OpenTap/", StringComparison.Ordinal))
                    dependency.Value!.AsObject().Remove("runtime");
        File.WriteAllText(deps, graph.ToJsonString());
    }

    [Fact]
    public void Counterpart_archive_has_complete_managed_graph_and_no_competing_base_and_fixed_timestamps()
    {
        using var stream = StandaloneVisaPackage.OpenArchive();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var files = zip.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains(StandaloneVisaPackage.WrapperFileName, files);
        Assert.Contains("HardwareTest.OpenTap.StandaloneVisa.deps.json", files);
        Assert.Contains("HardwareTest.OpenTap.StandaloneVisa.runtimeconfig.json", files);
        Assert.Contains("InstrumentComponents.OpenTap.Visa.dll", files);
        Assert.Contains("InstrumentComponents.Visa.dll", files);
        Assert.Contains("Ivi.Visa.dll", files);
        Assert.DoesNotContain("InstrumentComponents.dll", files);
        Assert.DoesNotContain("InstrumentComponents.OpenTap.dll", files);
        Assert.DoesNotContain("OpenTap.dll", files);
        Assert.Equal(files.Order(StringComparer.Ordinal), files);
        Assert.Equal(files.Length, files.Distinct(StringComparer.Ordinal).Count());
        // ZIP stores local clock fields without an offset. Check every field,
        // independently of the timezone of the machine reading the archive.
        Assert.All(zip.Entries, entry => Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime));
        using (var runtime = zip.GetEntry("HardwareTest.OpenTap.StandaloneVisa.runtimeconfig.json")!.Open())
        {
            var options = JsonNode.Parse(runtime)!["runtimeOptions"]!;
            Assert.Equal("Microsoft.NETCore.App", (string?)options["framework"]?["name"]);
            Assert.Null(options["includedFrameworks"]);
        }
        using var metadata = zip.GetEntry("Packages/HardwareTest Standalone VISA/package.xml")!.Open();
        var document = XDocument.Load(metadata);
        var dependencies = document.Descendants().Where(element => element.Name.LocalName == "PackageDependency").ToArray();
        Assert.Contains(dependencies, dependency => (string?)dependency.Attribute("Package") == PublishedInstrumentComponents.PackageName && (string?)dependency.Attribute("Version") == "0.1.1");
        Assert.Contains(dependencies, dependency => (string?)dependency.Attribute("Package") == "OpenTAP" && (string?)dependency.Attribute("Version") == "9.35.0");
    }

    [Fact]
    public void Standalone_readiness_rejects_substituted_or_missing_provider_and_unsupported_dependencies()
    {
        var home = InstalledHome();
        Assert.True(StandaloneVisaReadiness.Assess(new(home)).Available);
        var provider = Path.Combine(home, "InstrumentComponents.OpenTap.Visa.dll");
        var original = File.ReadAllBytes(provider);
        File.WriteAllText(provider, "substituted");
        Assert.False(StandaloneVisaReadiness.Assess(new(home)).Available);
        File.Delete(provider);
        Assert.False(StandaloneVisaReadiness.Assess(new(home)).Available);
        File.WriteAllBytes(provider, original);
        var baseLibrary = Path.Combine(home, "InstrumentComponents.OpenTap.dll");
        var genuineBase = File.ReadAllBytes(baseLibrary);
        File.WriteAllText(baseLibrary, "damaged base");
        Assert.False(StandaloneVisaReadiness.Assess(new(home)).Available);
        File.WriteAllBytes(baseLibrary, genuineBase);
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("Version=\"0.1.1\"", "Version=\"0.1.0\"", StringComparison.Ordinal));
        Assert.Contains("unsupported", StandaloneVisaReadiness.Assess(new(home)).Reason);
        Assert.Equal(original, File.ReadAllBytes(provider));
    }

    [Theory]
    [InlineData("InstrumentComponents.dll")]
    [InlineData("InstrumentComponents.OpenTap.dll")]
    public async Task Current_package_metadata_with_older_assembly_version_is_unavailable_for_readiness_and_execution(string file)
    {
        var home = InstalledHome();
        var path = Path.Combine(home, file);
        var bytes = File.ReadAllBytes(path);
        int versionOffset;
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var pe = new PEReader(stream))
            versionOffset = pe.PEHeaders.MetadataStartOffset + pe.GetMetadataReader().GetTableMetadataOffset(TableIndex.Assembly) + sizeof(uint);
        // Assembly table stores Major, Minor, Build, Revision as little-endian UInt16.
        bytes[versionOffset + 4] = 0;
        bytes[versionOffset + 5] = 0;
        File.WriteAllBytes(path, bytes);
        Assert.Equal(new Version(0, 1, 0, 0), System.Reflection.AssemblyName.GetAssemblyName(path).Version);
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        document.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == file)
            .Elements().Single(element => element.Name.LocalName == "Hash").Value = Convert.ToHexString(SHA1.HashData(bytes));
        document.Save(metadata);
        Assert.False(StandaloneVisaReadiness.Assess(new(home)).Available);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, allowFailure: true);
        Assert.NotEqual(0, result.Code);
        Assert.Contains("requires current 0.1.1 library assemblies", result.Output);
    }

    [Fact]
    public void Launcher_selects_wrapper_for_library_home_and_retains_literal_arguments()
    {
        var home = InstalledHome();
        var plan = Path.Combine(_root, "R&D %PATH% plan.TapPlan");
        File.WriteAllText(plan, "fixture");
        var start = AuthoringExternalTuiLauncher.WindowsStartInfo("dotnet", home, plan);
        Assert.True(start.UseShellExecute);
        Assert.Equal(["--roll-forward", "Major", Path.Combine(home, StandaloneVisaPackage.WrapperFileName), "tui", plan], start.ArgumentList);
        File.Delete(Path.Combine(home, "InstrumentComponents.OpenTap.Visa.dll"));
        Assert.Contains("counterpart", AuthoringExternalTuiLauncher.Prerequisite(home, plan));
    }

    private string InstalledHome(bool minimal = false)
    {
        var home = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var fixture = Path.Combine(AppContext.BaseDirectory, "BoundaryFixture");
        if (!minimal) CopyTree(fixture, home);
        else
        {
            CopyTree(Path.Combine(fixture, "Packages", "OpenTAP"), Path.Combine(home, "Packages", "OpenTAP"));
            CopyTree(Path.Combine(fixture, "Dependencies"), Path.Combine(home, "Dependencies"));
            foreach (var name in new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json" })
                File.Copy(Path.Combine(fixture, name), Path.Combine(home, name));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "CliFixture", "HardwareTest.StandaloneVisa.CliFixture.dll"), Path.Combine(home, "HardwareTest.StandaloneVisa.CliFixture.dll"));
        }
        // Only the genuine TAP bytes are present at process startup, never both bases.
        using (var baseArchive = PublishedInstrumentComponents.OpenArchive()) Extract(baseArchive, home);
        using (var counterpart = StandaloneVisaPackage.OpenArchive()) Extract(counterpart, home);
        if (!minimal) RemoveBaseRuntimeBindings(home);
        return home;
    }

    private static void Extract(Stream archive, string home)
    {
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var path = Path.Combine(home, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var target = File.Create(path);
            source.CopyTo(target);
        }
    }

    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var path = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(file, path);
        }
    }

    private static async Task<(int Code, string Output)> Run(string home, string assembly, string argument, string mode = "--managed", bool allowFailure = false)
    {
        using var rejectionDirectory = mode is "--invalid-selected-metadata" or "--invalid-multiple-roots" or "--replace-approved-contract" or "--replace-approved-provider"
            ? new RejectionWorkingDirectory() : null;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
        {
            // OpenTAP initialization logs must stay outside every selected root and
            // fixture tree captured by the rejection tests, including their parents.
            WorkingDirectory = rejectionDirectory?.DirectoryPath ?? home,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(rejectionDirectory?.DirectoryPath ?? home, assembly));
        if (assembly != StandaloneVisaPackage.WrapperFileName) start.ArgumentList.Add(mode);
        start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        var output = await stdout + await stderr;
        if (!allowFailure) Assert.True(child.ExitCode is 0 or 17, output);
        return (child.ExitCode, output);
    }
}
