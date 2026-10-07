using System.IO.Compression;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class LibraryInstrumentCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-library-tests-" + Guid.NewGuid().ToString("N"));
    public LibraryInstrumentCatalogTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Discovery_filter_accepts_future_concrete_device_types_without_a_name_list()
    {
        var types = AuthoringInstrumentCatalog.DeviceTypes(
            [typeof(CatalogScope), typeof(FutureCatalogDevice), typeof(CatalogBase), typeof(MockDmmInstrument), typeof(AbstractCatalogDevice), typeof(GenericCatalogDevice<>)], typeof(CatalogBase));
        Assert.Equal([typeof(CatalogScope), typeof(FutureCatalogDevice)], types);
    }

    [Fact]
    public void Missing_library_does_not_borrow_another_home_or_arbitrary_installed_plugins()
    {
        var home = new OpenTapHome(_root);
        Assert.Empty(AuthoringInstrumentCatalog.Discover(home));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(
            new("Supply", "InstrumentComponents.OpenTap.DcPowerSupplyInstrument", "TCPIP::192.0.2.3::INSTR"), home));
        Assert.Contains("selected home", error.Message);
        Assert.Contains("Environment", error.Message);
        Assert.False(AuthoringInstrumentCatalog.TryGet(typeof(CatalogScope).FullName!, out _));
    }

    // Exercise exact published payload bytes by default; explicit fixture overrides remain supported.
    [Fact]
    public void Actual_upstream_catalog_and_non_dmm_lifecycle_roundtrip_preserve_exact_bindings()
    {
        var package = PublishedLibraryFixture.PackageRoot;
        var home = InstallActualPackage(package!);
        var adapters = AuthoringInstrumentCatalog.Discover(home);
        Assert.Equal(8, adapters.Count);
        Assert.Contains(adapters, a => a.DisplayName == "Oscilloscope");
        Assert.Contains(adapters, a => a.DisplayName == "DC Power Supply");
        var second = AuthoringInstrumentCatalog.Discover(home);
        foreach (var adapter in adapters) Assert.Same(adapter, second.Single(a => a.TypeId == adapter.TypeId));
        var supply = adapters.Single(a => a.DisplayName == "DC Power Supply");
        Assert.True(supply.Availability(home).Available);
        var binding = new InstrumentRef("Power rail", supply.TypeId, "TCPIP0::192.0.2.7::inst0::INSTR")
        { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "8123" } };
        var result = new AuthoringPlanInitializer().Construct(new("library-plan")
        { Home = home, Instruments = [binding], IdentityInstrumentSlot = binding.SlotName, IncludeSafeShutdown = true });
        var path = Path.Combine(_root, "library-plan.TapPlan");
        var compiler = new PlanCompiler([Path.Combine(home.Root, "Packages", AuthoringInstrumentCatalog.LibraryPackage)], home);
        compiler.Save(result.Draft, path);
        var xml = XDocument.Load(path).ToString();
        Assert.Contains("InstrumentComponents.OpenTap.DcPowerSupplyInstrument", xml);
        Assert.Contains("InstrumentComponents.OpenTap.IdentityQueryStep", xml);
        Assert.Contains("InstrumentComponents.OpenTap.SafeShutdownStep", xml);
        Assert.DoesNotContain("IdentityCheckStep", xml);
        Assert.DoesNotContain("MockDmmInstrument", xml);
        var reopened = compiler.Load(path);
        var resource = Assert.Single(reopened.Instruments);
        Assert.Equal(binding.TypeId, resource.TypeId);
        Assert.Equal(binding.SlotName, resource.SlotName);
        Assert.Equal(binding.VisaAddress, resource.VisaAddress);
        Assert.Equal("8123", resource.Settings["IoTimeoutMilliseconds"]);
        Assert.Equal(Assert.Single(result.Draft.Setup).NodeId, Assert.Single(reopened.Setup).NodeId);
        Assert.Equal(result.Draft.Cleanup.NodeId, reopened.Cleanup.NodeId);
        Assert.Equal([binding.SlotName], reopened.Cleanup.InstrumentSlots);
        var source = AuthoringDocumentDto.FromDraft(result.Draft).ToDraft();
        Assert.Equal(binding.TypeId, Assert.Single(source.Instruments).TypeId);
        Assert.Equal("8123", Assert.Single(source.Instruments).Settings["IoTimeoutMilliseconds"]);
        var incompatible = new AuthoringPlanInitializer().Construct(new("unsupported")
        { Home = home, Instruments = [binding], Measurement = new(AuthoringRecipeIds.MeanGte, binding.SlotName) });
        Assert.Contains(incompatible.Issues, issue => issue.Code == "INSTRUMENT_INCOMPATIBLE");
        Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(incompatible.Draft, Path.Combine(_root, "unsupported.TapPlan")));
        VerifyResidentProvenance(home, adapters);
        var foreign = new OpenTapHome(Path.Combine(_root, "foreign-home"));
        Assert.Empty(AuthoringInstrumentCatalog.Discover(foreign));
        Assert.False(supply.Availability(foreign).Available);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(binding, foreign));
        var providerCalls = 0;
        var foreignCompiler = new PlanCompiler(libraryHomeProvider: () => { providerCalls++; return foreign; });
        var unavailableImport = foreignCompiler.Load(path);
        Assert.Equal(1, providerCalls); // Home is captured once at the load boundary.
        var unavailableResource = Assert.Single(unavailableImport.Instruments);
        Assert.Equal(binding.TypeId, unavailableResource.TypeId);
        Assert.Equal("8123", unavailableResource.Settings["IoTimeoutMilliseconds"]);
        Assert.NotNull(unavailableResource.OpaqueResourceXml);
        Assert.Equal(binding.SlotName, Assert.IsType<IdentitySetup>(Assert.Single(unavailableImport.Setup)).InstrumentSlot);
        Assert.Equal([binding.SlotName], unavailableImport.Cleanup.InstrumentSlots);
        var payload = Path.Combine(home.Root, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        var bytes = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
        var workspace = new AuthoringWorkspace(_root, new AuthoringManifest
        { Dependencies = [new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "^0.1.0" }] }, []);
        new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true });
        foreach (var pair in bytes) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        Assert.False(File.Exists(Path.Combine(home.Root, "Packages", "HardwareTest VISA", AuthoringPluginSearch.VisaAssemblyFileName)));
        var vmRoot = Path.Combine(_root, "workspace");
        new AuthoringWorkspaceInitializer().Create(new(vmRoot, "Actual library workspace", "Actual library workspace") { IncludeLibraryPackage = true });
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(vmRoot); vm.OpenTapHomeOverride = home.Root;
        vm.InitializePlan(new("persisted-library") { Instruments = [binding], IdentityInstrumentSlot = binding.SlotName });
        vm.DisplayName = "Saved library lifecycle"; Assert.True(vm.SaveAll().Succeeded);
        var vmPath = Path.Combine(vmRoot, "plans", "persisted-library.TapPlan");
        Assert.True(File.Exists(vmPath)); Assert.Contains("InstrumentComponents.OpenTap.DcPowerSupplyInstrument", File.ReadAllText(vmPath));
        vm.Open(vmRoot); vm.SelectProgram("persisted-library");
        Assert.Equal(binding.TypeId, Assert.Single(vm.SelectedProgram!.Instruments).TypeId);
        Assert.Equal("8123", Assert.Single(vm.SelectedProgram.Instruments).Settings["IoTimeoutMilliseconds"]);
        var active = vm.Workspace;
        var badRoot = Path.Combine(_root, "bad-workspace"); Directory.CreateDirectory(badRoot); File.WriteAllText(Path.Combine(badRoot, "authoring.json"), "invalid json");
        Assert.ThrowsAny<Exception>(() => vm.PrepareOpen(badRoot));
        Assert.Same(active, vm.Workspace); Assert.Equal(home.Root, vm.OpenTapHomeOverride);
        var metadata = XDocument.Load(Path.Combine(payload, "package.xml"));
        metadata.Root!.SetAttributeValue("Version", "2.0.0"); metadata.Save(Path.Combine(payload, "package.xml"));
        Assert.Empty(AuthoringInstrumentCatalog.Discover(home));
        Assert.False(supply.Availability(home).Available);
        metadata.Root.SetAttributeValue("Version", HardwareTest.OpenTap.Host.PublishedInstrumentComponents.Version); metadata.Save(Path.Combine(payload, "package.xml"));
        // Same assembly name/version in another home cannot borrow cached types if its binary differs.
        using (var stream = new FileStream(Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll"), FileMode.Append)) stream.WriteByte(0);
        Assert.Empty(AuthoringInstrumentCatalog.Discover(home));
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(binding, home));
        File.WriteAllBytes(Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll"), bytes[Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll")]);
        File.Delete(Path.Combine(home.Root, "InstrumentComponents.dll"));
        Assert.Empty(AuthoringInstrumentCatalog.Discover(home));
        Assert.Contains("payload", supply.Availability(home).Reason);
    }

    [Fact]
    public void Actual_genuine_archive_import_discovers_home_root_payload_and_roundtrips_library_lifecycle()
    {
        var archivePath = PublishedLibraryFixture.Archive;
        var home = new OpenTapHome(Path.Combine(_root, "archive-home"));
        var workspace = new AuthoringWorkspace(_root, new AuthoringManifest
        {
            Dependencies = [new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "^0.1.0" }],
            InstrumentComponentsPackage = archivePath
        }, []);
        var bootstrap = new OpenTapHomeBootstrapper();
        bootstrap.Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true });
        using var archive = ZipFile.OpenRead(archivePath!);
        var originals = new Dictionary<string, byte[]>();
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" })
        {
            using var incoming = archive.GetEntry(file)!.Open(); using var bytes = new MemoryStream(); incoming.CopyTo(bytes);
            originals[file] = bytes.ToArray(); Assert.Equal(originals[file], File.ReadAllBytes(Path.Combine(home.Root, file)));
        }
        var adapters = AuthoringInstrumentCatalog.Discover(home);
        Assert.Equal(8, adapters.Count);
        var supply = adapters.Single(adapter => adapter.DisplayName == "DC Power Supply");
        Assert.True(supply.Availability(home).Available);
        var binding = new InstrumentRef("Rail", supply.TypeId, "TCPIP0::192.0.2.7::inst0::INSTR")
        { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "8123" } };
        var draft = new AuthoringPlanInitializer().Construct(new("archive-plan")
        { Home = home, Instruments = [binding], IdentityInstrumentSlot = "Rail" }).Draft;
        var path = Path.Combine(_root, "archive-plan.TapPlan"); var compiler = new PlanCompiler(selectedHome: home);
        compiler.Save(draft, path); var reopened = compiler.Load(path);
        Assert.Equal(binding, Assert.Single(reopened.Instruments) with { Settings = binding.Settings });
        Assert.Equal("8123", Assert.Single(reopened.Instruments).Settings["IoTimeoutMilliseconds"]);
        Assert.Equal(draft.Cleanup.NodeId, reopened.Cleanup.NodeId);
        bootstrap.Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true });
        foreach (var pair in originals) Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(home.Root, pair.Key)));
        File.WriteAllBytes(Path.Combine(home.Root, "InstrumentComponents.OpenTap.dll"), originals["InstrumentComponents.OpenTap.dll"].Concat(new byte[] { 0 }).ToArray());
        Assert.Empty(AuthoringInstrumentCatalog.Discover(home));
        bootstrap.Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true });
        Assert.Equal(8, AuthoringInstrumentCatalog.Discover(home).Count);
        foreach (var pair in originals) Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(home.Root, pair.Key)));
        Assert.Empty(Directory.GetFiles(home.Root, AuthoringPluginSearch.VisaAssemblyFileName, SearchOption.AllDirectories));
    }

    private void VerifyResidentProvenance(OpenTapHome origin, IReadOnlyList<AuthoringInstrumentAdapter> adapters)
    {
        var original = origin.Root;
        var second = new OpenTapHome(Path.Combine(_root, "identical-home"));
        var copied = second.Root;
        Directory.CreateDirectory(copied);
        var bytes = Directory.GetFiles(original, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(original, file), File.ReadAllBytes);
        foreach (var pair in bytes)
        {
            var destination = Path.Combine(copied, pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, pair.Value);
        }
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" })
        {
            File.Delete(Path.Combine(original, file));
            Assert.Equal(adapters.Count, AuthoringInstrumentCatalog.Discover(second).Count);
            var changed = bytes[file].Concat(new byte[] { 0 }).ToArray();
            File.WriteAllBytes(Path.Combine(original, file), changed);
            Assert.Equal(adapters.Count, AuthoringInstrumentCatalog.Discover(second).Count);
            File.WriteAllBytes(Path.Combine(copied, file), changed);
            Assert.Empty(AuthoringInstrumentCatalog.Discover(second));
            Assert.Contains("hash", AuthoringInstrumentCatalog.LibraryReadiness(second).Reason);
            File.WriteAllBytes(Path.Combine(original, file), bytes[file]);
            File.WriteAllBytes(Path.Combine(copied, file), bytes[file]);
        }
    }

    private OpenTapHome InstallActualPackage(string package)
    {
        var home = new OpenTapHome(Path.Combine(_root, "actual-home"));
        var target = Path.Combine(home.Root, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        Directory.CreateDirectory(target);
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" }) File.Copy(Path.Combine(package, file), Path.Combine(home.Root, file));
        File.Copy(Path.Combine(package, "package.xml"), Path.Combine(target, "package.xml"));
        return home;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

public abstract class CatalogBase : Instrument { }
public sealed class CatalogScope : CatalogBase { }
public sealed class FutureCatalogDevice : CatalogBase { }
public abstract class AbstractCatalogDevice : CatalogBase { }
public sealed class GenericCatalogDevice<T> : CatalogBase { }
