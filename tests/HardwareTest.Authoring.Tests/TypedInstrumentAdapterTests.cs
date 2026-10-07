using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class TypedInstrumentAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-typed-adapters-" + Guid.NewGuid().ToString("N"));
    private static string MockType => typeof(MockDmmInstrument).FullName!;
    private const string PhysicalType = "InstrumentComponents.OpenTap.DcPowerSupplyInstrument";
    private const string LegacyVisaType = "HardwareTest.OpenTap.Plugins.Basic.VisaDmmInstrument";

    public TypedInstrumentAdapterTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Catalog_constructs_exact_mock_and_current_physical_types_without_io()
    {
        var home = LibraryHome();
        var mock = Assert.IsType<MockDmmInstrument>(AuthoringInstrumentCatalog.Create(new("MOCK", MockType, "MOCK::7")));
        var physical = AuthoringInstrumentCatalog.Create(new("REAL", PhysicalType, "TCPIP::192.0.2.1::INSTR"), home);
        Assert.Equal("MOCK", mock.Name);
        Assert.Equal("MOCK::7", mock.VisaAddress);
        Assert.Equal("MOCK::7", mock.ResourceName);
        Assert.Equal(PhysicalType, physical.GetType().FullName);
        Assert.Equal("REAL", physical.Name);
        Assert.Equal("TCPIP::192.0.2.1::INSTR", physical.GetType().GetProperty("VisaAddress")!.GetValue(physical));
        Assert.True(AuthoringInstrumentCatalog.TryGet(PhysicalType, out var adapter));
        Assert.Empty(adapter.CompatibleFunctions);
        Assert.True(adapter.SupportsIdentity);
        Assert.True(adapter.SupportsShutdown);
        Assert.True(AuthoringFunctionCatalog.TryGet(AuthoringFunctionIds.BasicIdentityCheck, out var identity));
        Assert.True(identity.NeedsInstrument);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("9999999999999999999")]
    public void Current_physical_invalid_timeout_is_reported_at_the_authoring_boundary(string timeout)
    {
        var home = LibraryHome();
        var slot = new InstrumentRef("REAL", PhysicalType, "TCPIP::192.0.2.1::INSTR")
        { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = timeout } };
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(slot, home));
        Assert.Contains("INSTRUMENT_CONFIGURATION", error.Message);
    }

    [Fact]
    public void Legacy_visa_dmm_is_unavailable_and_never_substituted()
    {
        Assert.False(AuthoringInstrumentCatalog.TryGet(LegacyVisaType, out _));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(new("DMM", LegacyVisaType, "TCPIP::192.0.2.1::INSTR")));
        Assert.Contains("INSTRUMENT_UNAVAILABLE", error.Message);
        Assert.Contains(LegacyVisaType, error.Message);
    }

    [Theory]
    [InlineData("Other.MockDmmInstrument")]
    [InlineData("HardwareTest.OpenTap.Plugins.Basic.MockDmmInstrumentExtra")]
    [InlineData("hardwaretest.opentap.plugins.basic.mockdmminstrument")]
    public void Unregistered_mock_like_names_are_rejected_without_substitution(string typeId)
    {
        Assert.False(AuthoringInstrumentCatalog.TryGet(typeId, out _));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(new("DMM", typeId, "MOCK::0")));
        Assert.Contains("INSTRUMENT_UNAVAILABLE", error.Message);
        Assert.Contains(typeId, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_home_missing_package_or_assembly_blocks_save_and_preserves_existing_files(bool packagePresent)
    {
        Assert.True(AuthoringInstrumentCatalog.TryGet(MockType, out var adapter));
        var home = new OpenTapHome(Path.Combine(_root, "home"));
        var packageDirectory = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        Directory.CreateDirectory(packageDirectory);
        if (packagePresent) File.WriteAllText(Path.Combine(packageDirectory, "package.xml"), $"<Package Name=\"{adapter.RequiredPackage}\"><Files><File Path=\"{adapter.AssemblyFile}\"/></Files></Package>");
        var availability = adapter.Availability(home);
        Assert.False(availability.Available);
        Assert.Contains(packagePresent ? adapter.AssemblyFile : "metadata", availability.Reason!);
        Assert.Contains(AuthoringIssueService.GetIssues(MockDraft(), home), issue => issue.Code == "INSTRUMENT_UNAVAILABLE");
        var path = Path.Combine(_root, "typed.TapPlan");
        var sidecar = PlanCompiler.SidecarPath(path);
        File.WriteAllText(path, "existing plan");
        File.WriteAllText(sidecar, "existing sidecar");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler(selectedHome: home).Save(MockDraft(), path));
        Assert.Contains("INSTRUMENT_UNAVAILABLE", error.Message);
        Assert.Equal("existing plan", File.ReadAllText(path));
        Assert.Equal("existing sidecar", File.ReadAllText(sidecar));
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(sidecar + ".saving"));
    }

    [Fact]
    public void Current_physical_save_load_preserves_type_address_settings_and_lifecycle_binding_without_io()
    {
        var home = LibraryHome();
        var path = Path.Combine(_root, "typed.TapPlan");
        var compiler = new PlanCompiler(selectedHome: home);
        var draft = new ProgramDraft("typed", new ProgramSidecar { DisplayName = "Typed" },
            [new InstrumentRef("SUPPLY", PhysicalType, "TCPIP::192.0.2.1::INSTR") { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "1234" } }],
            [new IdentitySetup("SUPPLY")], [], new CleanupPolicy(true, "SUPPLY"));
        compiler.Save(draft, path);
        var loaded = compiler.Load(path);
        var slot = Assert.Single(loaded.Instruments);
        Assert.Equal(PhysicalType, slot.TypeId);
        Assert.Equal("TCPIP::192.0.2.1::INSTR", slot.VisaAddress);
        Assert.Equal("1234", slot.Settings["IoTimeoutMilliseconds"]);
        Assert.Equal(Assert.Single(draft.Setup).NodeId, Assert.Single(loaded.Setup).NodeId);
        Assert.Equal(draft.Cleanup.NodeId, loaded.Cleanup.NodeId);
        var identity = Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)), step => step.GetType().FullName == "InstrumentComponents.OpenTap.IdentityQueryStep");
        var resource = Assert.IsAssignableFrom<Instrument>(identity.GetType().GetProperty("Instrument")!.GetValue(identity));
        Assert.Equal(PhysicalType, resource.GetType().FullName);
        Assert.Equal(slot.SlotName, resource.Name);
        Assert.Equal(slot.VisaAddress, resource.GetType().GetProperty("VisaAddress")!.GetValue(resource));
        Assert.Equal(1234, resource.GetType().GetProperty("IoTimeoutMilliseconds")!.GetValue(resource));
        Assert.DoesNotContain("MockDmmInstrument", File.ReadAllText(path));
    }

    [Fact]
    public void Imported_mean_gte_on_second_slot_round_trips_its_actual_runtime_binding()
    {
        AuthoringPluginSearch.Search();
        var first = new MockDmmInstrument { Name = "FIRST", VisaAddress = "MOCK::0", ResourceName = "MOCK::0" };
        var second = new MockDmmInstrument { Name = "SECOND", VisaAddress = "MOCK::1", ResourceName = "MOCK::1" };
        var plan = new TestPlan();
        // Encounter FIRST before SECOND so the binding cannot accidentally pass via first-slot fallback.
        plan.ChildTestSteps.Add(new AcquireVoltageStep { Name = "Acquire", Instrument = first });
        plan.ChildTestSteps.Add(new MeanGteStep { Name = "Mean", Instrument = second, Threshold = 1.2 });
        var path = Path.Combine(_root, "imported.TapPlan");
        plan.Save(path);
        var compiler = new PlanCompiler();
        var imported = compiler.Load(path);
        Assert.Equal("FIRST", imported.Instruments[0].SlotName);
        var mean = AuthoringRecipeCatalog.EnumerateMetrics(imported.Measure).Single(m => m.Source is AlgorithmSource);
        Assert.Equal("SECOND", Assert.IsType<AlgorithmSource>(mean.Source).InstrumentSlot);

        compiler.Save(imported, path);

        var compiled = Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<MeanGteStep>());
        Assert.Equal("SECOND", compiled.Instrument.Name);
        Assert.Equal("MOCK::1", Assert.IsType<MockDmmInstrument>(compiled.Instrument).VisaAddress);
        var reloaded = compiler.Load(path);
        Assert.Equal("SECOND", Assert.IsType<AlgorithmSource>(AuthoringRecipeCatalog.EnumerateMetrics(reloaded.Measure).Single(m => m.Source is AlgorithmSource).Source).InstrumentSlot);
    }

    [Fact]
    public void Source_document_roundtrip_preserves_binding_opaque_resource_and_node_identity()
    {
        var original = MeanDraft();
        original = original with { Instruments = [original.Instruments[0] with { OpaqueResourceXml = "<Resource type='Vendor.Resource'><Secret>kept</Secret></Resource>" }, original.Instruments[1]] };
        var json = JsonSerializer.Serialize(AuthoringDocumentDto.FromDraft(original), AuthoringDocumentJsonContext.Default.AuthoringDocumentDto);
        var restored = JsonSerializer.Deserialize(json, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto)!.ToDraft();
        var before = Assert.IsType<MetricNode>(Assert.Single(original.Measure));
        var after = Assert.IsType<MetricNode>(Assert.Single(restored.Measure));
        Assert.Equal(before.NodeId, after.NodeId);
        Assert.Equal("SECOND", Assert.IsType<AlgorithmSource>(after.Metric.Source).InstrumentSlot);
        Assert.Equal(original.Instruments[0].OpaqueResourceXml, restored.Instruments[0].OpaqueResourceXml);
        Assert.Equal(original.Cleanup.NodeId, restored.Cleanup.NodeId);
    }

    [Fact]
    public void Unknown_resource_import_preserves_xml_and_refuses_mock_substitution_on_save()
    {
        var path = Path.Combine(_root, "typed.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(MockDraft() with { Instruments = [new InstrumentRef("DMM", MockType, "MOCK::0")] }, path);
        var xml = XDocument.Load(path);
        foreach (var resource in xml.Descendants().Where(element => element.Name.LocalName == "Instrument" && element.Attribute("type") is not null))
        {
            resource.SetAttributeValue("type", "Vendor.MockDmmInstrument");
            resource.Add(new XElement("VendorCalibration", "preserved-calibration"));
        }
        xml.Save(path);
        var originalBytes = File.ReadAllBytes(path);

        var imported = compiler.Load(path);

        var instrument = Assert.Single(imported.Instruments);
        Assert.Equal("Vendor.MockDmmInstrument", instrument.TypeId);
        Assert.Equal("DMM", instrument.SlotName);
        Assert.Equal("MOCK::0", instrument.VisaAddress);
        Assert.Contains("preserved-calibration", instrument.OpaqueResourceXml!);
        Assert.Contains("Vendor.MockDmmInstrument", instrument.OpaqueResourceXml!);
        var persisted = AuthoringDocumentDto.FromDraft(imported).ToDraft();
        Assert.Equal(instrument.OpaqueResourceXml, Assert.Single(persisted.Instruments).OpaqueResourceXml);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(imported, path));
        Assert.Contains("INSTRUMENT_UNAVAILABLE", error.Message);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Instrument_bound_wrong_function_fails_compile_with_no_output()
    {
        var draft = MockDraft();
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        draft = draft with { Measure = [metric with { Metric = metric.Metric with { Limits = new LimitSpec(null, null, 1.2), Source = new MeasureSource("DMM", AuthoringFunctionIds.BasicChannelAverage, new Dictionary<string, string>()) } }] };
        var path = Path.Combine(_root, "typed.TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains("INSTRUMENT_INCOMPATIBLE", error.Message);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
    }

    [Fact]
    public void Capability_compatible_mock_replacement_retargets_mean_identity_and_cleanup()
    {
        var draft = MeanDraft() with { Setup = [new IdentitySetup("FIRST")], Cleanup = new CleanupPolicy(true, ["FIRST"], true) };
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        draft = draft with { Measure = [metric with { Metric = metric.Metric with { Source = Assert.IsType<AlgorithmSource>(metric.Metric.Source) with { InstrumentSlot = "FIRST" } } }] };
        var replacement = new InstrumentRef("REAL", MockType, "MOCK::replacement");
        Assert.True(AuthoringInstrumentCatalog.CanReplace(draft, "FIRST", replacement));
        Assert.False(AuthoringInstrumentCatalog.CanReplace(draft, "FIRST", replacement with { TypeId = "Unknown.Dmm" }));
        var next = AuthoringInstrumentUsage.RetargetSlot(draft, "FIRST", "REAL");
        Assert.Equal("REAL", Assert.Single(next.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.Equal("REAL", Assert.IsType<AlgorithmSource>(Assert.IsType<MetricNode>(Assert.Single(next.Measure)).Metric.Source).InstrumentSlot);
        Assert.Equal(["REAL"], AuthoringCleanup.ResolveSlots(next));
        Assert.Equal("FIRST", Assert.Single(draft.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.Equal(["FIRST"], draft.Cleanup.InstrumentSlots);
        next = next with { Instruments = [replacement, draft.Instruments[1]] };
        var path = Path.Combine(_root, "typed.TapPlan");
        new PlanCompiler().Save(next, path);
        var runtime = Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<MeanGteStep>());
        Assert.Equal("REAL", runtime.Instrument.Name);
        Assert.Equal(replacement.VisaAddress, Assert.IsType<MockDmmInstrument>(runtime.Instrument).VisaAddress);
    }

    [Theory]
    [InlineData("algorithm-binding")]
    [InlineData("instrument-type")]
    [InlineData("instrument-address")]
    [InlineData("instrument-slot")]
    [InlineData("opaque-resource")]
    [InlineData("cleanup-enabled")]
    [InlineData("cleanup-slots")]
    [InlineData("cleanup-measure-policy")]
    public void Removal_review_rejects_each_isolated_content_change(string field)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "dirs.proj"))) repo = repo.Parent;
        File.Copy(Path.Combine(repo!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_root);
        vm.InitializePlan(new("typed") { Instruments = [] });
        vm.ReplaceSelected(MeanDraft());
        vm.SelectedInstrumentSlot = "FIRST";
        var impact = vm.PrepareSelectedInstrumentRemoval();
        var current = vm.SelectedProgram!;
        var metric = Assert.IsType<MetricNode>(Assert.Single(current.Measure));
        var changed = field switch
        {
            "algorithm-binding" => current with { Measure = [metric with { Metric = metric.Metric with { Source = Assert.IsType<AlgorithmSource>(metric.Metric.Source) with { InstrumentSlot = "FIRST" } } }] },
            "instrument-type" => current with { Instruments = [current.Instruments[0] with { TypeId = PhysicalType }, current.Instruments[1]] },
            "instrument-address" => current with { Instruments = [current.Instruments[0] with { VisaAddress = "MOCK::changed" }, current.Instruments[1]] },
            "instrument-slot" => current with { Instruments = [current.Instruments[0] with { SlotName = "RENAMED" }, current.Instruments[1]] },
            "opaque-resource" => current with { Instruments = [current.Instruments[0] with { OpaqueResourceXml = "<Resource><Changed/></Resource>" }, current.Instruments[1]] },
            "cleanup-enabled" => current with { Cleanup = current.Cleanup with { IncludeSafeShutdown = !current.Cleanup.IncludeSafeShutdown } },
            "cleanup-slots" => current with { Cleanup = current.Cleanup with { InstrumentSlots = ["FIRST"] } },
            _ => current with { Cleanup = current.Cleanup with { IncludeMeasureSlots = !current.Cleanup.IncludeMeasureSlots } }
        };
        vm.ReplaceSelected(changed);
        var programs = vm.Programs;
        var files = Directory.EnumerateFiles(_root, "*.TapPlan").ToDictionary(path => path, File.ReadAllBytes);

        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyInstrumentRemoval(impact, "SECOND"));

        Assert.Same(programs, vm.Programs);
        Assert.Same(changed, vm.SelectedProgram);
        Assert.Equal(files.Keys.Order(), Directory.EnumerateFiles(_root, "*.TapPlan").Order());
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Fact]
    public void New_legacy_recipe_persists_the_authoring_selected_slot()
    {
        var draft = AuthoringRecipeCatalog.Apply(MeanDraft() with { Measure = [] }, AuthoringRecipeIds.MeanGte, "SECOND");
        var source = Assert.IsType<AlgorithmSource>(Assert.IsType<MetricNode>(Assert.Single(draft.Measure)).Metric.Source);
        Assert.Equal("SECOND", source.InstrumentSlot);
        var path = Path.Combine(_root, "typed.TapPlan");
        new PlanCompiler().Save(draft, path);
        Assert.Equal("SECOND", Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<MeanGteStep>()).Instrument.Name);
    }

    [Fact]
    public void Unbound_instrument_algorithm_exposes_actionable_issue_and_refuses_compilation()
    {
        var draft = MeanDraft();
        var node = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        draft = draft with { Measure = [node with { Metric = node.Metric with { Source = Assert.IsType<AlgorithmSource>(node.Metric.Source) with { InstrumentSlot = null } } }] };
        Assert.Contains(AuthoringIssueService.GetIssues(draft), issue => issue.Code == "MISSING_INSTRUMENT_BINDING" && issue.NodeId == node.NodeId);
        var path = Path.Combine(_root, "typed.TapPlan");
        Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Current_physical_adapter_cannot_replace_mock_voltage_or_mean_dependencies()
    {
        LibraryHome();
        var replacement = new InstrumentRef("REAL", PhysicalType, "TCPIP::192.0.2.2::INSTR");
        Assert.False(AuthoringInstrumentCatalog.CanReplace(MeanDraft(), "SECOND", replacement));
        Assert.False(AuthoringInstrumentCatalog.CanReplace(MockDraft(), "DMM", replacement));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.EnsureCompatible(PhysicalType, AuthoringFunctionIds.BasicAcquireVoltage));
        Assert.Contains("INSTRUMENT_INCOMPATIBLE", error.Message);
    }

    [Theory]
    [InlineData("HardwareTest.OpenTap.Plugins.Basic.VisaDmmInstrument")]
    [InlineData("Vendor.ScopeInstrument")]
    public void Adding_a_slot_uses_the_visible_selected_adapter_instead_of_the_first_resource_type(string firstType)
    {
        var vm = CreationWorkspace();
        vm.ReplaceSelected(vm.SelectedProgram! with { Instruments = [new InstrumentRef("FIRST", firstType, "TCPIP::192.0.2.1::INSTR")] });
        Assert.Equal(MockType, vm.SelectedNewInstrumentType!.TypeId);
        vm.NewInstrumentSlot = "NEW";
        vm.AddInstrumentSlot();
        Assert.Equal(MockType, vm.SelectedProgram!.Instruments.Single(slot => slot.SlotName == "NEW").TypeId);
        Assert.Equal(firstType, vm.SelectedProgram.Instruments.Single(slot => slot.SlotName == "FIRST").TypeId);
    }

    [Fact]
    public void Explicit_current_physical_creation_requires_an_address_and_unknown_types_are_unavailable()
    {
        var vm = CreationWorkspace();
        vm.OpenTapHomeOverride = LibraryHome().Root;
        vm.NewInstrumentSlot = "REAL";
        vm.NewInstrumentTypeId = PhysicalType;
        Assert.False(vm.CanAddInstrumentSlot);
        Assert.Contains("instrument address", vm.NewInstrumentAvailabilityText);
        vm.NewInstrumentVisa = "TCPIP::192.0.2.2::INSTR";
        Assert.True(vm.CanAddInstrumentSlot);
        vm.AddInstrumentSlot();
        var added = vm.SelectedProgram!.Instruments.Single(slot => slot.SlotName == "REAL");
        Assert.Equal(PhysicalType, added.TypeId);
        Assert.Equal("TCPIP::192.0.2.2::INSTR", added.VisaAddress);
        vm.NewInstrumentSlot = "UNKNOWN";
        vm.NewInstrumentTypeId = "Vendor.ScopeInstrument";
        Assert.False(vm.CanAddInstrumentSlot);
        Assert.Contains("registered instrument type", vm.NewInstrumentAvailabilityText);
        var count = vm.SelectedProgram.Instruments.Count;
        Assert.Throws<AuthoringWorkspaceException>(() => vm.AddInstrumentSlot());
        Assert.Equal(count, vm.SelectedProgram.Instruments.Count);
    }

    [Theory]
    [InlineData("HardwareTest.Core.dll")]
    [InlineData("HardwareTest.OpenTap.Plugins.Basic.dll")]
    public void Selected_mock_home_requires_every_declared_payload_even_when_process_dependencies_are_loaded(string missing)
    {
        var home = MockPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(MockType, out var adapter));
        Assert.True(adapter.Availability(home).Available);
        File.Delete(Path.Combine(home.Root, "Packages", adapter.RequiredPackage, missing));
        var unavailable = adapter.Availability(home);
        Assert.False(unavailable.Available);
        Assert.Contains(missing, unavailable.Reason!);
        Assert.Contains("missing", unavailable.Reason!);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(new("MOCK", MockType, "MOCK::0"), home));
    }

    [Theory]
    [InlineData("name-only")]
    [InlineData("undeclared-required-payload")]
    [InlineData("empty")]
    [InlineData("traversal")]
    [InlineData("linked-payload")]
    [InlineData("linked-package")]
    public void Selected_home_rejects_incomplete_or_uncontained_declared_payloads(string defect)
    {
        var home = MockPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(MockType, out var adapter));
        var directory = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        var metadata = Path.Combine(directory, "package.xml");
        var dependency = Path.Combine(directory, "HardwareTest.Core.dll");
        var external = Path.Combine(_root, "external.dll");
        File.WriteAllText(external, "readable external file");
        if (defect == "name-only") File.WriteAllText(metadata, $"<Package Name=\"{adapter.RequiredPackage}\"/>");
        else if (defect == "undeclared-required-payload")
        {
            var xml = XDocument.Load(metadata);
            xml.Descendants().Single(element => (string?)element.Attribute("Path") == adapter.AssemblyFile).Remove();
            xml.Save(metadata);
        }
        else if (defect == "empty") File.WriteAllBytes(dependency, []);
        else if (defect == "traversal")
        {
            var xml = XDocument.Load(metadata);
            xml.Root!.Elements().Single(element => element.Name.LocalName == "Files").Add(new XElement("File", new XAttribute("Path", "../../../external.dll")));
            xml.Save(metadata);
        }
        else if (defect == "linked-payload") { File.Delete(dependency); File.CreateSymbolicLink(dependency, external); }
        else
        {
            var moved = Path.Combine(_root, "external-package");
            Directory.Move(directory, moved);
            Directory.CreateSymbolicLink(directory, moved);
        }
        var unavailable = adapter.Availability(home);
        Assert.False(unavailable.Available);
        Assert.Contains("Open Environment", unavailable.Reason!);
    }

    [Fact]
    public void Switching_mean_to_channel_average_has_no_phantom_instrument_constraint_or_removal_dependency()
    {
        var vm = CreationWorkspace();
        var draft = MeanDraft();
        var mean = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        mean = mean with { Metric = mean.Metric with { Source = Assert.IsType<AlgorithmSource>(mean.Metric.Source) with { InstrumentSlot = "FIRST", InputChannelKeys = ["VDC"] } } };
        var producer = new MetricNode(new MetricDraft("Acquire", "VDC", "timeseries", "V", null, null,
            new MeasureSource("SECOND", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
        vm.ReplaceSelected(draft with { Setup = [], Measure = [producer, mean], Cleanup = new CleanupPolicy(true, [], true) });
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == mean.NodeId));
        Assert.Equal(AuthoringFunctionIds.BasicMeanGte, vm.MetricFunctionId);
        vm.MetricFunctionId = AuthoringFunctionIds.BasicChannelAverage;
        Assert.Empty(vm.MetricInputChannels);
        vm.MetricInputChannels = "VDC";
        var switched = vm.SelectedProgram!;
        var source = Assert.IsType<AlgorithmSource>(AuthoringRecipeCatalog.EnumerateMetrics(switched.Measure).Last().Source);
        Assert.Null(source.InstrumentSlot);
        Assert.Empty(AuthoringDependencyIndex.Build(switched).Nodes.Single(node => node.NodeId == mean.NodeId).InstrumentSlots);
        Assert.Equal(["SECOND"], AuthoringCleanup.ResolveSlots(switched));
        Assert.DoesNotContain(AuthoringInstrumentUsage.DescribeSlotUsage(switched, "FIRST"), usage => usage.Contains("Mean", StringComparison.Ordinal));
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(switched), issue => issue.Code is "INSTRUMENT_INCOMPATIBLE" or "MISSING_INSTRUMENT");
        Assert.True(AuthoringInstrumentCatalog.CanReplace(switched, "FIRST", switched.Instruments[1]));
        vm.SelectedInstrumentSlot = "FIRST";
        var impact = vm.PrepareSelectedInstrumentRemoval();
        Assert.Contains("SECOND", impact.CompatibleReplacementSlots);
        vm.ApplyInstrumentRemoval(impact, "SECOND");
        var path = Path.Combine(_root, "typed.TapPlan");
        new PlanCompiler().Save(vm.SelectedProgram!, path);
        Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<ChannelAverageStep>());
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(vm.SelectedProgram!), issue => issue.Code is "INSTRUMENT_INCOMPATIBLE" or "MISSING_INSTRUMENT");
    }

    [Fact]
    public void Unknown_algorithm_retains_its_explicit_potential_dependency_and_opaque_removal_blocker()
    {
        var draft = MeanDraft();
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        draft = draft with { Measure = [metric with { Metric = metric.Metric with { Source = Assert.IsType<AlgorithmSource>(metric.Metric.Source) with { AlgorithmId = "Vendor.Algorithm", InstrumentSlot = "FIRST" } } }] };
        var node = AuthoringDependencyIndex.Build(draft).Nodes.Single(node => node.NodeId == metric.NodeId);
        Assert.True(node.IsOpaque);
        Assert.Equal(["FIRST"], node.InstrumentSlots);
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(draft));
        Assert.Contains(AuthoringInstrumentUsage.DescribeSlotUsage(draft, "FIRST"), usage => usage.Contains("Mean", StringComparison.Ordinal));
        Assert.False(AuthoringInstrumentCatalog.CanReplace(draft, "FIRST", draft.Instruments[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_link_targets_resolve_their_ancestor_links_before_containment_checks(bool escape)
    {
        var home = MockPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(MockType, out var adapter));
        var package = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        var payload = Path.Combine(package, adapter.AssemblyFile);
        var actualDirectory = escape ? Path.Combine(_root, "external-payloads") : Path.Combine(package, "contained-payloads");
        Directory.CreateDirectory(actualDirectory);
        File.Move(payload, Path.Combine(actualDirectory, "actual.dll"));
        Directory.CreateSymbolicLink(Path.Combine(package, "payload-link"), actualDirectory);
        File.CreateSymbolicLink(payload, Path.Combine("payload-link", "actual.dll"));

        var availability = adapter.Availability(home);

        Assert.Equal(!escape, availability.Available);
        if (escape)
        {
            Assert.Contains("outside", availability.Reason!);
            Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(new("MOCK", MockType, "MOCK::0"), home));
        }
        else Assert.IsType<MockDmmInstrument>(AuthoringInstrumentCatalog.Create(new("MOCK", MockType, "MOCK::0"), home));
    }

    [Fact]
    public void Cyclic_payload_link_chain_is_unavailable_instead_of_recursing_indefinitely()
    {
        var home = MockPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(MockType, out var adapter));
        var package = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        var payload = Path.Combine(package, adapter.AssemblyFile);
        File.Delete(payload);
        File.CreateSymbolicLink(payload, "cycle-two.dll");
        File.CreateSymbolicLink(Path.Combine(package, "cycle-two.dll"), adapter.AssemblyFile);
        var availability = adapter.Availability(home);
        Assert.False(availability.Available);
        Assert.Contains("cycle", availability.Reason!);
    }

    [Fact]
    public void Payload_link_parent_navigation_applies_after_target_ancestor_resolution()
    {
        var home = MockPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(MockType, out var adapter));
        var package = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        var payload = Path.Combine(package, adapter.AssemblyFile);
        var external = Path.Combine(_root, "external");
        Directory.CreateDirectory(Path.Combine(external, "child"));
        File.Copy(payload, Path.Combine(package, "actual.dll"));
        File.Move(payload, Path.Combine(external, "actual.dll"));
        Directory.CreateSymbolicLink(Path.Combine(package, "escape"), Path.Combine(external, "child"));
        File.CreateSymbolicLink(payload, Path.Combine("escape", "..", "actual.dll"));
        var availability = adapter.Availability(home);
        Assert.False(availability.Available);
        Assert.Contains("outside", availability.Reason!);
    }

    private OpenTapHome LibraryHome()
    {
        var home = new OpenTapHome(Path.Combine(_root, "library-home"));
        var directory = Path.Combine(home.Root, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        Directory.CreateDirectory(directory);
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" })
            File.Copy(Path.Combine(PublishedLibraryFixture.PackageRoot, file), Path.Combine(home.Root, file), overwrite: true);
        File.Copy(Path.Combine(PublishedLibraryFixture.PackageRoot, "package.xml"), Path.Combine(directory, "package.xml"), overwrite: true);
        Assert.Contains(AuthoringInstrumentCatalog.Discover(home), adapter => adapter.TypeId == PhysicalType);
        return home;
    }

    private OpenTapHome MockPayloadHome()
    {
        var home = new OpenTapHome(Path.Combine(_root, "home"));
        var directory = Path.Combine(home.Root, "Packages", "HardwareTest Basic");
        Directory.CreateDirectory(directory);
        var files = new[] { "HardwareTest.OpenTap.Plugins.Basic.dll", "HardwareTest.Core.dll" };
        new XElement("Package", new XAttribute("Name", "HardwareTest Basic"),
            new XElement("Files", files.Select(file => new XElement("File", new XAttribute("Path", file)))))
            .Save(Path.Combine(directory, "package.xml"));
        foreach (var file in files) File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(directory, file));
        return home;
    }

    private AuthoringWorkspaceViewModel CreationWorkspace()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "dirs.proj"))) repo = repo.Parent;
        File.Copy(Path.Combine(repo!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_root);
        vm.InitializePlan(new("typed") { Instruments = [] });
        return vm;
    }

    private static ProgramDraft MockDraft() => new("typed", new ProgramSidecar { DisplayName = "Typed" },
        [new InstrumentRef("DMM", MockType, "MOCK::0")],
        [new IdentitySetup("DMM")],
        [new MetricNode(new("Acquire", "VDC", PresentationDisplayRoles.Timeseries, "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())))],
        new CleanupPolicy(true, "DMM"));

    private static ProgramDraft MeanDraft() => new("typed", new ProgramSidecar { DisplayName = "Typed" },
        [new InstrumentRef("FIRST", MockType, "MOCK::0"), new InstrumentRef("SECOND", MockType, "MOCK::1")],
        [new IdentitySetup("FIRST")],
        [new MetricNode(new("Mean", "VDC.mean", PresentationDisplayRoles.Scalar, "V", new LimitSpec(null, null, 1.2), null,
            new AlgorithmSource(AuthoringFunctionIds.BasicMeanGte, [], new Dictionary<string, string> { ["Threshold"] = "1.2" }) { InstrumentSlot = "SECOND" }))],
        new CleanupPolicy(true, ["SECOND"], true));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
