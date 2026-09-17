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
        Assert.False(vm.HasMetricPresentation);
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
