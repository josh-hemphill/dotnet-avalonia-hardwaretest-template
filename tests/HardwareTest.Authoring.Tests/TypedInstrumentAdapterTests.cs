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
    private static string VisaType => typeof(VisaDmmInstrument).FullName!;

    public TypedInstrumentAdapterTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Catalog_constructs_exact_mock_and_real_visa_types_with_the_requested_address()
    {
        var mock = Assert.IsType<MockDmmInstrument>(AuthoringInstrumentCatalog.Create(new("MOCK", MockType, "MOCK::7")));
        var visa = Assert.IsType<VisaDmmInstrument>(AuthoringInstrumentCatalog.Create(new("REAL", VisaType, "TCPIP::192.0.2.1::INSTR")));
        Assert.Equal("MOCK", mock.Name);
        Assert.Equal("MOCK::7", mock.VisaAddress);
        Assert.Equal("MOCK::7", mock.ResourceName);
        Assert.Equal("REAL", visa.Name);
        Assert.Equal("TCPIP::192.0.2.1::INSTR", visa.VisaAddress);
        // Construction must not open a broker session or contact hardware.
        Assert.Throws<InvalidOperationException>(() => visa.ReadVoltage());
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
        Assert.Contains(AuthoringFunctionIds.BasicMeanGte, adapter.CompatibleFunctions);
        Assert.True(adapter.SupportsIdentity);
        Assert.True(AuthoringFunctionCatalog.TryGet(AuthoringFunctionIds.BasicIdentityCheck, out var identity));
        Assert.True(identity.NeedsInstrument);
        Assert.True(adapter.SupportsShutdown);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("9999999999999999999")]
    public void Real_visa_invalid_timeout_is_reported_at_the_authoring_boundary(string timeout)
    {
        var slot = new InstrumentRef("REAL", VisaType, "TCPIP::192.0.2.1::INSTR")
        { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = timeout } };
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(slot));
        Assert.Contains("INSTRUMENT_CONFIGURATION", error.Message);
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
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
        var home = new OpenTapHome(Path.Combine(_root, "home"));
        var packageDirectory = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        Directory.CreateDirectory(packageDirectory);
        if (packagePresent) File.WriteAllText(Path.Combine(packageDirectory, "package.xml"), $"<Package Name=\"{adapter.RequiredPackage}\"/>");
        var availability = adapter.Availability(home);
        Assert.False(availability.Available);
        Assert.Contains(packagePresent ? adapter.AssemblyFile : "metadata", availability.Reason!);
        Assert.Contains(AuthoringIssueService.GetIssues(VisaDraft(), home), issue => issue.Code == "INSTRUMENT_UNAVAILABLE");
        var path = Path.Combine(_root, "typed.TapPlan");
        var sidecar = PlanCompiler.SidecarPath(path);
        File.WriteAllText(path, "existing plan");
        File.WriteAllText(sidecar, "existing sidecar");

        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler(selectedHome: home).Save(VisaDraft(), path));

        Assert.Contains("INSTRUMENT_UNAVAILABLE", error.Message);
        Assert.Equal("existing plan", File.ReadAllText(path));
        Assert.Equal("existing sidecar", File.ReadAllText(sidecar));
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(sidecar + ".saving"));
    }

    [Fact]
    public void Real_visa_save_load_preserves_type_address_and_runtime_binding_without_io()
    {
        var path = Path.Combine(_root, "typed.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(VisaDraft(), path);
        var loaded = compiler.Load(path);
        var slot = Assert.Single(loaded.Instruments);
        Assert.Equal(VisaType, slot.TypeId);
        Assert.Equal("TCPIP::192.0.2.1::INSTR", slot.VisaAddress);
        var plan = TestPlan.Load(path);
        var acquire = Assert.Single(PlanCompiler.FlattenSteps(plan).OfType<AcquireVoltageStep>());
        var visa = Assert.IsType<VisaDmmInstrument>(acquire.Instrument);
        Assert.Equal(slot.SlotName, visa.Name);
        Assert.Equal(slot.VisaAddress, visa.VisaAddress);
        Assert.Equal(1234, visa.IoTimeoutMilliseconds);
        Assert.Equal("1234", slot.Settings["IoTimeoutMilliseconds"]);
        Assert.Throws<InvalidOperationException>(() => visa.ReadVoltage());
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
        compiler.Save(VisaDraft() with { Instruments = [new InstrumentRef("DMM", MockType, "MOCK::0")] }, path);
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
        var draft = VisaDraft();
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        draft = draft with { Measure = [metric with { Metric = metric.Metric with { Limits = new LimitSpec(null, null, 1.2), Source = new MeasureSource("DMM", AuthoringFunctionIds.BasicChannelAverage, new Dictionary<string, string>()) } }] };
        var path = Path.Combine(_root, "typed.TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains("INSTRUMENT_INCOMPATIBLE", error.Message);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
    }

    [Fact]
    public void Capability_compatible_visa_replacement_retargets_mean_identity_and_cleanup()
    {
        var draft = MeanDraft() with { Setup = [new IdentitySetup("FIRST")], Cleanup = new CleanupPolicy(true, ["FIRST"], true) };
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        draft = draft with { Measure = [metric with { Metric = metric.Metric with { Source = Assert.IsType<AlgorithmSource>(metric.Metric.Source) with { InstrumentSlot = "FIRST" } } }] };
        var replacement = new InstrumentRef("REAL", VisaType, "TCPIP::192.0.2.2::INSTR");
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
        Assert.Equal(replacement.VisaAddress, Assert.IsType<VisaDmmInstrument>(runtime.Instrument).VisaAddress);
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
        vm.CreateProgram("typed");
        vm.ReplaceSelected(MeanDraft());
        vm.SelectedInstrumentSlot = "FIRST";
        var impact = vm.PrepareSelectedInstrumentRemoval();
        var current = vm.SelectedProgram!;
        var metric = Assert.IsType<MetricNode>(Assert.Single(current.Measure));
        var changed = field switch
        {
            "algorithm-binding" => current with { Measure = [metric with { Metric = metric.Metric with { Source = Assert.IsType<AlgorithmSource>(metric.Metric.Source) with { InstrumentSlot = "FIRST" } } }] },
            "instrument-type" => current with { Instruments = [current.Instruments[0] with { TypeId = VisaType }, current.Instruments[1]] },
            "instrument-address" => current with { Instruments = [current.Instruments[0] with { VisaAddress = "MOCK::changed" }, current.Instruments[1]] },
            "instrument-slot" => current with { Instruments = [current.Instruments[0] with { SlotName = "RENAMED" }, current.Instruments[1]] },
            "opaque-resource" => current with { Instruments = [current.Instruments[0] with { OpaqueResourceXml = "<Resource><Changed/></Resource>" }, current.Instruments[1]] },
            "cleanup-enabled" => current with { Cleanup = current.Cleanup with { IncludeSafeShutdown = !current.Cleanup.IncludeSafeShutdown } },
            "cleanup-slots" => current with { Cleanup = current.Cleanup with { InstrumentSlots = ["FIRST"] } },
            _ => current with { Cleanup = current.Cleanup with { IncludeMeasureSlots = !current.Cleanup.IncludeMeasureSlots } }
        };
        vm.ReplaceSelected(changed);
        var programs = vm.Programs;

        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyInstrumentRemoval(impact, "SECOND"));

        Assert.Same(programs, vm.Programs);
        Assert.Same(changed, vm.SelectedProgram);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.TapPlan"));
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
    public void Unbound_legacy_algorithm_exposes_actionable_issue_and_refuses_compilation()
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
    public void Declared_real_adapter_home_round_trips_and_default_catalog_keeps_visa_excluded()
    {
        File.WriteAllText(Path.Combine(_root, "authoring.json"), """
            { "schemaVersion": 2, "displayName": "Typed", "plansDirectory": ".",
              "package": { "name": "Typed", "version": "0.1.0" },
              "dependencies": [{ "package": "HardwareTest VISA", "version": "^0.1.0" }],
              "includeTui": false }
            """);
        var workspace = AuthoringWorkspaceLoader.Load(_root);
        var home = new OpenTapHomeBootstrapper().Bootstrap(workspace,
            new BootstrapOptions { HomeDirectory = Path.Combine(_root, "home"), Offline = true });
        var path = Path.Combine(_root, "typed.TapPlan");
        new PlanCompiler(selectedHome: home).Save(VisaDraft(), path);
        workspace = AuthoringWorkspaceLoader.Load(_root);

        Assert.True(AuthoringInstrumentCatalog.DeclaresVisa(workspace));
        Assert.DoesNotContain(VisaType, TuiCompatChecker.ScanCatalog(home).Keys);
        Assert.Contains(VisaType, TuiCompatChecker.ScanCatalog(home, includeVisa: true).Keys);
        var report = new TuiCompatChecker().Compare(workspace, home, home);
        Assert.False(report.BlocksPack(), string.Join("; ", report.RoundTrips.Select(finding => finding.Message)));
        workspace.Manifest.Dependencies.Clear();
        Assert.False(AuthoringInstrumentCatalog.DeclaresVisa(workspace));
        Assert.Contains(new TuiCompatChecker().Compare(workspace, home, home).RoundTrips,
            finding => finding.Code == TuiCompatCodes.TypeUnknown && finding.Message.Contains(VisaType, StringComparison.Ordinal));
        var metadata = Path.Combine(home.Root, "Packages", OpenTapHomeBootstrapper.VisaPackageName, "package.xml");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("HardwareTest VISA", "Unregistered VISA", StringComparison.Ordinal));
        Assert.DoesNotContain(VisaType, TuiCompatChecker.ScanCatalog(home, includeVisa: true).Keys);
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
        Assert.False(adapter.Availability(home).Available);
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
    public void Explicit_visa_creation_requires_an_address_and_unknown_types_are_unavailable()
    {
        var vm = CreationWorkspace();
        vm.Workspace!.Manifest.Dependencies.Add(new AuthoringPackageDependency { Package = OpenTapHomeBootstrapper.VisaPackageName, Version = "^0.1.0" });
        vm.NewInstrumentSlot = "REAL";
        vm.NewInstrumentTypeId = VisaType;
        Assert.False(vm.CanAddInstrumentSlot);
        Assert.Contains("VISA address", vm.NewInstrumentAvailabilityText);
        vm.NewInstrumentVisa = "TCPIP::192.0.2.2::INSTR";
        Assert.True(vm.CanAddInstrumentSlot);
        vm.AddInstrumentSlot();
        var added = vm.SelectedProgram!.Instruments.Single(slot => slot.SlotName == "REAL");
        Assert.Equal(VisaType, added.TypeId);
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
    [InlineData("Ivi.Visa.dll")]
    [InlineData("HardwareTest.OpenTap.Plugins.Visa.dll")]
    public void Selected_visa_home_requires_every_declared_payload_even_when_process_dependencies_are_loaded(string missing)
    {
        var home = VisaPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
        Assert.True(adapter.Availability(home).Available);
        File.Delete(Path.Combine(home.Root, "Packages", adapter.RequiredPackage, missing));
        var unavailable = adapter.Availability(home);
        Assert.False(unavailable.Available);
        Assert.Contains(missing, unavailable.Reason!);
        Assert.Contains("missing", unavailable.Reason!);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(new("REAL", VisaType, "TCPIP::192.0.2.1::INSTR"), home));
    }

    [Theory]
    [InlineData("name-only")]
    [InlineData("undeclared-dependency")]
    [InlineData("empty")]
    [InlineData("traversal")]
    [InlineData("linked-payload")]
    [InlineData("linked-package")]
    public void Selected_home_rejects_incomplete_or_uncontained_declared_payloads(string defect)
    {
        var home = VisaPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
        var directory = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        var metadata = Path.Combine(directory, "package.xml");
        var dependency = Path.Combine(directory, "Ivi.Visa.dll");
        var external = Path.Combine(_root, "external.dll");
        File.WriteAllText(external, "readable external file");
        if (defect == "name-only") File.WriteAllText(metadata, $"<Package Name=\"{adapter.RequiredPackage}\"/>");
        else if (defect == "undeclared-dependency")
        {
            var xml = XDocument.Load(metadata);
            xml.Descendants().Single(element => (string?)element.Attribute("Path") == "Ivi.Visa.dll").Remove();
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
        var home = VisaPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
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
            Assert.Throws<AuthoringWorkspaceException>(() => AuthoringInstrumentCatalog.Create(new("REAL", VisaType, "TCPIP::192.0.2.1::INSTR"), home));
        }
        else Assert.IsType<VisaDmmInstrument>(AuthoringInstrumentCatalog.Create(new("REAL", VisaType, "TCPIP::192.0.2.1::INSTR"), home));
    }

    [Fact]
    public void Cyclic_payload_link_chain_is_unavailable_instead_of_recursing_indefinitely()
    {
        var home = VisaPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
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
        var home = VisaPayloadHome();
        Assert.True(AuthoringInstrumentCatalog.TryGet(VisaType, out var adapter));
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

    private OpenTapHome VisaPayloadHome()
    {
        var home = new OpenTapHome(Path.Combine(_root, "home"));
        var directory = Path.Combine(home.Root, "Packages", OpenTapHomeBootstrapper.VisaPackageName);
        Directory.CreateDirectory(directory);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "dirs.proj"))) repo = repo.Parent;
        File.Copy(Path.Combine(repo!.FullName, "src", "HardwareTest.OpenTap.Plugins.Visa", "package.xml"), Path.Combine(directory, "package.xml"));
        foreach (var file in new[] { "HardwareTest.OpenTap.Plugins.Visa.dll", "HardwareTest.Core.dll", "Ivi.Visa.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(directory, file));
        return home;
    }

    private AuthoringWorkspaceViewModel CreationWorkspace()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "dirs.proj"))) repo = repo.Parent;
        File.Copy(Path.Combine(repo!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_root);
        vm.CreateProgram("typed");
        return vm;
    }

    private static ProgramDraft VisaDraft() => new("typed", new ProgramSidecar { DisplayName = "Typed" },
        [new InstrumentRef("DMM", VisaType, "TCPIP::192.0.2.1::INSTR") { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "1234" } }],
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
