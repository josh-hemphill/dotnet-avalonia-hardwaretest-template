using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringPlanInitializationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-initialize-" + Guid.NewGuid().ToString("N"));
    public AuthoringPlanInitializationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private PlanInitializationRequest Request(string id = "new-plan") => new(id) { WorkspaceRoot = _root };

    [Fact]
    public void Empty_product_creation_is_durable_incomplete_and_never_inserts_mock_or_compiled_files()
    {
        var result = new AuthoringPlanInitializer().Create(Request() with { DisplayName = "Device checks" });
        Assert.Empty(result.Draft.Instruments); Assert.Empty(result.Draft.Setup); Assert.Empty(result.Draft.Measure);
        Assert.False(result.Draft.Cleanup.IncludeSafeShutdown);
        Assert.Contains(result.Issues, issue => issue.Code == "EMPTY_MEASURE");
        var document = new AuthoringDocumentStore(_root).Load("new-plan").Document!;
        Assert.True(document.RequiresCompilation); Assert.Equal("Device checks", document.Sidecar.DisplayName);
        Assert.True(document.Sidecar.RequireSerial); Assert.Contains(RequiredFieldIds.Serial, document.Sidecar.RequiredFields!);
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.EndsWith("new-plan.authoring.json", result.DestinationPath);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("CON")]
    [InlineData("workspace")]
    [InlineData("bad/name")]
    [InlineData("bad name")]
    [InlineData("bad.")]
    public void Invalid_ID_never_creates_files(string id)
    {
        Assert.Throws<ArgumentException>(() => new AuthoringPlanInitializer().Create(Request(id)));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void Duplicate_session_ID_invalid_name_and_outside_destination_are_rejected_without_writes()
    {
        var initializer = new AuthoringPlanInitializer();
        Assert.Throws<AuthoringWorkspaceException>(() => initializer.Create(Request() with { ExistingPlanIds = ["NEW-PLAN"] }));
        Assert.Throws<ArgumentException>(() => initializer.Create(Request() with { DisplayName = "\n" }));
        Assert.Throws<ArgumentException>(() => initializer.Create(Request() with { DestinationPath = Path.Combine(Path.GetDirectoryName(_root)!, "escape.authoring.json") }));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void Direct_creation_rejects_existing_compiled_workspace_ID_without_caller_inventory()
    {
        Workspace();
        var before = File.ReadAllBytes(Path.Combine(_root, "sample.TapPlan"));
        Assert.Throws<AuthoringWorkspaceException>(() => new AuthoringPlanInitializer().Create(Request("SAMPLE")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "sample.TapPlan")));
        Assert.False(new AuthoringDocumentStore(_root).Load("SAMPLE").Exists);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Case_equivalent_existing_file_or_directory_is_never_overwritten(bool directory)
    {
        var path = new AuthoringDocumentStore(_root).GetDocumentPath("NEW-PLAN");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (directory) Directory.CreateDirectory(path); else File.WriteAllText(path, "external bytes");
        Assert.Throws<IOException>(() => new AuthoringPlanInitializer().Create(Request()));
        if (directory) Assert.True(Directory.Exists(path)); else Assert.Equal("external bytes", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Competitor_after_preflight_preserves_exact_external_bytes_and_removes_only_owned_staging(bool equivalentCase)
    {
        var destination = new AuthoringDocumentStore(_root).GetDocumentPath(equivalentCase ? "NEW-PLAN" : "new-plan");
        byte[] bytes = [0, 2, 255, 13, 10];
        var writer = new AuthoringAtomicWriter(beforeCreate: () => File.WriteAllBytes(destination, bytes));
        Assert.Throws<IOException>(() => new AuthoringPlanInitializer(writer).Create(Request()));
        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Compiled_competitor_after_preflight_rejects_creation_and_preserves_its_bytes()
    {
        Workspace(); var competing = Path.Combine(_root, "NEW-PLAN.TapPlan");
        var writer = new AuthoringAtomicWriter(beforeCreate: () => File.WriteAllText(competing, "external compiled bytes"));
        Assert.Throws<AuthoringWorkspaceException>(() => new AuthoringPlanInitializer(writer).Create(Request()));
        Assert.Equal("external compiled bytes", File.ReadAllText(competing));
        Assert.False(new AuthoringDocumentStore(_root).Load("new-plan").Exists);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "authoring-drafts")));
    }

    [Fact]
    public void Explicit_real_hardware_with_missing_package_persists_without_substituting_demo_resources()
    {
        var type = AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "VISA DMM").TypeId;
        var result = new AuthoringPlanInitializer().Create(Request() with
        {
            StartingPoint = PlanStartingPoint.VoltageTask,
            Instruments = [new("BENCH", type, "TCPIP::192.0.2.1::INSTR")],
            IdentityInstrumentSlot = "BENCH",
            Home = new OpenTapHome(Path.Combine(_root, "missing-home"))
        });
        Assert.Equal(type, Assert.Single(result.Draft.Instruments).TypeId);
        Assert.Contains(result.Issues, issue => issue.Code == "INSTRUMENT_UNAVAILABLE");
        Assert.Equal(["BENCH"], result.Draft.Cleanup.InstrumentSlots);
        Assert.Equal("BENCH", Assert.Single(result.Draft.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        var reopened = new AuthoringDocumentStore(_root).Load("new-plan").Document!.ToDraft();
        Assert.Equal(type, Assert.Single(reopened.Instruments).TypeId);
    }

    [Fact]
    public void VM_opens_missing_dependencies_as_saved_issues_and_reopens_the_chosen_real_resource()
    {
        Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.StopRecovery();
        vm.OpenTapHomeOverride = Path.Combine(_root, "missing-home");
        var type = AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "VISA DMM").TypeId;
        vm.InitializePlan(Request() with { StartingPoint = PlanStartingPoint.VoltageTask, Instruments = [new("BENCH", type, "TCPIP::192.0.2.1::INSTR")] });
        Assert.False(vm.HasUnsavedChanges); Assert.Contains(vm.EditingIssues, issue => issue.PlanId == "new-plan" && issue.Code == "INSTRUMENT_UNAVAILABLE");
        vm.Open(_root); vm.StopRecovery(); vm.SelectProgram("new-plan");
        Assert.Equal(type, Assert.Single(vm.SelectedProgram!.Instruments).TypeId);
        Assert.Contains(vm.EditingIssues, issue => issue.PlanId == "new-plan" && issue.Code == "INSTRUMENT_UNAVAILABLE");
    }

    [Fact]
    public void Task_choices_allow_explicitly_omitted_template_hardware_and_measurement_and_synchronize_operator_requirements()
    {
        var result = new AuthoringPlanInitializer().Create(Request() with
        {
            StartingPoint = PlanStartingPoint.DemoVoltageTask,
            UseTemplateHardware = false,
            IncludeTemplateMeasurement = false,
            RequireSerial = false,
            RequiredOperatorFields = ["partNumber", "fixtureId"]
        });
        Assert.Empty(result.Draft.Instruments); Assert.Empty(result.Draft.Measure);
        Assert.False(result.Draft.Sidecar.RequireSerial); Assert.True(result.Draft.Sidecar.RequirePartNumber);
        Assert.Contains("DemoVoltageTask", result.Review); Assert.Contains("fixtureId", result.Review);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("staged")]
    [InlineData("interrupted")]
    public void Cancellation_and_interrupted_staging_leave_no_plan_or_temporary_files(string timing)
    {
        using var cancellation = new CancellationTokenSource();
        if (timing == "before") cancellation.Cancel();
        var writer = new AuthoringAtomicWriter(beforeCreate: () =>
        {
            if (timing == "interrupted") throw new IOException("interrupted publication");
            cancellation.Cancel();
        });
        var initializer = new AuthoringPlanInitializer(writer);
        if (timing == "interrupted") Assert.Throws<IOException>(() => initializer.Create(Request(), cancellation.Token));
        else Assert.Throws<OperationCanceledException>(() => initializer.Create(Request(), cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shutdown_review_matches_selected_policy_and_compiled_cleanup(bool enabled)
    {
        var result = new AuthoringPlanInitializer().Create(Request() with
        {
            StartingPoint = PlanStartingPoint.DemoVoltageTask,
            IdentityInstrumentSlot = "DMM",
            IncludeSafeShutdown = enabled
        });
        Assert.Equal(enabled, result.Draft.Cleanup.IncludeSafeShutdown); Assert.Equal(["DMM"], result.Draft.Cleanup.InstrumentSlots);
        Assert.Equal(enabled ? true : (bool?)null, result.Draft.Sidecar.IncludeSafeShutdown);
        var stored = new AuthoringDocumentStore(_root).Load("new-plan").Document!.ToDraft();
        Assert.Equal(enabled, stored.Cleanup.IncludeSafeShutdown); Assert.Equal(["DMM"], stored.Cleanup.InstrumentSlots);
        var path = Path.Combine(_root, "new-plan.TapPlan"); new PlanCompiler().Save(stored, path);
        Assert.Equal(enabled, XDocument.Load(path).Descendants("TestStep").Any(step => ((string?)step.Attribute("type"))?.Contains("SafeShutdownStep", StringComparison.Ordinal) == true));
        Assert.Equal(enabled, new PlanCompiler().Load(path).Cleanup.IncludeSafeShutdown);
        var shutdown = result.Review.Split('\n').Single(line => line.Contains("shutdown:", StringComparison.Ordinal));
        if (enabled) Assert.Contains("DMM", shutdown);
        else { Assert.Contains("shutdown: disabled", shutdown); Assert.DoesNotContain("DMM", shutdown); }
    }

    [Fact]
    public void Explicit_demo_template_has_unique_stable_IDs_and_selected_setup_shutdown_and_output()
    {
        var initializer = new AuthoringPlanInitializer();
        var request = Request() with
        {
            StartingPoint = PlanStartingPoint.DemoVoltageTask,
            IdentityInstrumentSlot = "DMM",
            FixtureConfirmation = "Fixture seated?",
            FixtureInputField = "fixtureId",
            RequiredOperatorFields = ["fixtureId"],
            Measurement = new(AuthoringRecipeIds.MeanGte, "DMM") { ChannelKey = "rail.mean", ThresholdText = "1.1", SampleCount = "8" }
        };
        var first = initializer.Create(request).Draft;
        var second = initializer.Create(request with { PlanId = "second" }).Draft;
        Assert.Equal("Mock DMM", AuthoringInstrumentCatalog.All.Single(adapter => adapter.TypeId == Assert.Single(first.Instruments).TypeId).DisplayName);
        Assert.Equal("DMM", Assert.Single(first.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.Equal(["DMM"], first.Cleanup.InstrumentSlots); Assert.True(first.Cleanup.IncludeSafeShutdown);
        Assert.Equal("rail.mean", Assert.IsType<MetricNode>(Assert.Single(first.Measure)).Metric.ChannelKey);
        var ids = AuthoringDependencyIndex.Build(first).Nodes.Select(node => node.NodeId).ToArray();
        Assert.DoesNotContain(Guid.Empty, ids); Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Empty(ids.Intersect(AuthoringDependencyIndex.Build(second).Nodes.Select(node => node.NodeId)));
        Assert.Equal(ids, AuthoringDependencyIndex.Build(new AuthoringDocumentStore(_root).Load("new-plan").Document!.ToDraft()).Nodes.Select(node => node.NodeId));
    }

    [Fact]
    public void Missing_hardware_and_unfinished_measurement_text_survive_reopen_as_actionable_issues()
    {
        var request = Request() with
        {
            StartingPoint = PlanStartingPoint.VoltageTask,
            Measurement = new(AuthoringRecipeIds.MeanGte, "") { SampleCount = "unfinished", ThresholdText = "1e" },
            Home = new OpenTapHome(Path.Combine(_root, "missing-home"))
        };
        var draft = new AuthoringPlanInitializer().Create(request).Draft;
        Assert.Empty(draft.Instruments);
        var reopened = new AuthoringDocumentStore(_root).Load("new-plan").Document!.ToDraft();
        Assert.True(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(reopened)));
        Assert.Contains(AuthoringIssueService.GetIssues(reopened), issue => issue.Code == "MISSING_INSTRUMENT_BINDING");
        Assert.Contains(AuthoringIssueService.GetIssues(reopened), issue => issue.Field == "Threshold");
        Assert.Contains("unfinished", reopened.AuthoringState.IncompleteNumericText.Values);
        Assert.Contains("1e", reopened.AuthoringState.IncompleteNumericText.Values);
    }

    [Fact]
    public void Durable_VM_creation_opens_saved_document_with_selection_history_and_reopens_source_without_compilation()
    {
        Workspace();
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.StopRecovery();
        vm.InitializePlan(Request() with { StartingPoint = PlanStartingPoint.VoltageTask, Measurement = new(AuthoringRecipeIds.Acquire, "") { SampleCount = "pending" } });
        Assert.Equal("new-plan", vm.SelectedProgram!.PlanId); Assert.NotNull(vm.SelectedSequence?.NodeId);
        Assert.False(vm.HasUnsavedChanges); Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanUndo);
        Assert.Equal("pending", vm.MetricSettingRows.Single(row => row.Key == "SampleCount").Value);
        var id = vm.SelectedSequence!.NodeId;
        vm.DisplayName = "changed"; Assert.True(vm.CanUndo); vm.Undo(); Assert.Equal("new-plan", vm.DisplayName); Assert.False(vm.HasUnsavedChanges);
        vm.Redo(); Assert.Equal("changed", vm.DisplayName); vm.Undo();
        vm.Open(_root); vm.StopRecovery(); vm.SelectProgram("new-plan");
        Assert.Equal(id, Assert.Single(vm.SelectedProgram!.Measure).NodeId);
        Assert.Contains("pending", vm.SelectedProgram.AuthoringState.IncompleteNumericText.Values);
        vm.SelectMeasure(0); Assert.Equal("pending", vm.MetricSettingRows.Single(row => row.Key == "SampleCount").Value);
        vm.SetMetricSetting("SampleCount", "8"); Assert.Empty(vm.SelectedProgram.AuthoringState.IncompleteNumericText);
        vm.Undo();
        Assert.False(File.Exists(Path.Combine(_root, "new-plan.TapPlan")));
        vm.CreateProgram("legacy"); Assert.Empty(vm.SelectedProgram!.Instruments); Assert.Empty(vm.SelectedProgram.Setup);
        Assert.True(vm.HasUnsavedChanges); Assert.False(new AuthoringDocumentStore(_root).Load("legacy").Exists);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Included_empty_draft_requires_a_measurement_before_deployment_even_after_explicit_compile(int loopDepth)
    {
        Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.StopRecovery();
        vm.InitializePlan(loopDepth == 0 ? Request() : Request() with
        {
            StartingPoint = PlanStartingPoint.DemoVoltageTask,
            IdentityInstrumentSlot = "DMM",
            Measurement = new(AuthoringRecipeIds.MeanGte, "DMM")
        });
        if (loopDepth > 0)
        {
            vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
            for (var level = 1; level < loopDepth; level++)
                vm.ReplaceSelected(vm.SelectedProgram! with { Measure = [new RepeatNode(2, vm.SelectedProgram!.Measure)] });
            Assert.True(vm.SaveAll().Succeeded); Assert.True(vm.CanPack);
            var metricId = AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram!.Measure).Single().ChannelKey;
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
            vm.RemoveSelectedSequence();
            Assert.Empty(AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram.Measure));
            vm.Undo(); Assert.Equal(metricId, AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram.Measure).Single().ChannelKey);
            vm.Redo(); Assert.Empty(AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram.Measure));
            Assert.True(vm.SaveAll().Succeeded);
            vm.Open(_root); vm.StopRecovery(); vm.SelectProgram("new-plan");
            Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure));
        }
        Assert.False(vm.CanPack);
        Assert.Contains(vm.EditingIssues, issue => issue.Code == "EMPTY_MEASURE" && issue.PlanId == "new-plan");
        var source = new AuthoringDocumentStore(_root).GetDocumentPath("new-plan"); var before = File.ReadAllBytes(source);
        Assert.Throws<PackPreflightException>(() => vm.Pack(Path.Combine(_root, "output")));
        Assert.Equal(before, File.ReadAllBytes(source)); Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        var home = new OpenTapHomeBootstrapper().Bootstrap(vm.Workspace!, new BootstrapOptions { HomeDirectory = Path.Combine(_root, "home"), Offline = true });
        var request = AuthoringBuildService.CaptureSaved(vm.Workspace!, new PackOptions { Home = home, Offline = true });
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, Path.Combine(_root, "output")));
        Assert.Contains("BUILD_INCOMPLETE", error.Message); Assert.Equal(before, File.ReadAllBytes(source));
        var cliError = new StringWriter();
        Assert.Equal(1, AuthoringCli.Run(["--pack", _root, "--out", Path.Combine(_root, "cli-output"), "--opentap-home", home.Root], new StringWriter(), cliError));
        Assert.Contains("BUILD_INCOMPLETE", cliError.ToString()); Assert.False(Directory.Exists(Path.Combine(_root, "cli-output")));
        vm.Apply(); Assert.False(vm.CanPack);
        vm.Open(_root); vm.StopRecovery(); vm.SelectProgram("new-plan"); Assert.False(vm.CanPack);
        vm.OpenTapHomeOverride = home.Root; vm.SetBuildProgramIncluded("new-plan", false); Assert.True(vm.SaveAll().Succeeded); Assert.True(vm.CanPack);
        vm.Pack(Path.Combine(_root, "excluded-output"), new PackOptions { Home = home, Offline = true });
        Assert.DoesNotContain(vm.LastBuildReceipt!.Sources, source => source.PlanId == "new-plan");
        vm.NewInstrumentSlot = "BENCH"; vm.NewInstrumentTypeId = AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "Mock DMM").TypeId;
        vm.NewInstrumentVisa = "MOCK::BENCH"; vm.AddInstrumentSlot();
        if (loopDepth == 0) vm.ApplyRecipe(AuthoringRecipeIds.Identity);
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte); vm.ApplyRecipe(AuthoringRecipeIds.Shutdown);
        if (loopDepth > 0)
        {
            IReadOnlyList<MeasureNode> corrected = [vm.SelectedProgram!.Measure.OfType<MetricNode>().Single()];
            for (var level = 0; level < loopDepth; level++) corrected = [new RepeatNode(2, corrected)];
            vm.ReplaceSelected(vm.SelectedProgram! with { Measure = corrected });
        }
        vm.SetBuildProgramIncluded("new-plan", true); Assert.True(vm.SaveAll().Succeeded); Assert.True(vm.CanPack);
        vm.Pack(Path.Combine(_root, "corrected-output"), new PackOptions { Home = home, Offline = true });
        Assert.Contains(vm.LastBuildReceipt!.Sources, source => source.PlanId == "new-plan");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Saved_nested_empty_loops_are_rejected_by_CLI_before_publishing(int loopDepth)
    {
        Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.StopRecovery();
        vm.InitializePlan(Request() with { StartingPoint = PlanStartingPoint.DemoVoltageTask, IdentityInstrumentSlot = "DMM" });
        vm.ReplaceSelected(vm.SelectedProgram! with { Measure = [new RepeatNode(2, [])] });
        for (var level = 1; level < loopDepth; level++)
            vm.ReplaceSelected(vm.SelectedProgram! with { Measure = [new RepeatNode(2, vm.SelectedProgram!.Measure)] });
        Assert.True(vm.SaveAll().Succeeded); vm.Apply();
        var home = new OpenTapHomeBootstrapper().Bootstrap(vm.Workspace!, new BootstrapOptions { HomeDirectory = Path.Combine(_root, "home"), Offline = true });
        var output = Path.Combine(_root, "cli-nested-output"); var error = new StringWriter();
        Assert.Equal(1, AuthoringCli.Run(["--pack", _root, "--out", output, "--opentap-home", home.Root], new StringWriter(), error));
        Assert.Contains("BUILD_INCOMPLETE", error.ToString()); Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Legacy_compiled_only_setup_and_cleanup_plan_remains_a_supported_build_input()
    {
        Workspace();
        var path = Path.Combine(_root, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(path) with { Measure = [new RepeatNode(2, [new RepeatNode(2, [])])] };
        Assert.NotEmpty(draft.Setup); Assert.True(draft.Cleanup.IncludeSafeShutdown);
        compiler.Save(draft, path);
        var workspace = AuthoringWorkspaceLoader.Load(_root);
        var home = new OpenTapHomeBootstrapper().Bootstrap(workspace, new BootstrapOptions { HomeDirectory = Path.Combine(_root, "home"), Offline = true });
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true });
        var result = AuthoringBuildService.Execute(request, Path.Combine(_root, "compiled-only-output"));
        Assert.Contains("sample", result.Receipt.IncludedPlans);
        var compiledSource = Assert.Single(result.Receipt.Sources, source => source.PlanId == "sample");
        Assert.Null(compiledSource.SavedRevision); Assert.Null(compiledSource.SourceSha256);
        Assert.False(new AuthoringDocumentStore(_root).Load("sample").Exists);
    }

    [Fact]
    public void Completed_supported_template_compiles_strictly_validates_and_passes_production_compatibility()
    {
        Workspace();
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.StopRecovery();
        vm.InitializePlan(Request() with
        {
            StartingPoint = PlanStartingPoint.DemoVoltageTask,
            IdentityInstrumentSlot = "DMM",
            Measurement = new(AuthoringRecipeIds.MeanGte, "DMM") { ChannelKey = "rail.mean", ThresholdText = "1.1", SampleCount = "8" }
        });
        Assert.True(new AuthoringDocumentStore(_root).Load("new-plan").Document!.RequiresCompilation);
        Assert.DoesNotContain(vm.Workspace!.TapPlanPaths, path => Path.GetFileNameWithoutExtension(path) == "new-plan");
        vm.Apply();
        Assert.False(new AuthoringDocumentStore(_root).Load("new-plan").Document!.RequiresCompilation);
        var path = Assert.Single(vm.Workspace!.TapPlanPaths, path => Path.GetFileNameWithoutExtension(path) == "new-plan");
        Assert.False(PlanContractValidator.Validate([path], new PlanContractOptions { Strict = true }).HasErrors);
        var home = new OpenTapHomeBootstrapper().Bootstrap(vm.Workspace, new BootstrapOptions { HomeDirectory = Path.Combine(_root, "home"), Offline = true });
        var report = new TuiCompatChecker().Compare(vm.Workspace with { TapPlanPaths = [path] }, home, home);
        Assert.False(report.BlocksPack()); Assert.Contains("rail.mean", TuiCompatChecker.RoundTripChannelKeys(path, home));
    }

    private void Workspace()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "dirs.proj"))) directory = Path.GetDirectoryName(directory)!;
        foreach (var file in Directory.GetFiles(Path.Combine(directory, "plans", "opentap"))) File.Copy(file, Path.Combine(_root, Path.GetFileName(file)));
        var workspace = AuthoringWorkspaceLoader.Load(_root); workspace.Manifest.Package.Name = "Initialization tests";
        AuthoringWorkspaceLoader.SaveManifest(_root, workspace.Manifest);
    }
}
