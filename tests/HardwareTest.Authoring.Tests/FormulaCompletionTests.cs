using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class FormulaCompletionTests
{
    [Fact]
    public void IdentAt_uses_dotted_channel_keys_as_one_span()
    {
        var span = FormulaCatalog.IdentAt("mean(VDC.mean)", 10);
        Assert.Equal("VDC.mean", span.Text);
        Assert.Equal(5, span.Start);
        Assert.Equal(8, span.Length);
    }

    [Fact]
    public void CompletionsFor_filters_prefix_and_never_offers_fft()
    {
        var items = FormulaCatalog.CompletionsFor("me", ["VDC", "fft"]);
        Assert.Contains(items, item => item.Name == "mean" && item.Packs);
        Assert.Contains(items, item => item.Name == "median" && !item.Packs);
        Assert.DoesNotContain(items, item => item.Name == "std");
        Assert.DoesNotContain(items, item => item.Name == "fft");
        Assert.DoesNotContain(FormulaCatalog.Completions(["fft"]), item => item.Name == "fft");
        Assert.DoesNotContain(
            FormulaCatalog.Completions(["FFT"]),
            item => item.Name.Equals("fft", StringComparison.OrdinalIgnoreCase));
        Assert.True(FormulaCatalog.IsReservedUnknown("FFT"));
    }

    [Fact]
    public void ApplyFormulaCompletion_replaces_the_ident_under_the_caret()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("complete");
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.FormulaSource = "me";
        var caret = vm.ApplyFormulaCompletion("mean(", 2);
        Assert.Equal("mean(", vm.FormulaSource);
        Assert.Equal(5, caret);
        Assert.Contains(vm.CompletionsAt(5), item => item.Name == "mean");
    }

    [Fact]
    public void ApplyFormulaCompletion_replaces_a_partial_ident_inside_a_call()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("mid-complete");
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.FormulaSource = "std(me)";
        var caret = vm.ApplyFormulaCompletion("mean(", 6);
        Assert.Equal("std(mean()", vm.FormulaSource);
        Assert.Equal(9, caret);
    }

    [Fact]
    public void ApplyFormulaCompletion_no_ops_when_formula_is_not_selected()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("no-formula");
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        var identity = vm.SequenceItems.Single(row => row.Label == "Identity Check");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(identity));
        Assert.Equal(0, vm.ApplyFormulaCompletion("mean(", 0));
        Assert.Equal(string.Empty, vm.FormulaSource);
        Assert.Empty(vm.CompletionsAt(0));
    }

    private static AuthoringWorkspaceViewModel OpenEmpty()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-complete-" + Guid.NewGuid().ToString("N"));
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
