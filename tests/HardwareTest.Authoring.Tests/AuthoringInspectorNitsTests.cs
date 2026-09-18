using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringInspectorNitsTests
{
    [Fact]
    public void Cleanup_and_formula_editors_no_op_when_identity_is_selected()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("gate");
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        var identity = vm.SequenceItems.Single(row => row.Label == "Identity Check");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(identity));
        Assert.False(vm.HasFormula);
        Assert.False(vm.HasCleanupEditor);
        Assert.Equal(string.Empty, vm.FormulaSource);
        vm.FormulaSource = "std(VDC)";
        Assert.Equal(string.Empty, vm.FormulaSource);
        var before = vm.SelectedProgram!.Cleanup.InstrumentSlot;
        vm.CleanupInstrumentSlot = "SCOPE";
        Assert.Equal(before, vm.SelectedProgram.Cleanup.InstrumentSlot);
        vm.IncludeSafeShutdown = false;
        Assert.True(vm.SelectedProgram.Cleanup.IncludeSafeShutdown);
    }

    [Fact]
    public void Import_tf_json_applies_to_the_selected_transfer_function_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("tf-apply");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
        var count = vm.SelectedProgram!.Measure.Count;
        vm.ImportTransferFunction(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "tf", "model.valid.json"));
        Assert.Equal(count, vm.SelectedProgram.Measure.Count);
        Assert.Equal("VDC.filt", vm.ChannelKey);
        Assert.True(vm.HasTransferFunction);
    }

    [Fact]
    public void Input_field_ids_edit_the_selected_operator_input_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("input-ids");
        vm.ApplyRecipe(AuthoringRecipeIds.Input);
        var input = vm.SequenceItems.Single(row => row.Label == "Operator Input");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(input));
        Assert.True(vm.HasInputSetup);
        vm.InputStringFieldId = "fixtureId";
        vm.InputNumberFieldId = "torqueNm";
        var setup = Assert.IsType<OperatorInputSetup>(vm.SelectedSetup);
        Assert.Equal("fixtureId", setup.StringFieldId);
        Assert.Equal("torqueNm", setup.NumberFieldId);
    }

    [Fact]
    public void SelectedProgram_setter_switches_the_session_program()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("alpha");
        vm.CreateProgram("beta");
        vm.SelectedProgram = vm.Programs.Single(p => p.PlanId == "alpha");
        Assert.Equal("alpha", vm.SelectedProgram?.PlanId);
        vm.SelectedProgram = vm.Programs.Single(p => p.PlanId == "beta");
        Assert.Equal("beta", vm.SelectedProgram?.PlanId);
    }

    [Fact]
    public void Raw_xml_is_empty_until_a_raw_row_is_selected()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("raw-gate");
        vm.ReplaceSelected(vm.SelectedProgram! with
        {
            Measure = [new RawStepNode("HangForeverStep", "<TestStep />")],
        });
        var raw = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Raw);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(raw));
        Assert.True(vm.HasRawStep);
        Assert.Equal("HangForeverStep", vm.RawTypeName);
        var identity = vm.SequenceItems.Single(row => row.Label == "Identity Check");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(identity));
        Assert.False(vm.HasRawStep);
        Assert.Equal(string.Empty, vm.RawXml);
    }

    private static AuthoringWorkspaceViewModel OpenEmpty()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-nits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
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
