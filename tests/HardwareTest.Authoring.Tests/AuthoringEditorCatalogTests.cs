using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringEditorCatalogTests
{
    [Fact]
    public void Formula_completions_offer_allowlist_and_channels_but_never_fft()
    {
        var items = FormulaCatalog.Completions(["VDC", "VDC.mean"]);
        Assert.Contains(items, item => item.Name == "mean" && item.Packs);
        Assert.Contains(items, item => item.Name == "filter" && item.Packs);
        Assert.Contains(items, item => item.Name == "filtfilt" && item.Packs);
        Assert.Contains(items, item => item.Name == "std" && !item.Packs);
        Assert.Contains(items, item => item.Kind == "channel" && item.Name == "VDC");
        Assert.DoesNotContain(items, item => FormulaCatalog.ReservedUnknown.Contains(item.Name));
        FormulaParser.Parse("mean(VDC)");
        var reserved = Assert.Throws<AuthoringWorkspaceException>(() => FormulaParser.Parse("fft(VDC)"));
        Assert.Contains(AuthoringCompileCodes.FormulaParse, reserved.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeSave_distinguishes_pack_from_preview_only()
    {
        Assert.Contains("Mean GTE", FormulaLowerer.DescribeSave("mean(VDC)", new LimitSpec(null, null, 1.2)), StringComparison.Ordinal);
        Assert.Contains("Apply Transfer Function", FormulaLowerer.DescribeSave("filter([0.5 0.5],[1],VDC)", null), StringComparison.Ordinal);
        Assert.Contains("Apply Transfer Function", FormulaLowerer.DescribeSave("filtfilt([0.5 0.5],[1],VDC)", null), StringComparison.Ordinal);
        Assert.Contains(AuthoringCompileCodes.FormulaNoLower, FormulaLowerer.DescribeSave("std(VDC)", null), StringComparison.Ordinal);
        Assert.Contains(AuthoringCompileCodes.FormulaParse, FormulaLowerer.DescribeSave("fft(VDC)", null), StringComparison.Ordinal);
    }

    [Fact]
    public void Display_roles_and_tf_methods_are_closed_lists()
    {
        Assert.Equal(
            [PresentationRoles.Scalar, PresentationRoles.Passband, PresentationRoles.Timeseries, PresentationRoles.Timing],
            AuthoringEditorCatalog.DisplayRoles);
        Assert.Equal(["filter", "filtfilt"], AuthoringEditorCatalog.TfMethods);
        Assert.Equal(["V", "ms", "%"], AuthoringEditorCatalog.YUnits);
        Assert.Equal(["status", "certification"], AuthoringEditorCatalog.ReportKinds);
        Assert.Equal(["dut", "stationHealth"], AuthoringEditorCatalog.ProgramKinds);
        Assert.Equal(["warn", "block"], AuthoringEditorCatalog.StationHealthGates);
    }
}

public sealed class AuthoringWorkspaceCatalogTests
{
    [Fact]
    public void Report_and_program_kind_options_union_defaults_manifest_and_sidecars()
    {
        var manifest = new AuthoringManifest
        {
            Catalogs = new AuthoringWorkspaceCatalogs
            {
                ReportKinds = ["traceability"],
                ProgramKinds = ["incomingInspect"],
            },
        };
        var selected = AuthoringRecipeCatalog.CreateProgram("union");
        selected.Sidecar.ReportKinds = ["status", "mes"];
        selected.Sidecar.DefaultReportKind = "mes";
        selected.Sidecar.ProgramKind = "incomingInspect";
        var other = AuthoringRecipeCatalog.CreateProgram("other");
        other.Sidecar.ReportKinds = ["certification"];
        other.Sidecar.ProgramKind = "stationHealth";

        var reports = AuthoringWorkspaceCatalog.ReportKindOptions(manifest, [other, selected], selected);
        Assert.Equal(["status", "certification", "traceability", "mes"], reports);
        var kinds = AuthoringWorkspaceCatalog.ProgramKindOptions(manifest, [other, selected], selected);
        Assert.Equal(["dut", "stationHealth", "incomingInspect"], kinds);
        Assert.Equal(
            ["serial", "partNumber", "revision", "operator", "fixtureId"],
            AuthoringWorkspaceCatalog.RequiredFieldOptions(
                new AuthoringManifest { Catalogs = new AuthoringWorkspaceCatalogs { RequiredFields = ["fixtureId"] } },
                [],
                null));

        var orphan = AuthoringRecipeCatalog.CreateProgram("orphan-default");
        orphan.Sidecar.ReportKinds = ["status"];
        orphan.Sidecar.DefaultReportKind = "lab";
        Assert.Contains(
            "lab",
            AuthoringWorkspaceCatalog.ReportKindOptions(null, [orphan], orphan));
    }

    [Fact]
    public void Y_unit_options_include_metric_units()
    {
        var acquire = AuthoringRecipeCatalog.Apply(
            AuthoringRecipeCatalog.CreateProgram("units"),
            AuthoringRecipeIds.Acquire);
        var metric = Assert.IsType<MetricNode>(Assert.Single(acquire.Measure));
        var draft = acquire with { Measure = [new MetricNode(metric.Metric with { YUnit = "A" })] };
        Assert.Equal(["V", "ms", "%", "A"], AuthoringWorkspaceCatalog.YUnitOptions(draft));
    }
}

public sealed class AuthoringProgramSettingsViewModelTests
{
    [Fact]
    public void Sidecar_report_kinds_and_dut_flags_round_trip_on_the_session()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("sidecar");
        vm.DisplayName = "Board A";
        vm.RequirePartNumber = true;
        vm.ReportCertification = true;
        vm.ProgramKind = "stationHealth";
        vm.RequireStationHealth = true;
        vm.StationHealthGate = "block";
        Assert.Equal("Board A", vm.SelectedProgram!.Sidecar.DisplayName);
        Assert.True(vm.SelectedProgram.Sidecar.RequirePartNumber);
        Assert.True(vm.ReportStatus);
        Assert.True(vm.ReportCertification);
        Assert.Contains("certification", vm.SelectedProgram.Sidecar.ReportKinds!);
        Assert.Equal("stationHealth", vm.ProgramKind);
        Assert.Equal("block", vm.StationHealthGate);
        Assert.Contains("session/DUT/Typst", vm.SidecarHelp, StringComparison.Ordinal);
        Assert.Contains("serial", vm.RequiredFieldChoices.Select(row => row.Id));
        Assert.True(vm.RequiredFieldChoices.Single(row => row.Id == "partNumber").Included);
    }

    [Fact]
    public void Required_fields_sync_known_flags_and_persist_extra_ids()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("required-fields");
        Assert.True(vm.RequireSerial);
        Assert.False(vm.RequirePartNumber);
        vm.RequirePartNumber = true;
        Assert.Contains(RequiredFieldIds.PartNumber, vm.SelectedProgram!.Sidecar.RequiredFields!);
        Assert.True(vm.SelectedProgram.Sidecar.RequirePartNumber);
        vm.NewRequiredField = "fixtureId";
        vm.AddRequiredField();
        Assert.Contains("fixtureId", vm.SelectedProgram.Sidecar.RequiredFields!);
        Assert.Contains(RequiredFieldIds.Serial, vm.SelectedProgram.Sidecar.RequiredFields!);
        Assert.True(vm.SelectedProgram.Sidecar.RequireSerial);
        Assert.True(vm.RequireSerial);
        Assert.Contains("fixtureId", vm.Workspace!.Manifest.Catalogs!.RequiredFields);
        var reloaded = AuthoringWorkspaceLoader.Load(vm.Workspace.Root);
        Assert.Contains("fixtureId", reloaded.Manifest.Catalogs!.RequiredFields);
    }

    [Fact]
    public void Formula_insert_and_visibility_follow_the_selected_source()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("formula-ui");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        Assert.False(vm.HasFormula);
        Assert.True(vm.HasMetricPresentation);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        Assert.True(vm.HasFormula);
        Assert.False(vm.HasTransferFunction);
        Assert.Contains("Mean GTE", vm.FormulaSaveNote, StringComparison.Ordinal);
        vm.InsertFormulaToken("+std(");
        Assert.Contains("+std(", vm.FormulaSource, StringComparison.Ordinal);
        Assert.Contains(AuthoringCompileCodes.FormulaParse, vm.FormulaError, StringComparison.Ordinal);
        vm.FormulaSource = "std(VDC)";
        Assert.Contains(AuthoringCompileCodes.FormulaNoLower, vm.FormulaSaveNote, StringComparison.Ordinal);
        Assert.Contains("VDC", vm.ChannelKeys);
        Assert.Contains(vm.FormulaCompletions, item => item.Name == "mean" && item.Packs);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        Assert.True(vm.HasRepeatEditor);
        Assert.False(vm.HasFormula);
        Assert.False(vm.HasTransferFunction);
        Assert.False(vm.HasMetricPresentation);
        Assert.True(string.IsNullOrEmpty(vm.FormulaSaveNote));
        Assert.True(string.IsNullOrEmpty(vm.MetricInstrumentSlot));
    }

    [Fact]
    public void Instrument_visa_updates_the_named_slot()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("visa-slot");
        Assert.Equal(["DMM"], vm.InstrumentSlots);
        vm.SetInstrumentVisa("DMM", "TCPIP0::10.0.0.5::INSTR");
        Assert.Equal("TCPIP0::10.0.0.5::INSTR", vm.VisaAddress);
        Assert.Equal("TCPIP0::10.0.0.5::INSTR", vm.Instruments[0].VisaAddress);
    }

    [Fact]
    public void Add_report_kind_and_program_kind_are_session_and_workspace_catalogs()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("catalogs");
        Assert.Equal(["status", "certification"], vm.ReportKindOptions);
        vm.NewReportKind = "traceability";
        vm.AddReportKind();
        Assert.Contains("traceability", vm.ReportKindOptions);
        Assert.Contains("traceability", vm.SelectedProgram!.Sidecar.ReportKinds!);
        Assert.True(vm.ReportKindChoices.Single(row => row.Id == "traceability").Included);
        Assert.Equal(string.Empty, vm.NewReportKind);
        Assert.Contains("traceability", vm.Workspace!.Manifest.Catalogs!.ReportKinds);

        vm.NewProgramKind = "incomingInspect";
        vm.AddProgramKind();
        Assert.Equal("incomingInspect", vm.ProgramKind);
        Assert.Contains("incomingInspect", vm.ProgramKindOptions);
        Assert.Contains("incomingInspect", vm.Workspace.Manifest.Catalogs.ProgramKinds);

        var reloaded = AuthoringWorkspaceLoader.Load(vm.Workspace.Root);
        Assert.Contains("traceability", reloaded.Manifest.Catalogs!.ReportKinds);
        Assert.Contains("incomingInspect", reloaded.Manifest.Catalogs.ProgramKinds);
        Assert.DoesNotContain("status", reloaded.Manifest.Catalogs.ReportKinds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("dut", reloaded.Manifest.Catalogs.ProgramKinds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Program_kind_and_default_report_ignore_empty_or_unlisted_values()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("guard-kinds");
        vm.ProgramKind = "stationHealth";
        vm.ProgramKind = null!;
        vm.ProgramKind = "   ";
        Assert.Equal("stationHealth", vm.ProgramKind);
        Assert.Equal(["status"], vm.IncludedReportKinds);
        vm.DefaultReportKind = "certification";
        Assert.Equal("status", vm.DefaultReportKind);
        vm.ReportCertification = true;
        vm.DefaultReportKind = "certification";
        Assert.Equal("certification", vm.DefaultReportKind);
        Assert.Equal(["status", "certification"], vm.IncludedReportKinds);
    }

    [Fact]
    public void Add_instrument_slot_is_selectable_and_rejects_duplicates()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-add");
        Assert.Equal(["DMM"], vm.InstrumentSlots);
        vm.NewInstrumentSlot = "SCOPE";
        vm.NewInstrumentVisa = "MOCK::SCOPE0";
        Assert.True(vm.CanAddInstrumentSlot);
        vm.AddInstrumentSlot();
        Assert.Equal(["DMM", "SCOPE"], vm.InstrumentSlots);
        Assert.Equal("SCOPE", vm.SelectedInstrumentSlot);
        Assert.Equal("MOCK::SCOPE0", vm.SelectedInstrumentVisa);
        Assert.Contains("SCOPE", vm.Workspace!.Manifest.Catalogs!.InstrumentSlotNames);
        Assert.Equal(string.Empty, vm.NewInstrumentSlot);
        Assert.False(vm.CanAddInstrumentSlot);

        vm.NewInstrumentSlot = "scope";
        var duplicate = Assert.Throws<AuthoringWorkspaceException>(vm.AddInstrumentSlot);
        Assert.Contains("already exists", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProgram_seeds_workspace_catalog_slots()
    {
        var vm = OpenEmpty();
        vm.Workspace!.Manifest.Catalogs = new AuthoringWorkspaceCatalogs
        {
            InstrumentSlotNames = ["DMM", "PSU"],
        };
        vm.CreateProgram("seeded-slots");
        Assert.Contains("DMM", vm.InstrumentSlots);
        Assert.Contains("PSU", vm.InstrumentSlots);
    }

    [Fact]
    public void Transfer_function_method_is_a_closed_choice()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("tf-ui");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
        Assert.True(vm.HasTransferFunction);
        Assert.Contains("filter", vm.TfMethodOptions);
        vm.TfMethod = "filtfilt";
        Assert.Equal("filtfilt", vm.TfMethod);
        vm.TfMethod = "fft";
        Assert.Equal("filtfilt", vm.TfMethod);
        Assert.Contains("VDC", vm.ChannelKeys);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        Assert.True(vm.HasRepeatEditor);
        Assert.False(vm.HasTransferFunction);
        Assert.True(string.IsNullOrEmpty(vm.MetricInstrumentSlot));
    }

    [Fact]
    public void Prompt_message_edits_the_selected_setup_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("prompt-ui");
        vm.ApplyRecipe(AuthoringRecipeIds.Prompt);
        var prompt = vm.SequenceItems.Single(row => row.Label == "Operator Prompt");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(prompt));
        Assert.True(vm.HasSetupEditor);
        vm.PromptMessage = "Seat the fixture.";
        Assert.Equal("Seat the fixture.", Assert.IsType<OperatorPromptSetup>(vm.SelectedSetup).Message);
        Assert.Equal(string.Empty, vm.SetupInstrumentSlot);
    }

    [Fact]
    public void Identity_and_cleanup_slots_edit_the_selected_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots");
        var identity = vm.SequenceItems.Single(row => row.Label == "Identity Check");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(identity));
        Assert.Equal("DMM", vm.SetupInstrumentSlot);
        vm.SetupInstrumentSlot = "DMM";
        Assert.Equal("DMM", Assert.IsType<IdentitySetup>(vm.SelectedSetup).InstrumentSlot);

        var cleanup = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Cleanup);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(cleanup));
        Assert.True(vm.HasCleanupEditor);
        Assert.True(vm.IncludeSafeShutdown);
        Assert.Equal("DMM", vm.CleanupInstrumentSlot);
        vm.IncludeSafeShutdown = false;
        Assert.False(vm.SelectedProgram!.Cleanup.IncludeSafeShutdown);
        Assert.Contains("Cleanup skipped", vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Cleanup).Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_can_include_multiple_slots_and_measure_slots()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("multi-cleanup");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.MetricInstrumentSlot = "SCOPE";
        Assert.Equal("SCOPE", Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source).InstrumentSlot);
        var cleanup = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Cleanup);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(cleanup));
        Assert.Equal(["DMM"], vm.SelectedProgram!.Cleanup.InstrumentSlots);
        vm.IncludeMeasureSlots = true;
        Assert.True(vm.SelectedProgram.Cleanup.IncludeMeasureSlots);
        Assert.True(vm.SelectedProgram.Sidecar.IncludeMeasureSlots);
        Assert.Equal(["DMM"], vm.SelectedProgram.Cleanup.InstrumentSlots);
        Assert.Equal(["DMM", "SCOPE"], AuthoringCleanup.ResolveSlots(vm.SelectedProgram));
        Assert.False(vm.CleanupSlotChoices.Single(row => row.Id == "SCOPE").Included);
        vm.SetCleanupSlotIncluded("SCOPE", true);
        Assert.Equal(["DMM", "SCOPE"], vm.SelectedProgram.Cleanup.InstrumentSlots);
        Assert.True(vm.CleanupSlotChoices.Single(row => row.Id == "SCOPE").Included);
        vm.CleanupInstrumentSlot = "PSU";
        Assert.Equal("PSU", vm.CleanupInstrumentSlot);
        Assert.Equal(["PSU", "SCOPE"], vm.SelectedProgram.Cleanup.InstrumentSlots);
        vm.IncludeMeasureSlots = false;
        vm.SetCleanupSlotIncluded("PSU", false);
        vm.SetCleanupSlotIncluded("SCOPE", false);
        Assert.Empty(vm.SelectedProgram.Cleanup.InstrumentSlots);
        Assert.True(vm.SelectedProgram.Cleanup.IncludeSafeShutdown);
        Assert.Empty(AuthoringCleanup.ResolveSlots(vm.SelectedProgram));
        vm.IncludeMeasureSlots = true;
        vm.SetCleanupSlotIncluded("DMM", true);
        vm.SetCleanupSlotIncluded("SCOPE", true);
        vm.ApplyRecipe(AuthoringRecipeIds.Shutdown);
        Assert.Equal(["DMM", "SCOPE"], vm.SelectedProgram.Cleanup.InstrumentSlots);
        Assert.True(vm.SelectedProgram.Cleanup.IncludeMeasureSlots);
    }

    private static AuthoringWorkspaceViewModel OpenEmpty()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-editcat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
        var schema = Path.Combine(src, "authoring.schema.json");
        if (File.Exists(schema))
        {
            File.Copy(schema, Path.Combine(dest, "authoring.schema.json"));
        }

        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(dest);
        return vm;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }
}
