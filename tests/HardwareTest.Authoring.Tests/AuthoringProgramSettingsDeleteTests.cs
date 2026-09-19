using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringProgramSettingsDeleteTests
{
    [Fact]
    public void Built_in_catalog_rows_cannot_be_removed()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("protected");
        Assert.False(vm.RequiredFieldChoices.Single(row => row.Id == RequiredFieldIds.Serial).CanRemove);
        Assert.False(vm.RequiredFieldChoices.Single(row => row.Id == RequiredFieldIds.PartNumber).CanRemove);
        Assert.False(vm.RequiredFieldChoices.Single(row => row.Id == RequiredFieldIds.Revision).CanRemove);
        Assert.False(vm.RequiredFieldChoices.Single(row => row.Id == RequiredFieldIds.Operator).CanRemove);
        Assert.False(vm.ReportKindChoices.Single(row => row.Id == "status").CanRemove);
        Assert.False(vm.ReportKindChoices.Single(row => row.Id == "certification").CanRemove);
        Assert.False(vm.ProgramKindChoices.Single(row => row.Id == "dut").CanRemove);
        Assert.False(vm.ProgramKindChoices.Single(row => row.Id == "stationHealth").CanRemove);
        Assert.False(vm.CanRemoveSelectedInstrumentSlot);
        foreach (var field in RequiredFieldIds.Known)
        {
            var blocked = Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveRequiredField(field));
            Assert.Contains("serial/partNumber/revision/operator", blocked.Message, StringComparison.Ordinal);
        }

        var status = Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveReportKind("status"));
        Assert.Contains("status and certification", status.Message, StringComparison.Ordinal);
        var certification = Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveReportKind("certification"));
        Assert.Contains("status and certification", certification.Message, StringComparison.Ordinal);
        var dut = Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveProgramKindFromCatalog("dut"));
        Assert.Contains("dut and stationHealth", dut.Message, StringComparison.Ordinal);
        var station = Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveProgramKindFromCatalog("stationHealth"));
        Assert.Contains("dut and stationHealth", station.Message, StringComparison.Ordinal);
        var last = Assert.Throws<AuthoringWorkspaceException>(vm.RemoveSelectedInstrumentSlot);
        Assert.Contains("at least one instrument", last.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_required_field_strips_every_program_and_the_catalog()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("fields-a");
        vm.NewRequiredField = "fixtureId";
        vm.AddRequiredField();
        Assert.True(vm.RequiredFieldChoices.Single(row => row.Id == "fixtureId").CanRemove);
        vm.CreateProgram("fields-b");
        vm.SetRequiredFieldIncluded("fixtureId", true);
        Assert.Contains("fixtureId", vm.SelectedProgram!.Sidecar.RequiredFields!);
        vm.SelectProgram("fields-a");
        vm.Apply();
        vm.SelectProgram("fields-b");
        vm.Apply();
        vm.RemoveRequiredField("fixtureId");
        Assert.DoesNotContain("fixtureId", vm.RequiredFieldOptions);
        Assert.All(vm.Programs, program =>
            Assert.DoesNotContain("fixtureId", RequiredFieldIds.FromSidecar(program.Sidecar)));
        Assert.DoesNotContain("fixtureId", vm.Workspace!.Manifest.Catalogs!.RequiredFields);
        var reloaded = AuthoringWorkspaceLoader.Load(vm.Workspace.Root);
        Assert.DoesNotContain("fixtureId", reloaded.Manifest.Catalogs!.RequiredFields);
        var sidecar = File.ReadAllText(PlanCompiler.SidecarPath(Path.Combine(vm.Workspace.Root, "fields-a.TapPlan")));
        Assert.DoesNotContain("fixtureId", sidecar, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Removed required field fixtureId", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_report_kind_rewrites_defaults_and_forgets_the_catalog()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("reports-a");
        vm.NewReportKind = "traceability";
        vm.AddReportKind();
        vm.DefaultReportKind = "traceability";
        Assert.True(vm.ReportKindChoices.Single(row => row.Id == "traceability").CanRemove);
        vm.CreateProgram("reports-b");
        vm.SetReportKindIncluded("traceability", true);
        vm.DefaultReportKind = "traceability";
        vm.SelectProgram("reports-a");
        vm.Apply();
        vm.SelectProgram("reports-b");
        vm.Apply();
        vm.RemoveReportKind("traceability");
        Assert.DoesNotContain("traceability", vm.ReportKindOptions);
        Assert.All(vm.Programs, program =>
        {
            Assert.DoesNotContain("traceability", program.Sidecar.ReportKinds ?? []);
            Assert.NotEqual("traceability", program.Sidecar.DefaultReportKind);
        });
        Assert.DoesNotContain("traceability", vm.Workspace!.Manifest.Catalogs!.ReportKinds);
        var reloaded = AuthoringWorkspaceLoader.Load(vm.Workspace.Root);
        Assert.DoesNotContain("traceability", reloaded.Manifest.Catalogs!.ReportKinds);
        Assert.All(vm.Programs, program => Assert.Equal("status", program.Sidecar.DefaultReportKind));
        var sidecarPath = PlanCompiler.SidecarPath(Path.Combine(vm.Workspace.Root, "reports-a.TapPlan"));
        Assert.True(File.Exists(sidecarPath), sidecarPath);
        Assert.DoesNotContain("traceability", File.ReadAllText(sidecarPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remove_program_kind_resets_users_to_dut()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("kinds-a");
        vm.NewProgramKind = "incomingInspect";
        vm.AddProgramKind();
        vm.CreateProgram("kinds-b");
        vm.ProgramKind = "incomingInspect";
        vm.SelectProgram("kinds-a");
        vm.Apply();
        vm.SelectProgram("kinds-b");
        vm.Apply();
        Assert.True(vm.ProgramKindChoices.Single(row => row.Id == "incomingInspect").CanRemove);
        vm.RemoveProgramKindFromCatalog("incomingInspect");
        Assert.DoesNotContain("incomingInspect", vm.ProgramKindOptions);
        Assert.Equal("dut", vm.ProgramKind);
        Assert.All(vm.Programs, program =>
            Assert.False(string.Equals(program.Sidecar.ProgramKind, "incomingInspect", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("incomingInspect", vm.ProgramKindOptions);
        Assert.DoesNotContain("incomingInspect", vm.Workspace!.Manifest.Catalogs!.ProgramKinds);
        var reloaded = AuthoringWorkspaceLoader.Load(vm.Workspace.Root);
        Assert.DoesNotContain("incomingInspect", reloaded.Manifest.Catalogs!.ProgramKinds);
        var sidecar = File.ReadAllText(PlanCompiler.SidecarPath(Path.Combine(vm.Workspace.Root, "kinds-b.TapPlan")));
        Assert.DoesNotContain("incomingInspect", sidecar, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remove_instrument_slot_strips_cleanup_and_forgets_unused_catalog_names()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-delete");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        var cleanup = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Cleanup);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(cleanup));
        vm.SetCleanupSlotIncluded("SCOPE", true);
        Assert.Contains("SCOPE", vm.SelectedProgram!.Cleanup.InstrumentSlots);
        vm.Apply();
        vm.SelectedInstrumentSlot = "SCOPE";
        Assert.True(vm.CanRemoveSelectedInstrumentSlot);
        vm.RemoveSelectedInstrumentSlot();
        Assert.Equal(["DMM"], vm.InstrumentSlots);
        Assert.DoesNotContain("SCOPE", vm.SelectedProgram.Cleanup.InstrumentSlots);
        Assert.DoesNotContain("SCOPE", vm.Workspace!.Manifest.Catalogs!.InstrumentSlotNames);
        Assert.Equal("DMM", vm.SelectedInstrumentSlot);
        Assert.False(vm.CanRemoveSelectedInstrumentSlot);
        var sidecar = File.ReadAllText(PlanCompiler.SidecarPath(Path.Combine(vm.Workspace.Root, "slots-delete.TapPlan")));
        Assert.DoesNotContain("SCOPE", sidecar, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_instrument_slot_retargets_identity_and_measure_to_remaining_slot()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-retarget");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var acquire = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Metric);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(acquire));
        Assert.Equal("DMM", vm.MetricInstrumentSlot);
        vm.SelectedInstrumentSlot = "DMM";
        Assert.False(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(vm.SelectedProgram));
        Assert.True(vm.CanRemoveSelectedInstrumentSlot);
        vm.RemoveSelectedInstrumentSlot();
        Assert.Equal(["SCOPE"], vm.InstrumentSlots);
        var identity = Assert.Single(vm.SelectedProgram!.Setup.OfType<IdentitySetup>());
        Assert.Equal("SCOPE", identity.InstrumentSlot);
        var measure = Assert.IsType<MeasureSource>(Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram.Measure)).Metric.Source);
        Assert.Equal("SCOPE", measure.InstrumentSlot);
        Assert.Equal("SCOPE", vm.SelectedInstrumentSlot);
        Assert.Contains("Removed slot DMM", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_instrument_slot_retargets_nested_measure_sources()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-nested");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        vm.SelectedInstrumentSlot = "DMM";
        Assert.True(vm.CanRemoveSelectedInstrumentSlot);
        vm.RemoveSelectedInstrumentSlot();
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure));
        var nested = Assert.IsType<MetricNode>(Assert.Single(repeat.Children));
        var measure = Assert.IsType<MeasureSource>(nested.Metric.Source);
        Assert.Equal("SCOPE", measure.InstrumentSlot);
        Assert.Equal("SCOPE", Assert.Single(vm.SelectedProgram.Setup.OfType<IdentitySetup>()).InstrumentSlot);
    }

    [Fact]
    public void Remove_instrument_slot_fails_closed_on_raw_and_unknown_nodes()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-refs");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        vm.ReplaceSelected(vm.SelectedProgram! with
        {
            Measure = [new RawStepNode("OpenTap.Unknown", "<step/>")],
        });
        vm.SelectedInstrumentSlot = "SCOPE";
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(vm.SelectedProgram));
        Assert.False(vm.CanRemoveSelectedInstrumentSlot);
        var blocked = Assert.Throws<AuthoringWorkspaceException>(vm.RemoveSelectedInstrumentSlot);
        Assert.Contains("raw or unknown", blocked.Message, StringComparison.Ordinal);
        Assert.Contains("SCOPE", vm.InstrumentSlots);
    }

    [Fact]
    public void Remove_instrument_slot_keeps_catalog_when_another_program_still_uses_it()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-keep-a");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        vm.CreateProgram("slots-keep-b");
        Assert.Contains("SCOPE", vm.InstrumentSlots);
        vm.SelectProgram("slots-keep-a");
        vm.SelectedInstrumentSlot = "SCOPE";
        vm.RemoveSelectedInstrumentSlot();
        Assert.DoesNotContain("SCOPE", vm.InstrumentSlots);
        Assert.Contains("SCOPE", vm.Workspace!.Manifest.Catalogs!.InstrumentSlotNames);
        vm.SelectProgram("slots-keep-b");
        Assert.Contains("SCOPE", vm.InstrumentSlots);
    }

    [Fact]
    public void Blank_catalog_deletes_are_no_ops()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("blank");
        vm.RemoveRequiredField("   ");
        vm.RemoveReportKind(string.Empty);
        vm.RemoveProgramKindFromCatalog("  ");
        Assert.Null(vm.Error);
        Assert.Contains(RequiredFieldIds.Serial, vm.RequiredFieldOptions);
        Assert.Contains("status", vm.ReportKindOptions);
    }

    [Fact]
    public void Unknown_measure_nodes_fail_closed()
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("raw");
        draft = draft with
        {
            Instruments =
            [
                draft.Instruments[0],
                new InstrumentRef("SCOPE", draft.Instruments[0].TypeId, "MOCK::SCOPE"),
            ],
            Measure = [new RawStepNode("OpenTap.Unknown", "<step/>")],
        };
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(draft));
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(null));
    }

    [Fact]
    public void Nested_raw_and_unknown_setup_fail_closed()
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("nested-raw");
        draft = draft with
        {
            Instruments =
            [
                draft.Instruments[0],
                new InstrumentRef("SCOPE", draft.Instruments[0].TypeId, "MOCK::SCOPE"),
            ],
            Measure = [new RepeatNode(2, [new RawStepNode("OpenTap.Unknown", "<step/>")])],
        };
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(draft));

        var unknownSetup = draft with { Setup = [new MysterySetup()] };
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(unknownSetup));
        Assert.False(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(
            draft with { Measure = [], Setup = [new OperatorPromptSetup("Prompt", "ok")] }));
    }

    [Fact]
    public void Remove_does_not_materialize_implicit_sidecar_arrays()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("implicit");
        vm.SelectedProgram!.Sidecar.ReportKinds = null;
        vm.SelectedProgram.Sidecar.RequiredFields = null;
        vm.NewReportKind = "traceability";
        vm.AddReportKind();
        vm.SetReportKindIncluded("traceability", false);
        vm.SelectedProgram.Sidecar.ReportKinds = null;
        vm.RemoveReportKind("traceability");
        Assert.Null(vm.SelectedProgram.Sidecar.ReportKinds);
        vm.RemoveRequiredField("fixtureId");
        Assert.Null(vm.SelectedProgram.Sidecar.RequiredFields);
        vm.SelectedProgram.Sidecar.DefaultReportKind = "traceability";
        vm.SelectedProgram.Sidecar.ReportKinds = null;
        vm.RemoveReportKind("traceability");
        Assert.Null(vm.SelectedProgram.Sidecar.ReportKinds);
        Assert.Equal("status", vm.SelectedProgram.Sidecar.DefaultReportKind);
    }

    [Fact]
    public void Duplicate_slot_names_cannot_delete_the_last_remaining_name()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("dup-slots");
        var typeId = vm.SelectedProgram!.Instruments[0].TypeId;
        vm.ReplaceSelected(vm.SelectedProgram with
        {
            Setup = [new OperatorPromptSetup("Prompt", "ok")],
            Instruments =
            [
                new InstrumentRef("SCOPE", typeId, "MOCK::SCOPE0"),
                new InstrumentRef("SCOPE", typeId, "MOCK::SCOPE1"),
            ],
        });
        vm.SelectedInstrumentSlot = "SCOPE";
        Assert.False(vm.CanRemoveSelectedInstrumentSlot);
        var blocked = Assert.Throws<AuthoringWorkspaceException>(vm.RemoveSelectedInstrumentSlot);
        Assert.Contains("at least one instrument", blocked.Message, StringComparison.Ordinal);
        Assert.Equal(2, vm.SelectedProgram.Instruments.Count);
    }

    [Fact]
    public void Remove_program_kind_can_forget_an_unselected_catalog_entry()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("kinds-unselected");
        vm.NewProgramKind = "incomingInspect";
        vm.AddProgramKind();
        vm.ProgramKind = "dut";
        Assert.True(vm.ProgramKindChoices.Single(row => row.Id == "incomingInspect").CanRemove);
        vm.RemoveProgramKindFromCatalog("incomingInspect");
        Assert.DoesNotContain("incomingInspect", vm.ProgramKindOptions);
        Assert.Equal("dut", vm.ProgramKind);
    }

    [Fact]
    public void Catalog_delete_does_not_invent_sidecar_for_unsaved_program()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("unsaved");
        vm.NewReportKind = "traceability";
        vm.AddReportKind();
        vm.RemoveReportKind("traceability");
        Assert.Empty(Directory.EnumerateFiles(vm.Workspace!.Root, "*.TapPlan", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(vm.Workspace.Root, "*.program.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Remove_selected_program_deletes_orphan_sidecar_without_a_tapplan()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("orphan");
        var invented = Path.Combine(vm.Workspace!.Root, "orphan.TapPlan");
        var sidecar = PlanCompiler.SidecarPath(invented);
        File.WriteAllText(sidecar, """{"schemaVersion":1,"displayName":"orphan"}""");
        Assert.True(File.Exists(sidecar));
        Assert.DoesNotContain(
            vm.Workspace.TapPlanPaths,
            path => string.Equals(Path.GetFileNameWithoutExtension(path), "orphan", StringComparison.OrdinalIgnoreCase));
        vm.RemoveSelectedProgram();
        Assert.False(File.Exists(sidecar));
        Assert.False(File.Exists(invented));
        Assert.Empty(vm.Programs);
    }

    [Fact]
    public void Remove_instrument_slot_saves_existing_tapplan_instruments()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("slots-save");
        vm.NewInstrumentSlot = "SCOPE";
        vm.AddInstrumentSlot();
        vm.Apply();
        vm.SelectedInstrumentSlot = "DMM";
        vm.RemoveSelectedInstrumentSlot();
        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(vm.Workspace!.Root);
        reloaded.SelectProgram("slots-save");
        Assert.Equal(["SCOPE"], reloaded.InstrumentSlots);
        Assert.DoesNotContain("DMM", reloaded.SelectedProgram!.Instruments.Select(instrument => instrument.SlotName));
        Assert.Equal("SCOPE", Assert.Single(reloaded.SelectedProgram.Setup.OfType<IdentitySetup>()).InstrumentSlot);
    }

    [Fact]
    public void Retarget_slot_rewrites_identity_and_is_a_no_op_for_the_same_name()
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("retarget");
        var typeId = draft.Instruments[0].TypeId;
        draft = draft with
        {
            Instruments =
            [
                draft.Instruments[0],
                new InstrumentRef("SCOPE", typeId, "MOCK::SCOPE"),
            ],
        };
        var moved = AuthoringInstrumentUsage.RetargetSlot(draft, "DMM", "SCOPE");
        Assert.Equal("SCOPE", Assert.Single(moved.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.Same(draft, AuthoringInstrumentUsage.RetargetSlot(draft, "DMM", "DMM"));
        Assert.Same(draft, AuthoringInstrumentUsage.RetargetSlot(draft, "  ", "SCOPE"));
    }

    private sealed record MysterySetup : SetupAction;

    private static AuthoringWorkspaceViewModel OpenEmpty()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-setdel-" + Guid.NewGuid().ToString("N"));
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
